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
            
            // Initialize translation buffer dictionary
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
            
            // Check translation buffer dictionary
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
                    return verseResult;
                }

                // Use GameTextParser to split text into parts
                var sentences = GameTextParser.Parse(input);
                
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[TranslationService] INPUT: '{input}'");
                for (int i = 0; i < sentences.Count; i++)
                {
                    TranslationMod.Logger?.LogInfo($"[TranslationService] SENTENCE[{i}]: '{sentences[i]}'");
                }
#endif
                
                // Create template from original input
                var template = CreateTemplate(input, sentences);
                
                var translatedSentences = new List<string>();
                foreach (var sentence in sentences)
                {      
                    string translatedSentence = TranslateSentence(sentence);
                    translatedSentences.Add(translatedSentence);
                }

                // Apply translated sentences to template
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
                
                return input;
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

                TranslationMod.Logger?.LogInfo($"[Verse] '{line}' -> '{translated}' [found={wasTranslated}]");
            }

            if (translatedCount > 0)
            {
                string result = string.Join("\n", translatedLines);
                TranslationMod.Logger?.LogInfo($"[Verse] Result ({translatedCount}/4): '{result}'");
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

        /// <summary>Логирует успешное нахождение перевода через Title Case (с дедупликацией)</summary>
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

        /// <summary>Логирует успешное нахождение перевода через {ITEM} паттерн (с дедупликацией)</summary>
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

        /// <summary>Логирует успешное нахождение перевода списка предметов (с дедупликацией)</summary>
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

        /// <summary>Логирует успешное нахождение перевода через замену апострофа (с дедупликацией)</summary>
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
        /// Пытается заменить имя игрока на плейсхолдер {PLAYER} и найти перевод
        /// </summary>
        /// <param name="sentence">Исходное предложение</param>
        /// <returns>Переведенное предложение с восстановленным именем игрока или null если не найдено</returns>
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

                // Проверяем, содержит ли строка имя игрока
                if (!sentence.Contains(playerName))
                {
#if DEBUG
                TranslationMod.Logger?.LogDebug($"[TranslationService] Sentence does not contain player name '{playerName}'");
#endif
                    return null;
                }

                // Заменяем имя игрока на плейсхолдер
                string sentenceWithPlaceholder = sentence.Replace(playerName, "{PLAYER}");
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[TranslationService] Checking player name replacement: '{sentence}' -> '{sentenceWithPlaceholder}'");
#endif

                // Ищем перевод для строки с плейсхолдером
                if (_dict.TryGetValue(sentenceWithPlaceholder, out string translatedWithPlaceholder))
                {
                    // Заменяем плейсхолдер обратно на имя игрока в переводе
                    string finalTranslation = translatedWithPlaceholder.Replace("{PLAYER}", playerName);
                    
                    // Обрабатываем плейсхолдер {IFHE} в итоговом переводе
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
        /// Получает имя текущего игрока из игры
        /// </summary>
        /// <returns>Имя игрока или null в случае ошибки</returns>
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
        /// Получает пол текущего игрока из игры
        /// </summary>
        /// <returns>true если игрок мужчина, false если женщина, null в случае ошибки</returns>
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
        /// Обрабатывает плейсхолдер {IFHE string_if_player_man | string_if_player_woman} в переводе
        /// </summary>
        /// <param name="translation">Строка перевода с возможными плейсхолдерами {IFHE}</param>
        /// <returns>Обработанная строка с заменёнными плейсхолдерами</returns>
        private static string ProcessGenderPlaceholder(string translation)
        {
            if (string.IsNullOrEmpty(translation) || !translation.Contains("{IFHE"))
            {
                return translation;
            }

            try
            {
                // Регулярное выражение для поиска плейсхолдера {IFHE text1 | text2}
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
                            
                            // Выбираем текст в зависимости от пола игрока
                            string selectedText = playerGender.Value ? maleText : femaleText;
                            
                            // Заменяем плейсхолдер на выбранный текст
                            result = result.Replace(match.Value, selectedText);
                            
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Gender placeholder processed: '{match.Value}' -> '{selectedText}' (player is {(playerGender.Value ? "male" : "female")})");
#endif
                        }
                    }
                    else
                    {
                        // Если не удалось определить пол игрока, используем мужской вариант по умолчанию
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
        /// Создает список regex паттернов для обработки {ITEM} плейсхолдеров
        /// </summary>
        /// <param name="dict">Словарь переводов</param>
        /// <returns>Список паттернов и соответствующих шаблонов</returns>
        private static List<(Regex regex, string template)> CreateItemPatterns(Dictionary<string, string> dict)
        {
            var patterns = new List<(Regex regex, string template)>();
            
            foreach (var kvp in dict)
            {
                if (kvp.Key.Contains("{ITEM}"))
                {
                    try
                    {
                        // Подсчитываем количество {ITEM} в ключе
                        int itemCount = 0;
                        string tempKey = kvp.Key;
                        
                        // Заменяем каждый {ITEM} на уникальный временный маркер
                        while (tempKey.Contains("{ITEM}"))
                        {
                            // Заменяем только первое вхождение {ITEM}
                            int index = tempKey.IndexOf("{ITEM}");
                            if (index >= 0)
                            {
                                tempKey = tempKey.Substring(0, index) + 
                                         $"___ITEM_PLACEHOLDER_{itemCount}___" + 
                                         tempKey.Substring(index + 6); // 6 = "{ITEM}".Length
                            }
                            itemCount++;
                        }
                        
                        // Применяем Regex.Escape для безопасности
                        string escapedKey = Regex.Escape(tempKey);
                        
                        // Заменяем каждый временный маркер на отдельную regex группу
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
        /// Пытается найти совпадение по {ITEM} паттернам и выполнить перевод
        /// </summary>
        /// <param name="sentence">Исходное предложение</param>
        /// <returns>Переведенное предложение или null если не найдено</returns>
        private string TryMatchItemPattern(string sentence)
        {
            try
            {
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[TranslationService] Checking {_itemPatterns.Count} item patterns for: '{sentence}'");
#endif
                
                // Сначала пробуем прямое совпадение
                string directMatch = TryMatchItemPatternDirect(sentence, sentence, false);
                if (directMatch != null)
                {
                    return directMatch;
                }
                
                // Если не найдено и строка в CAPS - пробуем Title Case версию
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
        /// Выполняет прямое сопоставление с {ITEM} паттернами
        /// </summary>
        /// <param name="testSentence">Предложение для тестирования против паттернов</param>
        /// <param name="originalSentence">Оригинальное предложение для логирования</param>
        /// <param name="convertToUpper">Нужно ли конвертировать результат в CAPS</param>
        /// <returns>Переведенное предложение или null если не найдено</returns>
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
                        // Получаем все найденные предметы (кроме группы 0, которая содержит полное совпадение)
                        var items = new List<string>();
                        for (int i = 1; i < match.Groups.Count; i++)
                        {
                            items.Add(match.Groups[i].Value);
                        }
                        
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[TranslationService] Item pattern matched: '{testSentence}' -> items: [{string.Join(", ", items)}] using template: '{template}'");
#endif
                        
                        // Переводим каждый найденный предмет отдельно
                        var translatedItems = new List<string>();
                        foreach (string item in items)
                        {
                            string translatedItem = TranslateItemDirectly(item);
                            translatedItems.Add(translatedItem);
                        }
                        
                        // Заменяем каждый {ITEM} в шаблоне на соответствующий переведенный предмет
                        string finalTranslation = template;
                        for (int i = 0; i < translatedItems.Count; i++)
                        {
                            // Находим первое вхождение {ITEM} и заменяем его
                            int index = finalTranslation.IndexOf("{ITEM}");
                            if (index >= 0)
                            {
                                finalTranslation = finalTranslation.Substring(0, index) + 
                                                 translatedItems[i] + 
                                                 finalTranslation.Substring(index + 6); // 6 = "{ITEM}".Length
                            }
                        }
                        
                        // Обрабатываем плейсхолдер {IFHE} в итоговом переводе
                        finalTranslation = ProcessGenderPlaceholder(finalTranslation);
                        
                        // Если нужно конвертировать в CAPS
                        if (convertToUpper)
                        {
                            finalTranslation = finalTranslation.ToUpper();
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[TranslationService] Converted result to CAPS: '{finalTranslation}'");
#endif
                        }
                        
                        // Логируем успешное совпадение
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
        /// Переводит предмет напрямую из словаря без дополнительных проверок
        /// (избегает рекурсии при переводе {ITEM} паттернов)
        /// </summary>
        /// <param name="item">Название предмета</param>
        /// <returns>Переведенное название предмета</returns>
        private string TranslateItemDirectly(string item)
        {
            try
            {
                // Прямой поиск в словаре
                if (_dict.TryGetValue(item, out string directTranslation))
                {
                    // Обрабатываем плейсхолдер {IFHE} в итоговом переводе
                    directTranslation = ProcessGenderPlaceholder(directTranslation);
                    
#if DEBUG
                TranslationMod.Logger?.LogDebug($"[TranslationService] Direct item translation: '{item}' -> '{directTranslation}'");
#endif
                    return directTranslation;
                }
                
                // Если не найден - проверяем Title Case (для ЗАГЛАВНЫХ БУКВ)
                if (IsAllUpperCase(item))
                {
                    string titleCaseVersion = ConvertToTitleCase(item);
                    if (_dict.TryGetValue(titleCaseVersion, out string titleCaseTranslated))
                    {
                        // Обрабатываем плейсхолдер {IFHE} перед конвертацией в CAPS
                        titleCaseTranslated = ProcessGenderPlaceholder(titleCaseTranslated);
                        string upperTranslation = titleCaseTranslated.ToUpper();
#if DEBUG
                    TranslationMod.Logger?.LogDebug($"[TranslationService] Title case item translation: '{item}' -> '{titleCaseVersion}' -> '{upperTranslation}'");
#endif
                        return upperTranslation;
                    }
                }
                
                // Если перевод не найден - возвращаем оригинал
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
        /// Пытается перевести строку как список предметов, разделенных запятыми
        /// </summary>
        /// <param name="sentence">Исходное предложение</param>
        /// <returns>Переведенный список предметов или null если не является списком</returns>
        private string TryTranslateItemList(string sentence)
        {
            try
            {
                // Разбиваем строку по запятым, сохраняя разделители
                string[] parts = Regex.Split(sentence, @"(\s*,\s*)");
                
                // Если меньше 3 частей (минимум: предмет1, запятая, предмет2), то это не список
                if (parts.Length < 3)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TranslationService] Not enough parts for item list: {parts.Length}");
#endif
                    return null;
                }
                
                // Проверяем, что у нас есть хотя бы 2 предмета (нечетные индексы - это предметы)
                var itemParts = new List<string>();
                for (int i = 0; i < parts.Length; i += 2) // берем только нечетные индексы (предметы)
                {
                    if (!string.IsNullOrWhiteSpace(parts[i]))
                    {
                        itemParts.Add(parts[i].Trim());
                    }
                }
                
                // Минимум 2 предмета для списка
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
                
                // Пытаемся перевести каждый предмет
                var translatedParts = new List<string>();
                int translatedCount = 0;
                
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i % 2 == 0) // Это предмет (нечетный индекс в оригинальном массиве)
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
                    else // Это разделитель (запятая с пробелами)
                    {
                        translatedParts.Add(parts[i]);
                    }
                }
                
                // Если хотя бы один предмет был переведен, считаем это успехом
                if (translatedCount > 0)
                {
                    string finalTranslation = string.Join("", translatedParts);
                    
                    // Обрабатываем плейсхолдер {IFHE} в итоговом переводе
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

        /// <summary>Получает путь к файлу need_translate.csv</summary>
        private static string GetMissingKeysFilePath()
        {
            var currentLanguagePack = LanguageManager.GetCurrentLanguagePack();
            if (currentLanguagePack == null) return null;

            string languagePackDirectory = currentLanguagePack.DirectoryPath;
            //string translationsDirectory = Path.Combine(
            //    languagePackDirectory, currentLanguagePack.TranslationFilesPath);

            return Path.Combine(languagePackDirectory, "need_translate.csv");
        }

        /// <summary>Экранирует значение для CSV формата</summary>
        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            
            // Если содержит кавычки, запятые или переносы строк - оборачиваем в кавычки
            if (value.Contains("\"") || value.Contains(",") || value.Contains("\n") || value.Contains("\r"))
            {
                // Удваиваем кавычки и оборачиваем в кавычки
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            
            return value;
        }

        /// <summary>
        /// Проверяет, состоит ли строка только из заглавных букв, цифр, пробелов и знаков препинания
        /// и содержит хотя бы одну заглавную букву
        /// </summary>
        /// <param name="input">Строка для проверки</param>
        /// <returns>true если соответствует паттерну заглавных букв</returns>
        private static bool IsAllUpperCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            input = Regex.Replace(input, @"\s+", " ").Trim();
            return AllUpperCaseRegex.IsMatch(input);
        }

        /// <summary>
        /// Преобразует строку в Title Case (первая буква каждого слова заглавная, остальные маленькие)
        /// Оптимизировано для английского языка
        /// </summary>
        /// <param name="input">Исходная строка</param>
        /// <returns>Строка в Title Case</returns>
        private static string ConvertToTitleCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // приводим к lower-case один раз
            var parts = Regex.Split(input.ToLowerInvariant(), @"(\s+)"); // сохраняем пробелы

            for (int i = 0; i < parts.Length; i++)
            {
                // если это разделитель (пробелы / табы) — пропускаем
                if (Regex.IsMatch(parts[i], @"^\s+$")) continue;

                // слово «of» — оставляем строчным, кроме первого слова всей фразы
                if (i != 0 && (parts[i] == "of" || parts[i] == "as" || parts[i] == "for")) continue;

                // капитализируем первую буквенную позицию слова
                parts[i] = Regex.Replace(parts[i], @"^\p{L}",
                            m => m.Value.ToUpperInvariant());
            }

            return string.Concat(parts);
        }

        /// <summary>
        /// Создает шаблон из исходного текста, заменяя найденные предложения на плейсхолдеры {0}, {1}, {2}, etc.
        /// </summary>
        /// <param name="input">Исходная строка</param>
        /// <param name="sentences">Список предложений найденных в строке</param>
        /// <returns>Шаблон с плейсхолдерами</returns>
        private static string CreateTemplate(string input, List<string> sentences)
        {
            if (string.IsNullOrEmpty(input) || sentences == null || sentences.Count == 0)
                return input;

            string template = input;
            
            // Сортируем предложения по убыванию длины, чтобы сначала заменить более длинные
            // Это предотвращает случайную замену коротких предложений внутри длинных
            var sortedSentences = sentences
                .Select((sentence, index) => new { Sentence = sentence, Index = index })
                .Where(x => !string.IsNullOrEmpty(x.Sentence))
                .OrderByDescending(x => x.Sentence.Length)
                .ToList();

            foreach (var item in sortedSentences)
            {
                string sentence = item.Sentence;
                int originalIndex = item.Index;
                
                // Ищем первое вхождение предложения в шаблоне
                int position = template.IndexOf(sentence, StringComparison.Ordinal);
                
                if (position >= 0)
                {
                    // Заменяем найденное предложение на плейсхолдер
                    string placeholder = "{" + originalIndex + "}";
                    template = template.Substring(0, position) + 
                              placeholder + 
                              template.Substring(position + sentence.Length);
                }
            }
            
            return template;
        }

        /// <summary>
        /// Применяет переведенные предложения к шаблону
        /// </summary>
        /// <param name="template">Шаблон с плейсхолдерами</param>
        /// <param name="translatedSentences">Список переведенных предложений</param>
        /// <returns>Итоговая строка с переводом</returns>
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

        /// <summary>Получаем список CSV-файлов из language-пакета.</summary>
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

            foreach (var path in csvPaths.OrderBy(p => p)) // детерминированный порядок
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;

                    var columns = ParseCsvLine(line);
                    if (columns.Length < 2) continue;   // нужна хотя бы Original;Translate

                    string original = columns[0];
                    string translation = columns[1];

                    // Поддержка маркера \n в переводах — заменяем на реальный перенос строки.
                    // Это позволяет задавать многострочные переводы (напр. стихи) в однострочном CSV.
                    // В CSV пишем: "оригинал","строка1\nстрока2\nстрока3"
                    if (translation.Contains("\\n"))
                    {
                        translation = translation.Replace("\\n", "\n");
                    }

                    // Первый встретившийся перевод выигрывает
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
                        // Двойная кавычка внутри кавычек - это экранированная кавычка
                        current.Append('"');
                        i++; // Пропускаем следующую кавычку
                    }
                    else
                    {
                        // Переключаем состояние "внутри кавычек"
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
