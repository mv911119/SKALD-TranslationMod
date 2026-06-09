using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TranslationMod.Configuration;

namespace TranslationMod
{
    /// <summary>
    /// 翻译服务。
    /// 负责加载 CSV 翻译表，按句切分输入文本，并结合缓存、占位符、物品模式、
    /// 玩家名称和性别占位符等规则生成最终译文。
    /// </summary>
    public sealed class TranslationService
    {
        private readonly Dictionary<string, string> _dict;
        private readonly Dictionary<string, string> _translationCache = new();
        private readonly HashSet<string> _missingKeys = new();
        private readonly HashSet<string> _loggedInputs = new();
        private readonly HashSet<string> _loggedTitleCaseHits = new();
        private readonly HashSet<string> _loggedItemPatternHits = new();
        private readonly HashSet<string> _loggedItemListHits = new();
        private readonly HashSet<string> _loggedApostropheHits = new();

        private readonly object _lockObject = new();
        private readonly string _missingKeysFilePath;
        
        /// <summary>
        /// 用于匹配 `{ITEM}` 占位符模板的正则规则集合。
        /// </summary>
        private readonly List<(Regex regex, string template)> _itemPatterns;

        /// <summary>
        /// 判断字符串是否为“全大写风格”文本。
        /// 要求字符串只由大写字母、数字、空白和标点构成，且至少包含一个大写字母。
        /// </summary>
        //private static readonly Regex AllUpperCaseRegex = new Regex(@"^(?=.*[A-Z])[A-Z0-9 \p{P}]+$", RegexOptions.Compiled);
        private static readonly Regex AllUpperCaseRegex =
            new(@"^(?=.*\p{Lu})[0-9\p{P}\p{Lu}\s]+$", RegexOptions.CultureInvariant);
        /// <summary>
        /// 构造翻译服务。
        /// 流程：加载全部翻译 CSV，准备缺失键输出路径，再预编译物品模板匹配规则。
        /// </summary>
        public TranslationService() 
        {
            _dict = LoadCsv(GetCsvFiles());
            
            _missingKeysFilePath = GetMissingKeysFilePath();
            
            // 预构建 `{ITEM}` 模式，避免翻译时重复生成正则
            _itemPatterns = CreateItemPatterns(_dict);
            
            // 初始化翻译缓存相关数据
#if DEBUG
        TranslationMod.Logger?.LogInfo($"[TranslationService] Initialized with {_dict.Count} translations loaded from CSV files");
        TranslationMod.Logger?.LogInfo($"[TranslationService] Created {_itemPatterns.Count} item patterns for {{ITEM}} placeholders");
        TranslationMod.Logger?.LogInfo($"[TranslationService] Missing keys will be saved to: {_missingKeysFilePath}");
#endif
        }

        /// <summary>
        /// 翻译任意输入文本。
        /// 流程：先查缓存，再尝试诗句模式；若不是诗句，则交给文本解析器拆句，
        /// 分句翻译后再按原模板拼回，失败时回退原文。
        /// </summary>
        /// <param name="input">原始文本</param>
        /// <returns>翻译后的文本</returns>
        public string Process(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            // 先检查翻译缓存
            lock (_lockObject)
            {
                if (_translationCache.TryGetValue(input, out string cachedResult))
                {
                    return cachedResult;
                }
            }
            
#if DEBUG
            if (!_loggedInputs.Contains(input))
            {
                _loggedInputs.Add(input);
                TranslationMod.Logger?.LogDebug($"[TranslationService] Processing new input: '{input}'");
            }
#endif
            
            try
            {
                // 先尝试按“四行诗句”处理，避免解析器破坏原有换行结构
                var verseResult = TryTranslateAsVerse(input);
                if (verseResult != null)
                {
                    lock (_lockObject)
                    {
                        if (!_translationCache.ContainsKey(input))
                        {
                            _translationCache[input] = verseResult;
                        }
                    }
#if DEBUG
                    LogProcessTrace(input, verseResult);
#endif
                    return verseResult;
                }

                // 使用 GameTextParser 将文本拆分为多个片段
                var sentences = GameTextParser.Parse(input);
                
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[TranslationService] PARSER INPUT: '{input}'");
                for (int i = 0; i < sentences.Count; i++)
                {
                    TranslationMod.Logger?.LogInfo($"[TranslationService] PARSER SENTENCE[{i}]: '{sentences[i]}'");
                }
#endif
                
                // 根据原始输入生成模板
                var template = CreateTemplate(input, sentences);
                
                var translatedSentences = new List<string>();
                foreach (var sentence in sentences)
                {      
                    string translatedSentence = TranslateSentence(sentence);
                    translatedSentences.Add(translatedSentence);
                }

                // 将翻译后的句子回填到模板中
                var result = ApplyTemplate(template, translatedSentences);
                
                // 如果模板回填失败但分句本身已有翻译，则直接拼接分句结果作为回退
                if (string.Equals(result, input, StringComparison.Ordinal))
                {
                    bool anyTranslated = false;
                    for (int i = 0; i < sentences.Count; i++)
                    {
                        if (!string.Equals(sentences[i], translatedSentences[i], StringComparison.Ordinal))
                        {
                            anyTranslated = true;
                            break;
                        }
                    }
                    
                    if (anyTranslated)
                    {
                        // 单句时直接采用译文
                        if (translatedSentences.Count == 1)
                        {
                            result = translatedSentences[0];
                        }
                        else
                        {
                            // 多句时用换行拼接，尽量保留多行结构
                            result = string.Join("\n", translatedSentences);
                        }
#if DEBUG
                        TranslationMod.Logger?.LogInfo($"[TranslationService] Template fallback: {translatedSentences.Count} translated sentences, result='{result}'");
#endif
                    }
                }

                // 写回缓存，避免重复翻译
                lock (_lockObject)
                {
                    if (!_translationCache.ContainsKey(input))
                    {
                        _translationCache[input] = result;
                    }
                }
#if DEBUG
                LogProcessTrace(input, result);
#endif                
                return result;
            }
            catch (Exception ex)
            {
                // 解析器失败时直接回退原文
                TranslationMod.Logger?.LogWarning($"GameTextParser failed for input '{input}': {ex.Message}, returning original text");
                
                // 同时缓存原文，避免后续反复抛错
                lock (_lockObject)
                {
                    if (!_translationCache.ContainsKey(input))
                    {
                        _translationCache[input] = input;
                    }
                }
#if DEBUG                
                LogProcessTrace(input, input);
#endif
                return input;
            }
        }

        private static void LogProcessTrace(string input, string result)
        {
            try
            {
                TranslationMod.Logger?.LogInfo(
                    $"[exec_chain]: {Environment.StackTrace}\n[input]: {input}\n[translated]: {result}\n");
            }
            catch
            {
            }
        }

        /// <summary>
        /// 按四行诗句模式尝试翻译文本。
        /// 只有输入包含换行且恰好能提取出 4 个有效文本行时才生效，
        /// 每行独立翻译，至少命中一行才返回结果。
        /// </summary>
        private string TryTranslateAsVerse(string input)
        {
            if (!input.Contains("\n"))
                return null;

            var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var nonEmptyLines = new List<string>();
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                // 跳过空行和不含文字的装饰行
                if (trimmed.Length > 0 && Regex.IsMatch(trimmed, @"\p{L}"))
                    nonEmptyLines.Add(trimmed);
            }

            if (nonEmptyLines.Count != 4)
                return null;

            var translatedLines = new List<string>();
            int translatedCount = 0;

            foreach (var line in nonEmptyLines)
            {
                string translated = TranslateSentence(line);
                bool wasTranslated = !string.Equals(line, translated, StringComparison.Ordinal);

                if (wasTranslated)
                    translatedCount++;

                translatedLines.Add(translated);
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[Verse] '{line}' -> '{translated}' [found={wasTranslated}]");
#endif
            }

            if (translatedCount > 0)
            {
                string result = string.Join("\n", translatedLines);
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[Verse] Result ({translatedCount}/4): '{result}'");
#endif
                return result;
            }

            return null;
        }

        /// <summary>
        /// 翻译单句文本。
        /// 流程：优先查字典直译，再依次尝试引号变体、全大写回退、玩家名替换、
        /// `{ITEM}` 模板和物品列表翻译，最后处理性别占位符。
        /// </summary>
        /// <param name="sentence">待翻译句子</param>
        /// <returns>翻译结果</returns>
        private string TranslateSentence(string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return sentence;

            // 先尝试精确匹配整句翻译
            string translated;
            bool foundDirectTranslation = _dict.TryGetValue(sentence, out translated);
            
            if (!foundDirectTranslation)
            {
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[TranslationService] Key not found: '{sentence}'");
#endif
                
                if (sentence.Contains("’"))
                {
                    string sentenceWithCurlyApostrophe = sentence.Replace("’", "\'");
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Sentence contains straight apostrophe. Trying with curly apostrophe: '{sentenceWithCurlyApostrophe}'");
#endif
                    if (_dict.TryGetValue(sentenceWithCurlyApostrophe, out string apostropheTranslated))
                    {
                        // 使用替换后的撇号版本命中翻译
                        translated = apostropheTranslated;
                        
                        // 记录这种命中路径，便于后续补词条
                        LogApostropheHit(sentence, sentenceWithCurlyApostrophe, translated);
                        
                        // 返回前处理性别占位符
                        translated = ProcessGenderPlaceholder(translated);
                        return translated;
                    }
                }
                
                // 全大写文本再尝试一次 Title Case 回退查找
                if (IsAllUpperCase(sentence))
                {
                    string titleCaseVersion = ConvertToTitleCase(sentence);

#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Key is CAPS. TitleCase: '{titleCaseVersion}'");
#endif
                    if (_dict.TryGetValue(titleCaseVersion, out string titleCaseTranslated))
                    {
                        // 命中后再转回全大写风格
                        translated = titleCaseTranslated.ToUpper();
                        
                        // 记录 Title Case 命中
                        LogTitleCaseHit(sentence, titleCaseVersion, translated);
                        return translated;
                    }
                }
                
                // 尝试玩家名占位符替换
                string playerNameReplacement = TryReplacePlayerName(sentence);
                if (playerNameReplacement != null)
                {
                    // 玩家名替换方案命中
                    return playerNameReplacement;
                }
                // 尝试 `{ITEM}` 模板匹配
                string itemPatternReplacement = TryMatchItemPattern(sentence);
                if (itemPatternReplacement != null)
                {
                    // 物品模板命中
                    translated = itemPatternReplacement;
                }
                else
                {
                    // 尝试把句子识别为逗号分隔的物品列表
                    string itemListReplacement = TryTranslateItemList(sentence);
                    if (itemListReplacement != null)
                    {
                        // 物品列表翻译命中
                        translated = itemListReplacement;
                    }
                    else
                    {
                        translated = sentence;
                        SaveMissingKey(sentence);
                    }
                }
            }

            // 统一处理译文中的性别占位符
            translated = ProcessGenderPlaceholder(translated);

            return translated;
        }

        /// <summary>
        /// 记录缺失翻译键到 `need_translate.csv`。
        /// 流程：先做去重，再确保目录存在，最后以追加方式写入文件。
        /// </summary>
        private void SaveMissingKey(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_missingKeysFilePath)) 
                return;

            lock (_lockObject)
            {
                // 同一个缺失键只记录一次
                if (_missingKeys.Contains(key)) 
                    return;

                _missingKeys.Add(key);

                try
                {
                    // 输出目录不存在时自动创建
                    string directory = Path.GetDirectoryName(_missingKeysFilePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
#if DEBUG
                        TranslationMod.Logger?.LogInfo($"[TranslationService] Created directory for missing keys: '{directory}'");
#endif
                    }

                    // 以追加模式写入缺失键
                    using (var writer = new StreamWriter(_missingKeysFilePath, true, System.Text.Encoding.UTF8))
                    {
                        // 先做 CSV 转义
                        string escapedKey = EscapeCsvValue(key);
                        writer.WriteLine($"{escapedKey},");
                    }
                    
                    // 记录新增缺失项
                    TranslationMod.Logger?.LogWarning($"[TranslationService] Missing translation key added to need_translate.csv: '{key}'");
                }
                catch (Exception ex)
                {
                    // 写盘失败只记日志，不阻断翻译流程
                    TranslationMod.Logger?.LogError($"[TranslationService] Failed to save missing translation key '{key}': {ex.Message}");
                }
            }
        }

        /// <summary>记录通过 Title Case 命中的翻译日志（带去重）。</summary>
        private void LogTitleCaseHit(string original, string titleCaseVersion, string finalTranslation)
        {
#if DEBUG
            lock (_lockObject)
            {
                if (!_loggedTitleCaseHits.Contains(original))
                {
                    _loggedTitleCaseHits.Add(original);
                TranslationMod.Logger?.LogInfo($"[TranslationService] Title Case hit: '{original}' -> '{titleCaseVersion}' -> '{finalTranslation}'");

                }
            }
#endif
        }

        /// <summary>记录通过 `{ITEM}` 模式命中的翻译日志（带去重）。</summary>
        private void LogItemPatternHit(string original, string pattern, string item, string finalTranslation)
        {
#if DEBUG
            lock (_lockObject)
            {
                if (!_loggedItemPatternHits.Contains(original))
                {
                    _loggedItemPatternHits.Add(original);
                TranslationMod.Logger?.LogInfo($"[TranslationService] Item pattern hit: '{original}' -> pattern '{pattern}' -> item '{item}' -> '{finalTranslation}'");
                }
            }
#endif
        }

        /// <summary>记录物品列表翻译命中的日志（带去重）。</summary>
        private void LogItemListHit(string original, int itemCount, int translatedCount, string finalTranslation)
        {
#if DEBUG
            lock (_lockObject)
            {
                if (!_loggedItemListHits.Contains(original))
                {
                    _loggedItemListHits.Add(original);
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Item list hit: '{original}' -> {itemCount} items ({translatedCount} translated) -> '{finalTranslation}'");
                }
            }
#endif
        }

        /// <summary>记录通过撇号替换命中的翻译日志（带去重）。</summary>
        private void LogApostropheHit(string original, string apostropheVersion, string finalTranslation)
        {
#if DEBUG
            lock (_lockObject)
            {
                if (!_loggedApostropheHits.Contains(original))
                {
                    _loggedApostropheHits.Add(original);
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Apostrophe hit: '{original}' -> '{apostropheVersion}' -> '{finalTranslation}'");
                }
            }
#endif
        }

        /// <summary>
        /// 尝试将玩家名替换为 `{PLAYER}` 占位符后查找翻译。
        /// </summary>
        /// <param name="sentence">原始句子</param>
        /// <returns>恢复玩家名后的译文；若未命中则返回 null</returns>
        private string TryReplacePlayerName(string sentence)
        {
            try
            {
                string playerName = GetCurrentPlayerName();
                if (string.IsNullOrEmpty(playerName))
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Player name is empty or null");
#endif
                    return null;
                }

                // 检查句子中是否包含玩家名
                if (!sentence.Contains(playerName))
                {
#if DEBUG
                TranslationMod.Logger?.LogDebug($"[TranslationService] Sentence does not contain player name '{playerName}'");
#endif
                    return null;
                }

                // 将玩家名替换为占位符
                string sentenceWithPlaceholder = sentence.Replace(playerName, "{PLAYER}");
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[TranslationService] Checking player name replacement: '{sentence}' -> '{sentenceWithPlaceholder}'");
#endif

                // 查找带占位符版本的翻译
                if (_dict.TryGetValue(sentenceWithPlaceholder, out string translatedWithPlaceholder))
                {
                    // 将译文中的占位符还原成玩家名
                    string finalTranslation = translatedWithPlaceholder.Replace("{PLAYER}", playerName);
                    
                    // 处理最终译文中的 {IFHE} 占位符
                    finalTranslation = ProcessGenderPlaceholder(finalTranslation);
                                        
                    return finalTranslation;
                }

#if DEBUG
                TranslationMod.Logger?.LogDebug($"[TranslationService] No translation found for player placeholder: '{sentenceWithPlaceholder}'");
#endif
                return null;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error in TryReplacePlayerName: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取当前玩家名称
        /// </summary>
        /// <returns>玩家名；出错时返回 null</returns>
        private static string GetCurrentPlayerName()
        {
            try
            {
                var dataControl = MainControl.getDataControl();
                if (dataControl == null)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] DataControl is null");
#endif
                    return null;
                }

                var currentPC = dataControl.getCurrentPC();
                if (currentPC == null)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Current PC is null");
#endif
                    return null;
                }

                string playerName = currentPC.getName();
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Retrieved player name: '{playerName}'");
#endif
                return playerName;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error getting player name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取当前玩家性别
        /// </summary>
        /// <returns>男性返回 true，女性返回 false，出错时返回 null</returns>
        private static bool? GetCurrentPlayerGender()
        {
            try
            {
                var dataControl = MainControl.getDataControl();
                if (dataControl == null)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] DataControl is null for gender check");
#endif
                    return null;
                }

                var currentPC = dataControl.getCurrentPC();
                if (currentPC == null)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Current PC is null for gender check");
#endif
                    return null;
                }

                bool isMale = currentPC.isCharacterMale();
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Retrieved player gender: {(isMale ? "male" : "female")}");
#endif
                return isMale;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error getting player gender: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 处理译文中的 `{IFHE 男性文本 | 女性文本}` 占位符。
        /// </summary>
        /// <param name="translation">可能包含 `{IFHE}` 占位符的译文</param>
        /// <returns>替换完成后的结果字符串</returns>
        private static string ProcessGenderPlaceholder(string translation)
        {
            if (string.IsNullOrEmpty(translation) || !translation.Contains("{IFHE"))
            {
                return translation;
            }

            try
            {
                // 用于匹配 {IFHE text1 | text2} 占位符的正则
                var genderRegex = new Regex(@"\{IFHE\s+([^|]+?)\s*\|\s*([^}]+?)\s*\}", RegexOptions.CultureInvariant);
                
                string result = translation;
                var matches = genderRegex.Matches(translation);
                
                if (matches.Count > 0)
                {
                    bool? playerGender = GetCurrentPlayerGender();
                    
                    if (playerGender.HasValue)
                    {
                        foreach (Match match in matches)
                        {
                            string maleText = match.Groups[1].Value.Trim();
                            string femaleText = match.Groups[2].Value.Trim();
                            
                            // 根据玩家性别选择对应文本
                            string selectedText = playerGender.Value ? maleText : femaleText;
                            
                            // 将占位符替换为选中的文本
                            result = result.Replace(match.Value, selectedText);
                            
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Gender placeholder processed: '{match.Value}' -> '{selectedText}' (player is {(playerGender.Value ? "male" : "female")})");
#endif
                        }
                    }
                    else
                    {
                        // 如果无法确定玩家性别，则默认使用男性文本
                        foreach (Match match in matches)
                        {
                            string maleText = match.Groups[1].Value.Trim();
                            result = result.Replace(match.Value, maleText);
                            
#if DEBUG
                    TranslationMod.Logger?.LogWarning($"[TranslationService] Gender placeholder defaulted to male: '{match.Value}' -> '{maleText}' (could not determine player gender)");
#endif
                        }
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error processing gender placeholder in '{translation}': {ex.Message}");
                return translation;
            }
        }

        /// <summary>
        /// 创建用于处理 `{ITEM}` 占位符的正则模式列表。
        /// </summary>
        /// <param name="dict">翻译字典</param>
        /// <returns>模式及对应模板的列表</returns>
        private static List<(Regex regex, string template)> CreateItemPatterns(Dictionary<string, string> dict)
        {
            var patterns = new List<(Regex regex, string template)>();
            
            foreach (var kvp in dict)
            {
                if (kvp.Key.Contains("{ITEM}"))
                {
                    try
                    {
                        // 统计键中 `{ITEM}` 的出现次数
                        int itemCount = 0;
                        string tempKey = kvp.Key;
                        
                        // 将每个 `{ITEM}` 替换成唯一的临时标记
                        while (tempKey.Contains("{ITEM}"))
                        {
                            // 每次只替换第一个 `{ITEM}`
                            int index = tempKey.IndexOf("{ITEM}");
                            if (index >= 0)
                            {
                                tempKey = tempKey.Substring(0, index) + 
                                         $"___ITEM_PLACEHOLDER_{itemCount}___" + 
                                         tempKey.Substring(index + 6); // 6 = "{ITEM}".Length
                            }
                            itemCount++;
                        }
                        
                        // 使用 Regex.Escape 进行安全转义
                        string escapedKey = Regex.Escape(tempKey);
                        
                        // 将每个临时标记替换为独立的正则捕获组
                        for (int i = 0; i < itemCount; i++)
                        {
                            escapedKey = escapedKey.Replace($"___ITEM_PLACEHOLDER_{i}___", "(.+?)");
                        }
                        
                        string pattern = "^" + escapedKey + "$";
                        
                        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
                        patterns.Add((regex, kvp.Value));
                        
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[TranslationService] Created ITEM pattern: key='{kvp.Key}' (items: {itemCount}) -> pattern='{pattern}' -> template='{kvp.Value}'");
#endif
                    }
                    catch (Exception ex)
                    {
                        TranslationMod.Logger?.LogError($"[TranslationService] Error creating pattern for key '{kvp.Key}': {ex.Message}");
                    }
                }
            }
            
            return patterns;
        }

        /// <summary>
        /// 尝试匹配 `{ITEM}` 模式并生成译文。
        /// </summary>
        /// <param name="sentence">原始句子</param>
        /// <returns>命中时返回译文，否则返回 null</returns>
        private string TryMatchItemPattern(string sentence)
        {
            try
            {
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[TranslationService] Checking {_itemPatterns.Count} item patterns for: '{sentence}'");
#endif
                
                // 先尝试直接匹配
                string directMatch = TryMatchItemPatternDirect(sentence, sentence, false);
                if (directMatch != null)
                {
                    return directMatch;
                }
                
                // 若未命中且原文是全大写，再尝试 Title Case 版本
                if (IsAllUpperCase(sentence))
                {
                    string titleCaseVersion = ConvertToTitleCase(sentence);
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[TranslationService] Sentence is CAPS, trying Title Case version: '{titleCaseVersion}'");
#endif
                    
                    string titleCaseMatch = TryMatchItemPatternDirect(titleCaseVersion, sentence, true);
                    if (titleCaseMatch != null)
                    {
                        return titleCaseMatch;
                    }
                }
                
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[TranslationService] No item pattern matched for: '{sentence}'");
#endif
                return null;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error in TryMatchItemPattern: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 执行一次基于 `{ITEM}` 模式的直接匹配。
        /// </summary>
        /// <param name="testSentence">用于匹配模式的测试句子</param>
        /// <param name="originalSentence">用于日志记录的原始句子</param>
        /// <param name="convertToUpper">是否需要把结果转换为全大写</param>
        /// <returns>命中时返回译文，否则返回 null</returns>
        private string TryMatchItemPatternDirect(string testSentence, string originalSentence, bool convertToUpper)
        {
            try
            {
                foreach (var (regex, template) in _itemPatterns)
                {
                    //TranslationMod.Logger?.LogInfo($"[TranslationService] Testing pattern: '{regex}' against '{testSentence}'");
                    
                    var match = regex.Match(testSentence);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        // 提取所有匹配到的物品（不包含第 0 组的完整匹配）
                        var items = new List<string>();
                        for (int i = 1; i < match.Groups.Count; i++)
                        {
                            items.Add(match.Groups[i].Value);
                        }
                        
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[TranslationService] Item pattern matched: '{testSentence}' -> items: [{string.Join(", ", items)}] using template: '{template}'");
#endif
                        
                        // 分别翻译每个匹配到的物品
                        var translatedItems = new List<string>();
                        foreach (string item in items)
                        {
                            string translatedItem = TranslateItemDirectly(item);
                            translatedItems.Add(translatedItem);
                        }
                        
                        // 用对应的译名替换模板中的每个 `{ITEM}`
                        string finalTranslation = template;
                        for (int i = 0; i < translatedItems.Count; i++)
                        {
                            // 找到第一个 `{ITEM}` 并将其替换
                            int index = finalTranslation.IndexOf("{ITEM}");
                            if (index >= 0)
                            {
                                finalTranslation = finalTranslation.Substring(0, index) + 
                                                 translatedItems[i] + 
                                                 finalTranslation.Substring(index + 6); // 6 = "{ITEM}".Length
                            }
                        }
                        
                        // 处理最终译文中的 `{IFHE}` 占位符
                        finalTranslation = ProcessGenderPlaceholder(finalTranslation);
                        
                        // 如有需要，再将结果转为全大写
                        if (convertToUpper)
                        {
                            finalTranslation = finalTranslation.ToUpper();
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Converted result to CAPS: '{finalTranslation}'");
#endif
                        }
                        
                        // 记录这次成功命中
                        string logInfo = convertToUpper ? $" (CAPS: {testSentence})" : "";
                        LogItemPatternHit(originalSentence, regex.ToString(), string.Join(", ", items) + logInfo, finalTranslation);
                        
                        return finalTranslation;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error in TryMatchItemPatternDirect: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 直接从字典中翻译物品名，不再执行额外检查，
        /// 以避免在 `{ITEM}` 模式翻译时发生递归。
        /// </summary>
        /// <param name="item">物品名称</param>
        /// <returns>物品译名</returns>
        private string TranslateItemDirectly(string item)
        {
            try
            {
                // 先直接查字典
                if (_dict.TryGetValue(item, out string directTranslation))
                {
                    // 处理结果中的 `{IFHE}` 占位符
                    directTranslation = ProcessGenderPlaceholder(directTranslation);
                    
#if DEBUG
                TranslationMod.Logger?.LogDebug($"[TranslationService] Direct item translation: '{item}' -> '{directTranslation}'");
#endif
                    return directTranslation;
                }
                
                // 若未命中，则对全大写文本再尝试 Title Case 查找
                if (IsAllUpperCase(item))
                {
                    string titleCaseVersion = ConvertToTitleCase(item);
                    if (_dict.TryGetValue(titleCaseVersion, out string titleCaseTranslated))
                    {
                        // 在转换为全大写前先处理 `{IFHE}` 占位符
                        titleCaseTranslated = ProcessGenderPlaceholder(titleCaseTranslated);
                        string upperTranslation = titleCaseTranslated.ToUpper();
#if DEBUG
                    TranslationMod.Logger?.LogDebug($"[TranslationService] Title case item translation: '{item}' -> '{titleCaseVersion}' -> '{upperTranslation}'");
#endif
                        return upperTranslation;
                    }
                }
                
                // 如果仍未找到翻译，则返回原文
#if DEBUG
                TranslationMod.Logger?.LogDebug($"[TranslationService] No translation found for item: '{item}', using original");
#endif
                return item;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error in TranslateItemDirectly: {ex.Message}");
                return item;
            }
        }

        /// <summary>
        /// 尝试将整句识别为逗号分隔的物品列表并翻译。
        /// </summary>
        /// <param name="sentence">原始句子</param>
        /// <returns>若识别成功则返回翻译后的物品列表，否则返回 null</returns>
        private string TryTranslateItemList(string sentence)
        {
            try
            {
                // 按逗号拆分，并保留分隔符
                string[] parts = Regex.Split(sentence, @"(\s*,\s*)");
                
                // 少于 3 段时（最少需为：物品1、逗号、物品2），可判定其不是列表
                if (parts.Length < 3)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Not enough parts for item list: {parts.Length}");
#endif
                    return null;
                }
                
                // 检查是否至少有 2 个物品项
                var itemParts = new List<string>();
                for (int i = 0; i < parts.Length; i += 2) // 只取物品位置的片段
                {
                    if (!string.IsNullOrWhiteSpace(parts[i]))
                    {
                        itemParts.Add(parts[i].Trim());
                    }
                }
                
                // 物品列表至少要包含 2 个物品
                if (itemParts.Count < 2)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Not enough items for list: {itemParts.Count}");
#endif
                    return null;
                }
                
#if DEBUG
        TranslationMod.Logger?.LogInfo($"[TranslationService] Detected potential item list with {itemParts.Count} items: [{string.Join(", ", itemParts)}]");
#endif
                
                // 逐个尝试翻译每个物品
                var translatedParts = new List<string>();
                int translatedCount = 0;
                
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i % 2 == 0) // 当前位置是物品内容
                    {
                        string item = parts[i].Trim();
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            string translatedItem = TranslateItemDirectly(item);
                            translatedParts.Add(translatedItem);
                            
                            if (!translatedItem.Equals(item, StringComparison.Ordinal))
                            {
                                translatedCount++;
                            }
                        }
                        else
                        {
                            translatedParts.Add(parts[i]);
                        }
                    }
                    else // 当前位置是分隔符（逗号及其周围空白）
                    {
                        translatedParts.Add(parts[i]);
                    }
                }
                
                // 只要有至少一个物品翻译成功，就视为整体成功
                if (translatedCount > 0)
                {
                    string finalTranslation = string.Join("", translatedParts);
                    
                    // 处理最终结果中的 `{IFHE}` 占位符
                    finalTranslation = ProcessGenderPlaceholder(finalTranslation);
                    
                    LogItemListHit(sentence, itemParts.Count, translatedCount, finalTranslation);
                    return finalTranslation;
                }
                
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] No items were translated in the list");
#endif
                return null;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TranslationService] Error in TryTranslateItemList: {ex.Message}");
                return null;
            }
        }

        /// <summary>获取 `need_translate.csv` 的文件路径。</summary>
        private static string GetMissingKeysFilePath()
        {
            var currentLanguagePack = LanguageManager.GetCurrentLanguagePack();
            if (currentLanguagePack == null) return null;

            string languagePackDirectory = currentLanguagePack.DirectoryPath;
            //string translationsDirectory = Path.Combine(
            //    languagePackDirectory, currentLanguagePack.TranslationFilesPath);

            return Path.Combine(languagePackDirectory, "need_translate.csv");
        }

        /// <summary>对值进行 CSV 转义。</summary>
        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            
            // 如果包含引号、逗号或换行，则需要整体包裹在引号中
            if (value.Contains("\"") || value.Contains(",") || value.Contains("\n") || value.Contains("\r"))
            {
                // 将内部引号转义成双引号，并整体包裹引号
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            
            return value;
        }

        /// <summary>
        /// 检查字符串是否只由大写字母、数字、空白和标点组成，
        /// 且至少包含一个大写字母。
        /// </summary>
        /// <param name="input">待检查的字符串</param>
        /// <returns>若符合全大写风格模式则返回 true</returns>
        private static bool IsAllUpperCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            input = Regex.Replace(input, @"\s+", " ").Trim();
            return AllUpperCaseRegex.IsMatch(input);
        }

        /// <summary>
        /// 将字符串转换为 Title Case（每个单词首字母大写，其余字母小写）。
        /// 该逻辑主要面向英文文本。
        /// </summary>
        /// <param name="input">原始字符串</param>
        /// <returns>转换后的 Title Case 字符串</returns>
        private static string ConvertToTitleCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // 先统一转成小写
            var parts = Regex.Split(input.ToLowerInvariant(), @"(\s+)"); // 保留空白分隔符

            for (int i = 0; i < parts.Length; i++)
            {
                // 如果当前片段只是分隔符（空格 / 制表符），则跳过
                if (Regex.IsMatch(parts[i], @"^\s+$")) continue;

                // `of` / `as` / `for` 这类词，除首词外保持小写
                if (i != 0 && (parts[i] == "of" || parts[i] == "as" || parts[i] == "for")) continue;

                // 将单词中的首个字母位置转为大写
                parts[i] = Regex.Replace(parts[i], @"^\p{L}",
                            m => m.Value.ToUpperInvariant());
            }

            return string.Concat(parts);
        }

        /// <summary>
        /// 根据原始文本构造模板，将识别出的句子替换为 `{0}`、`{1}`、`{2}` 等占位符。
        /// </summary>
        /// <param name="input">原始字符串</param>
        /// <param name="sentences">从字符串中解析出的句子列表</param>
        /// <returns>带占位符的模板字符串</returns>
        private static string CreateTemplate(string input, List<string> sentences)
        {
            if (string.IsNullOrEmpty(input) || sentences == null || sentences.Count == 0)
                return input;

            string template = input;
            
            // 按句子长度从长到短排序，优先替换更长的句子
            // 这样可以避免较短句子误替换较长句子中的子串
            var sortedSentences = sentences
                .Select((sentence, index) => new { Sentence = sentence, Index = index })
                .Where(x => !string.IsNullOrEmpty(x.Sentence))
                .OrderByDescending(x => x.Sentence.Length)
                .ToList();

            foreach (var item in sortedSentences)
            {
                string sentence = item.Sentence;
                int originalIndex = item.Index;
                
                // 在模板中查找该句子的第一次出现位置
                int position = template.IndexOf(sentence, StringComparison.Ordinal);
                
                if (position >= 0)
                {
                    // 将命中的句子替换成占位符
                    string placeholder = "{" + originalIndex + "}";
                    template = template.Substring(0, position) + 
                              placeholder + 
                              template.Substring(position + sentence.Length);
                }
            }
            
            return template;
        }

        /// <summary>
        /// 将翻译后的句子回填到模板中。
        /// </summary>
        /// <param name="template">带占位符的模板</param>
        /// <param name="translatedSentences">翻译后的句子列表</param>
        /// <returns>回填后的最终译文</returns>
        private static string ApplyTemplate(string template, List<string> translatedSentences)
        {
            if (string.IsNullOrEmpty(template) || translatedSentences == null)
                return template;

            string result = template;
            
            for (int i = 0; i < translatedSentences.Count; i++)
            {
                string placeholder = "{" + i + "}";
                string translation = translatedSentences[i] ?? string.Empty;
                result = result.Replace(placeholder, translation);
            }
            
            return result;
        }

        /// <summary>获取语言包中的 CSV 文件列表。</summary>
        private static IEnumerable<string> GetCsvFiles()
        {
            var currentLanguagePack = LanguageManager.GetCurrentLanguagePack();
            if (currentLanguagePack == null) return Enumerable.Empty<string>();

            string languagePackDirectory = currentLanguagePack.DirectoryPath;
            string translationsDirectory = Path.Combine(
                languagePackDirectory, currentLanguagePack.TranslationFilesPath);

            if (!Directory.Exists(translationsDirectory))
                return Enumerable.Empty<string>();

            return Directory.GetFiles(translationsDirectory, "*.csv");
        }

        private static Dictionary<string, string> LoadCsv(IEnumerable<string> csvPaths)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var path in csvPaths.OrderBy(p => p)) // 保持确定性的处理顺序
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;

                    var columns = ParseCsvLine(line);
                    if (columns.Length < 2) continue;   // 至少需要 Original 和 Translate 两列

                    string original = columns[0];
                    string translation = columns[1];

                    // 支持在译文中使用 \n 标记，并将其还原为真实换行。
                    // 这样就能在单行 CSV 中表达多行译文，例如诗句。
                    // CSV 中可以写成："原文","第1行\n第2行\n第3行"
                    if (translation.Contains("\\n"))
                    {
                        translation = translation.Replace("\\n", "\n");
                    }

                    // 同一原文以首个出现的译文为准
                    if (!dict.ContainsKey(original))
                        dict[original] = translation;
                }
            }

            return dict;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                    // 引号中的双引号表示转义后的引号
                        current.Append('"');
                        i++; // 跳过下一枚引号
                    }
                    else
                    {
                        // 切换“当前是否处于引号内部”的状态
                        inQuotes = !inQuotes;
                    }
                    continue;
                }
                
                if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                
                current.Append(c);
            }
            
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
} 
