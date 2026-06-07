// Patches/UITextBlockSetContentPatch.cs
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    /// <summary>
    /// Harmony patch intercepting UITextBlock.setContent(string)
    /// and inserting translated text.
    /// </summary>
    [HarmonyPatch(typeof(UITextBlock), nameof(UITextBlock.setContent), new[] { typeof(string) })]
    public static class UITextBlockSetContentPatch
    {
        /* ----------------------------------------------------------------- */
        /* 1.  延迟初始化翻译服务                                            */
        /* ----------------------------------------------------------------- */

        private static readonly Lazy<TranslationService> _translator =
            new(() => new TranslationService());

        /* ----------------------------------------------------------------- */
        /* 1.0  Tooltip 键映射缓冲：翻译后 key -> 原始 key                    */
        /* ----------------------------------------------------------------- */
        
        /// <summary>
        /// Tooltip 键映射缓冲区，保存“翻译后 key -> 原始 key”的对应关系。
        /// </summary>
        public static readonly Dictionary<string, string> TooltipKeyBuffer = new();
        
        /// <summary>
        /// 用于从 `<tag>tooltipKey</tag>` 中提取 tooltip 关键词的正则。
        /// </summary>
        private static readonly Regex TooltipTagRegex = new(@"<tag>([^<]+)</tag>", RegexOptions.Compiled);
        private static readonly Regex HashPatternRegex = new Regex(@"#[A-Za-z]{2,}", RegexOptions.Compiled);
        /// <summary>
        /// Tooltip 缓冲区访问锁。
        /// </summary>
        private static readonly object _tooltipBufferLock = new();

        /* ----------------------------------------------------------------- */
        /* 1.1  通过反射访问 UITextBlock 私有成员                             */
        /* ----------------------------------------------------------------- */
        
        private static FieldInfo _illuminatedImageField;
        private static FieldInfo _fontField;
        private static FieldInfo _contentField;
        private static FieldInfo _tooltipField;
        
        private static MethodInfo _preProcessStringMethod;
        private static MethodInfo _identifyTooltipKeywordsMethod;
        private static MethodInfo _splitIntoParagraphMethod;
        private static MethodInfo _parseParagraphMethod;
        private static MethodInfo _pruneLengthMethod;
        
        /// <summary>延迟获取 `illuminatedImage` 字段反射信息。</summary>
        private static FieldInfo IlluminatedImageField
        {
            get
            {
                if (_illuminatedImageField == null)
                    _illuminatedImageField = typeof(UITextBlock).GetField("illuminatedImage", BindingFlags.NonPublic | BindingFlags.Instance);
                return _illuminatedImageField;
            }
        }
        
        /// <summary>延迟获取 `font` 字段反射信息。</summary>
        private static FieldInfo FontField
        {
            get
            {
                if (_fontField == null)
                    _fontField = typeof(UITextBlock).GetField("font", BindingFlags.NonPublic | BindingFlags.Instance);
                return _fontField;
            }
        }
        
        /// <summary>延迟获取 `content` 字段反射信息。</summary>
        private static FieldInfo ContentField
        {
            get
            {
                if (_contentField == null)
                    _contentField = typeof(UITextBlock).GetField("content", BindingFlags.NonPublic | BindingFlags.Instance);
                return _contentField;
            }
        }

        /// <summary>延迟获取 `toolTips` 字段反射信息。</summary>
        private static FieldInfo ToolTipField
        {
            get
            {
                if (_tooltipField == null)
                    _tooltipField = typeof(UITextBlock).GetField("toolTips", BindingFlags.NonPublic | BindingFlags.Instance);
                return _tooltipField;
            }
        }
        
        /// <summary>延迟获取 `preProcessString` 方法反射信息。</summary>
        private static MethodInfo PreProcessStringMethod
        {
            get
            {
                if (_preProcessStringMethod == null)
                    _preProcessStringMethod = typeof(UITextBlock).GetMethod("preProcessString", BindingFlags.NonPublic | BindingFlags.Instance);
                return _preProcessStringMethod;
            }
        }
        
        /// <summary>延迟获取 `identifyTooltipKeywords` 方法反射信息。</summary>
        private static MethodInfo IdentifyTooltipKeywordsMethod
        {
            get
            {
                if (_identifyTooltipKeywordsMethod == null)
                    _identifyTooltipKeywordsMethod = typeof(UITextBlock).GetMethod("identifyTooltipKeywords", BindingFlags.NonPublic | BindingFlags.Instance);
                return _identifyTooltipKeywordsMethod;
            }
        }
        
        /// <summary>延迟获取 `splitIntoParagraph` 方法反射信息。</summary>
        private static MethodInfo SplitIntoParagraphMethod
        {
            get
            {
                if (_splitIntoParagraphMethod == null)
                    _splitIntoParagraphMethod = typeof(UITextBlock).GetMethod("splitIntoParagraph", BindingFlags.NonPublic | BindingFlags.Instance);
                return _splitIntoParagraphMethod;
            }
        }
        
        /// <summary>延迟获取 `parseParagraph` 方法反射信息。</summary>
        private static MethodInfo ParseParagraphMethod
        {
            get
            {
                if (_parseParagraphMethod == null)
                    _parseParagraphMethod = typeof(UITextBlock).GetMethod("parseParagraph", BindingFlags.NonPublic | BindingFlags.Instance);
                return _parseParagraphMethod;
            }
        }
        
        /// <summary>延迟获取 `pruneLength` 方法反射信息。</summary>
        private static MethodInfo PruneLengthMethod
        {
            get
            {
                if (_pruneLengthMethod == null)
                    _pruneLengthMethod = typeof(UITextBlock).GetMethod("pruneLength", BindingFlags.NonPublic | BindingFlags.Instance);
                return _pruneLengthMethod;
            }
        }

        /* ----------------------------------------------------------------- */
        /* 2.  Prefix：接管 setContent 的翻译与渲染流程                       */
        /* ----------------------------------------------------------------- */

        /// <summary>
        /// `UITextBlock.setContent` 的前置补丁。
        /// 流程：英文直接放行；其他语言先翻译文本，再走自定义的完整渲染流程并跳过原方法。
        /// </summary>
        private static bool Prefix(UITextBlock __instance, string __0)
        {
            try
            {
                var currentLanguagePack = LanguageManager.GetCurrentLanguagePack();
                if (currentLanguagePack == null || currentLanguagePack.Name.Equals("English", StringComparison.OrdinalIgnoreCase))
                {
                    return true; // 英文环境下继续执行原始方法
                }
                string processedText = StripHashTags(__0);
                string translatedText = _translator.Value.Process(processedText);
                //LogSetContentTrace(__0, translatedText);
                // 使用翻译后的文本执行自定义渲染流程
                SetContentComplete(__instance, __0, translatedText);
                
                // 已接管逻辑，跳过原方法
                return false;
            }
            catch (Exception ex)
            {
#if DEBUG
                TranslationMod.Logger?.LogError($"Error in custom setContent implementation: {ex.Message}");
#endif
                // 自定义流程异常时回退原方法
                return true;
            }
        }

        // 打印翻译信息
        private static void LogSetContentTrace(string input, string translated)
        {
            try
            {
                TranslationMod.Logger?.LogInfo($"[SetContentTrace] input: {input ?? "<null>"}");
                TranslationMod.Logger?.LogInfo($"[SetContentTrace] translated: {translated ?? "<null>"}");
                TranslationMod.Logger?.LogInfo($"[SetContentTrace] stack:\n{Environment.StackTrace}");
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[SetContentTrace] Failed to log trace: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 重新实现 `UITextBlock.setContent` 的主要流程。
        /// 流程：清理旧元素，写入内容与字体，处理首字装饰、tooltip 标签、多段落解析，
        /// 最后执行裁剪与对齐。
        /// </summary>
        private static void SetContentComplete(UITextBlock instance, string input, string translated)
        {
            try
            {                
                if (instance.isContentEqual(input) || input == null || input.Length == 0)
                {
                    return;
                }
                
                if (PreProcessStringMethod == null)
                {
#if DEBUG
                    TranslationMod.Logger?.LogError("PreProcessStringMethod is null");
#endif
                    return;
                }
                
                instance.clearElements();
                
                if (ContentField == null)
                {
#if DEBUG
                    TranslationMod.Logger?.LogError("ContentField is null");
#endif
                    return;
                }

                ContentField.SetValue(instance, input);
                
                if (FontField == null)
                {
#if DEBUG
                    TranslationMod.Logger?.LogError("FontField is null");
#endif
                    return;
                }
                
                var font = FontField.GetValue(instance) as Font;
                if (font == null)
                {
                    TranslationMod.Logger?.LogError("No font set for text box:" + input);
                    MainControl.logError("No font set for text box:" + input);
                    return;
                }
                
                // 首字符装饰初始化
                if (instance.illuminatedFont != null)
                {   
                    // 获取首字符在字图集里的子图编号
                    int subimageForChar = StringPrinter.getSubimageForChar(translated[0]);
                    
                    // Расширенная проверка для кириллицы (ваш комментарий: переделать на проверку взятых кодов из кодировки)
                    // 0-25、 90-122 字符需要进行装饰
                    if (subimageForChar <= 25 || (subimageForChar >= 90 && subimageForChar <= 122))
                    {                        
                        if (IlluminatedImageField == null)
                        {
                            TranslationMod.Logger?.LogError("IlluminatedImageField is null");
                            return;
                        }
                        
                        var illuminatedImage = new UICanvasVertical();
                        // 将首字母从待翻译内容中去掉
                        translated = translated.Substring(1);       
                        //设置贴图
                        illuminatedImage.backgroundTexture = TextureTools.getLetterSubImageTextureData(subimageForChar, instance.illuminatedFont.getModelPath());
                        //设置右间距
                        illuminatedImage.padding.right = font.wordSpacing;
                        IlluminatedImageField.SetValue(instance, illuminatedImage);     // 设置 instance.illuminatedImage为illuminatedImage
                        instance.add(illuminatedImage);                                 // 将此装饰首字符对象添加至控件元素树
                    }
                    else
                    {
#if DEBUG
                        TranslationMod.Logger?.LogInfo($"Character '{translated[0]}' with subimage {subimageForChar} doesn't match illuminated criteria");
#endif
                    }
                }
                translated = (string)PreProcessStringMethod.Invoke(instance, new object[] { translated });
                
                string taggedInput = identifyTooltipKeywords(instance, input);
                if(taggedInput != input)
                {
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"Tagged input: {taggedInput}");
#endif
                    
                    // Извлекаем tooltip ключи и добавляем их в буфер
                    //提取 tooltip 的key，并将它们添加到缓冲区
                    var keys = ExtractAndBufferTooltipKeys(taggedInput);
                    
                    // Оборачиваем переведенные ключи в теги <tag></tag>
                    //将已翻译的key用 <tag></tag> 标签包裹起来
                    translated = TagKeys(translated, keys);
                    
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"Translated tagged input: {translated}");
#endif
                }

                if (SplitIntoParagraphMethod == null)
                {
                    TranslationMod.Logger?.LogError("SplitIntoParagraphMethod is null");
                    return;
                }
                // 按照待翻译文本中的换行\r\n、\r、\n将文本分割为多行文本list
                var paragraphs = (List<string>)SplitIntoParagraphMethod.Invoke(instance, new object[] { translated });
                if (paragraphs != null)
                {
                    foreach (string paragraph in paragraphs)
                    {   
                        ParseParagraphComplete(instance, paragraph);
                    }
                }
                
                if (PruneLengthMethod == null)
                {
                    TranslationMod.Logger?.LogError("PruneLengthMethod is null");
                    return;
                }
                
                PruneLengthMethod.Invoke(instance, null);
                
                instance.alignElements();                
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"Exception in SetContentComplete: {ex.Message}");
                TranslationMod.Logger?.LogError($"Stack trace: {ex.StackTrace}");
                throw; // Перебрасываем исключение для обработки в Prefix
            }
        }

        /// <summary>
        /// Извлекает tooltip ключи из тегов <tag>tooltipKey</tag>, переводит их и добавляет в буфер
        /// </summary>
        /// <param name="input">Исходный текст с тегами</param>
        private static Dictionary<string, string> ExtractAndBufferTooltipKeys(string input)
        {
            Dictionary<string, string> keys = new Dictionary<string, string>();
            try
            {
                var matches = TooltipTagRegex.Matches(input);
                if (matches.Count == 0)
                {
#if DEBUG
                    TranslationMod.Logger?.LogDebug($"[TooltipBuffer] No tooltip tags found in: {input}");
#endif
                    return keys;
                }
#if DEBUG
                TranslationMod.Logger?.LogDebug($"[TooltipBuffer] Match Count: {matches.Count}");
#endif 
                lock (_tooltipBufferLock)
                {
                    foreach (Match match in matches)
                    {
#if DEBUG
                        TranslationMod.Logger?.LogDebug($"[TooltipBuffer] Match Count: {match.Groups.Count}");
#endif      
                        if (match.Groups.Count > 1)
                        {          
                            string originalKey = match.Groups[1].Value;
                            if (string.IsNullOrWhiteSpace(originalKey))
                                continue;

                            // Переводим ключ
                            string translatedKey;
                            if(originalKey.Contains("#"))
                            {
                                translatedKey = _translator.Value.Process(originalKey.Replace("#", ""));                                translatedKey = translatedKey.Replace("#", "");
                            }
                            else 
                            {
                                translatedKey = _translator.Value.Process(originalKey);
                            }
                            if(!keys.ContainsKey(translatedKey))
                            {
                                keys.Add(translatedKey, originalKey);
                            }
                        }
                    }
                }
                return keys;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TooltipBuffer] Error extracting tooltip keys: {ex.Message}");
                return keys;
            }
        }

        private static string identifyTooltipKeywords(UITextBlock instance, string input)
        {
            if (IdentifyTooltipKeywordsMethod == null)
            {
                TranslationMod.Logger?.LogError("IdentifyTooltipKeywordsMethod is null");
                return input;
            }
            return (string)IdentifyTooltipKeywordsMethod.Invoke(instance, new object[] { input });
        }

        public static string StripHashTags(string input)
        {
            // (?<![=:])   — перед # нет = или :  → значит это "живой" тег, а не цвет
            // ([\p{L}\d_-]+) — буквы любых алфавитов, цифры, _ и -
            var regex = new Regex(@"(?<![=:])#([\p{L}\d_-]+)",
                                RegexOptions.Compiled | RegexOptions.Multiline);

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // MatchEvaluator сразу и собирает тег, и отдаёт замену без #
            string result = regex.Replace(input, m =>
            {
                string tag = m.Groups[1].Value;   // уже без «#»
                found.Add(tag);
                return tag;                       // замена в тексте
            });
            return result;
        }

        public static string HighlightKeysInText(string input, List<string> keys)
        {
            if (string.IsNullOrWhiteSpace(input) || keys == null || keys.Count == 0)
                return input;

            // Сортируем ключи по убыванию длины для приоритета длинных совпадений
            var orderedKeys = keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .OrderByDescending(k => k.Length)
                .ToList();

            foreach (var key in orderedKeys)
            {
                // Удаляем лишние пробелы и приводим к нижнему регистру для сопоставления
                string normalizedKey = key.Trim();

                // Пытаемся найти слова, начинающиеся на ключ и допускающие падежные окончания
                string pattern = $@"\b({Regex.Escape(normalizedKey)}\p{{L}}*)\b";

                input = Regex.Replace(
                    input,
                    pattern,
                    match =>
                    {
                        // Уже содержит тег — пропускаем
                        if (match.Value.Contains("<tag>") || match.Value.Contains("</tag>"))
                            return match.Value;

                        // Заворачиваем найденное совпадение
                        return $"<tag>{match.Value}</tag>";
                    },
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return input;
        }
        
        private struct PatternInfo
        {
            public string OriginalKey { get; set; }
            public string Key { get; set; }
            public Regex Rx { get; set; }
        }
        /// <summary>
        /// Оборачивает найденные ключи в <tag></tag> с предотвращением дублирования.
        /// </summary>
        public static string TagKeys(string text, Dictionary<string, string> dict)
        {
            if (string.IsNullOrWhiteSpace(text) || dict == null)
                return text;

            try
            {
                // Набор для отслеживания уже обработанных позиций
                var processedRanges = new List<(int start, int end)>();
                
                // 1) строим Regex-паттерны для всех ключей (длинные → первыми)
                var patternInfos = dict
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                    .OrderByDescending(kvp => kvp.Key.Length)
                    .Select(kvp => 
                    {
                        try
                        {
                            var pattern = BuildPattern(kvp.Key);
#if DEBUG
                            TranslationMod.Logger?.LogInfo($"[TagKeys] Pattern: {pattern}");
#endif
                            if (string.IsNullOrEmpty(pattern))
                                return (PatternInfo?)null;
                            

                            return (PatternInfo?)new PatternInfo
                            {
                                OriginalKey = kvp.Value,
                                Key = kvp.Key,
                                Rx  = new Regex(
                                        pattern,
                                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                            };
                        }
                        catch (Exception ex)
                        {
                            TranslationMod.Logger?.LogError($"[TagKeys] Error building pattern for key '{kvp.Key}': {ex.Message}");
                            return (PatternInfo?)null;
                        }
                    })
                    .Where(rx => rx.HasValue)
                    .Select(rx => rx.Value)
                    .ToArray();

                // 2) Собираем все совпадения с их позициями
                var allMatches = new List<(Match match, PatternInfo pattern, int priority)>();
                
                for (int i = 0; i < patternInfos.Length; i++)
                {
                    var p = patternInfos[i];
                    try
                    {
                        var matches = p.Rx.Matches(text);
                        foreach (Match match in matches)
                        {
                            // Проверяем, что совпадение не содержит уже тег
                            if (!match.Value.Contains("<tag>") && !match.Value.Contains("</tag>"))
                            {
                                allMatches.Add((match, p, i)); // i как приоритет (меньше = выше приоритет)
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TranslationMod.Logger?.LogError($"[TagKeys] Error finding matches for pattern: {ex.Message}");
                    }
                }

                // 3) Сортируем по приоритету (длина ключа) и позиции
                allMatches = allMatches
                    .OrderBy(x => x.priority) // Сначала по приоритету (длинные ключи первыми)
                    .ThenBy(x => x.match.Index) // Затем по позиции в тексте
                    .ToList();

                // 4) Отфильтровываем пересекающиеся совпадения
                var finalMatches = new List<(Match match, PatternInfo pattern)>();
                
                foreach (var (match, pattern, _) in allMatches)
                {
                    int start = match.Index;
                    int end = match.Index + match.Length - 1;
                    
                    // Проверяем, не пересекается ли с уже выбранными совпадениями
                    bool overlaps = processedRanges.Any(range => 
                        !(end < range.start || start > range.end));
                    
                    if (!overlaps)
                    {
                        finalMatches.Add((match, pattern));
                        processedRanges.Add((start, end));
#if DEBUG
                        TranslationMod.Logger?.LogDebug($"[TagKeys] Added match: '{match.Value}' at {start}-{end}");
#endif
                    }
                    else
                    {
#if DEBUG
                        TranslationMod.Logger?.LogDebug($"[TagKeys] Skipped overlapping match: '{match.Value}' at {start}-{end}");
#endif
                    }
                }

                // 5) Применяем замены в обратном порядке (от конца к началу), чтобы не сбить позиции
                finalMatches = finalMatches.OrderByDescending(x => x.match.Index).ToList();
                
                foreach (var (match, pattern) in finalMatches)
                {
                    try
                    {
                        string matchValue = match.Value;
                        string replacement = $"<tag>{matchValue}</tag>";
                        
                        // Добавляем в буфер tooltip ключей
                        if (!TooltipKeyBuffer.ContainsKey(matchValue) && matchValue != pattern.OriginalKey)
                        {
                            TooltipKeyBuffer.Add(matchValue, pattern.OriginalKey);
                        }
                        
                        // Заменяем в тексте
                        text = text.Substring(0, match.Index) + replacement + text.Substring(match.Index + match.Length);
                        
#if DEBUG
                        TranslationMod.Logger?.LogDebug($"[TagKeys] Replaced '{matchValue}' with '{replacement}'");
#endif
                    }
                    catch (Exception ex)
                    {
                        TranslationMod.Logger?.LogError($"[TagKeys] Error applying replacement: {ex.Message}");
                    }
                }

                return text;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TagKeys] General error in TagKeys: {ex.Message}");
                return text; // Возвращаем исходный текст в случае ошибки
            }
        }

        /* ── PRIVATE ──────────────────────────────────────────── */
        // 重新实现 parseParagraph 以替代原本的setContent的逻辑
        // foreach (string paragraph in paragraphs)
        // {   
        //     parseParagraph(instance, paragraph);
        // }
        //
        /* TODO
        对于相比原字体更大的字体载入时，会出现如下问题
        1、[完成] 依然按照大字体的尺寸显示，但字体Textture读取会偏下一些：这些需要找到切分字体Textture的函数，将其修改按照Textture大小切分，而不是代码中固定值
            1.1、[完成] 需要检查Font类型的getModelPath()以及其wordHeight属性
            1.2、[完成] 发现是生成字母数字贴图时最下面夹带了一行像素，导致读取偏下，修改Font生成脚本即可
        2、太大的字体超过控件大小或文本合并会超出控件可容纳范围，会影响观感：
            2.1、[完成] 需要选取一个合适的尺寸，目前游戏内文本显示 预估10pixel可能既能保证中文阅读又能不太超出
            2.2、[完成] 需要将词排版的词间距或左右padding调小，以便能容纳更多文本
                2.2.1、[完成] 当前词实际显示间距为3pixel，应该是导致word.padding.right=font.wordSpacing=3;导致（词间距会覆盖字符间距使用），故在行添加中文词时将word.padding.right置为1即可
            2.3、[删除，没啥卵用] 行间距需要从添加padding.top修改为添加padding.bottom，并使最后一行不需要添加padding.bottom，可以有效避免单行文本换行导致向下范围过大
            2.4、[完成] bark中文文本渲染其高度依然为7或8，需要修改其内部文本高度
                2.4.1、[完成] 需要检查下BarkControl.Bark中 StringPrinter.bakeFancyString逻辑，获取其中文本的高度
                2.4.2、[完成] 发现原因：是bakeString中生成textureData时，使用的font.wordHeight为默认值7
                2.4.3、[完成] 复写 FontContainer.getTinyFont()，将Font.wordHeight改为10
            2.5、[待完成] 需要查看滚动文本滑块控件，修改滑块产生条件，使得其能在中文情况下也能正常产生滑块并拖动
                2.5.1、对于UIScrollbarStandard创建滑块时设置的高度和绑定控件进行监控（预估控件为UICanvasVertical）
                    2.5.1.1、[完成] UIScrollbar.degree-GUIControl.updateRightScrollbar()-UITextBlock.setScrollIndex(int index)
                    2.5.1.2、[进行中] 发现是UITextBlock.pruneLength()控制scroll的内容裁剪，其中背景信息超出显示可能是因为UITextBlock.isTooLong()为False(可能原因：UITextBlock.enforceHeight为False，或添加行后getHeight()中没有正常增加)
                        2.5.1.2.1 [进行中] 检查 UITextBlock.enforceHeight 为False的更改为True
                               CharacterCreationBackgroundState.guiControl.sheetComplex.mainDescription           
                    2.5.1.3、[删除，没啥乱用] UIElement.add()中发现UIElement.alignElements()负责控件重新分配位置
                    2.5.1.4、[进行中] 检查UICanvasVertical.getHeight()中的stretchVertical是否为True，若stretchVertical==True, 控件会被固定长度，导致文本向下超出以及scroll不对应，修改stretchVertical为False
                    2.5.1.5、[进行中] 发现 CharacterCreationBackgroundState 为设置角色背景
                        2.5.[进行中] 检查
                    2.5.1.6、[进行中] 发现CharacterCreationBackgroundState.setGUIData中this.list.getScrolledStringList中的this.getIndexAtEndOfPage()中的this.getMaxPageSize()返回为固定20，导致显示button必定为20个，致使换行失效
        3、[完成] 部分字体排版会向上对齐（符号和字母）：与Font生成脚本有关，需要检查Font生成脚本
        */

        private static void ParseParagraphComplete(UITextBlock instance, string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            var textBlockType = typeof(UITextBlock);
            var createLineMethod = textBlockType.GetMethod("createLine", BindingFlags.NonPublic | BindingFlags.Instance);
            var shouldCreateNewLineMethod = textBlockType.GetMethod("shouldWeCreateNewLine", BindingFlags.NonPublic | BindingFlags.Instance);
            var getPixelsToNextTabMethod = textBlockType.GetMethod("getPixelsToNextTab", BindingFlags.NonPublic | BindingFlags.Instance);
            var trimBraceMethod = textBlockType.GetMethod("trimBrace", BindingFlags.NonPublic | BindingFlags.Instance);
            var getDrawColorMethod = textBlockType.GetMethod("getDrawColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var pushColorMethod = textBlockType.GetMethod("pushColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var popColorMethod = textBlockType.GetMethod("popColor", BindingFlags.NonPublic | BindingFlags.Instance);

            var currentTooltipWordField = textBlockType.GetField("currentTooltipWord", BindingFlags.NonPublic | BindingFlags.Instance);
            var drawingQuoteField = textBlockType.GetField("drawingQuote", BindingFlags.NonPublic | BindingFlags.Instance);
            var uppercaseTooltipWordsField = textBlockType.GetField("uppercaseTooltipWords", BindingFlags.NonPublic | BindingFlags.Instance);
            var highlightQuotesField = textBlockType.GetField("highlightQuotes", BindingFlags.NonPublic | BindingFlags.Instance);
            var toolTipHighlightColorField = textBlockType.GetField("toolTipHighlightColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var toolTipWordsField = textBlockType.GetField("toolTipWords", BindingFlags.NonPublic | BindingFlags.Instance);
            var multiLineField = textBlockType.GetField("multiLine", BindingFlags.NonPublic | BindingFlags.Instance);

            var wordType = textBlockType.GetNestedType("Word", BindingFlags.NonPublic);
            var letterType = textBlockType.GetNestedType("Letter", BindingFlags.NonPublic);
            var wordCtor = wordType?.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Font) },
                null);
            var letterCtor = letterType?.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(char), typeof(Color32), typeof(Color32), typeof(Font) },
                null);
            var wordGetWidthInLineMethod = wordType?.GetMethod("getWidthInLine", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var wordHighlightWordField = wordType?.GetField("highlightWord", BindingFlags.Public | BindingFlags.Instance);

            if (createLineMethod == null ||
                shouldCreateNewLineMethod == null ||
                getPixelsToNextTabMethod == null ||
                trimBraceMethod == null ||
                getDrawColorMethod == null ||
                pushColorMethod == null ||
                popColorMethod == null ||
                currentTooltipWordField == null ||
                drawingQuoteField == null ||
                uppercaseTooltipWordsField == null ||
                highlightQuotesField == null ||
                toolTipHighlightColorField == null ||
                toolTipWordsField == null ||
                multiLineField == null ||
                wordCtor == null ||
                letterCtor == null ||
                wordGetWidthInLineMethod == null ||
                wordHighlightWordField == null)
            {
                ParseParagraphMethod?.Invoke(instance, new object[] { input });
                return;
            }

            var font = FontField.GetValue(instance) as Font;
            if (font == null)
            {
                return;
            }

            object CreateWord() => wordCtor.Invoke(new object[] { font });
            UIElement CreateLetter(char c, Color32 main, Color32 shadow) => (UIElement)letterCtor.Invoke(new object[] { c, main, shadow, font });
            UICanvasHorizontal CreateLine() => (UICanvasHorizontal)createLineMethod.Invoke(instance, null);
            bool ShouldCreateNewLine(UICanvasHorizontal line, object word) => (bool)shouldCreateNewLineMethod.Invoke(instance, new[] { line, word });
            int GetPixelsToNextTab(UICanvasHorizontal line) => (int)getPixelsToNextTabMethod.Invoke(instance, new object[] { line });
            int GetWordWidthInLine(object word) => (int)wordGetWidthInLineMethod.Invoke(word, null);
            string TrimBrace(string s) => (string)trimBraceMethod.Invoke(instance, new object[] { s });
            Color32 GetDrawColor() => (Color32)getDrawColorMethod.Invoke(instance, null);
            void PushColor(Color32 color) => pushColorMethod.Invoke(instance, new object[] { color });
            void PopColor() => popColorMethod.Invoke(instance, null);
            string CurrentTooltipWord() => (string)currentTooltipWordField.GetValue(instance);
            void SetCurrentTooltipWord(string s) => currentTooltipWordField.SetValue(instance, s);
            bool DrawingQuote() => (bool)drawingQuoteField.GetValue(instance);
            void SetDrawingQuote(bool value) => drawingQuoteField.SetValue(instance, value);
            bool UppercaseTooltipWords() => (bool)uppercaseTooltipWordsField.GetValue(instance);
            bool HighlightQuotes() => (bool)highlightQuotesField.GetValue(instance);
            Color32 ToolTipHighlightColor() => (Color32)toolTipHighlightColorField.GetValue(instance);
            bool MultiLine() => (bool)multiLineField.GetValue(instance);
            void SetWordHighlightWord(object word, string s) => wordHighlightWordField.SetValue(word, s);

            UICanvasHorizontal lineCanvas = CreateLine();
            if (LanguageManager.NoLetterLanguage())
            {
                ((UIElement)lineCanvas).setPaddingTop(((UIElement)lineCanvas).padding.top + 1);   // 行高增加一个1pixel
                //TranslationMod.Logger?.LogError($"Line Height {((UIElement)lineCanvas).padding.top}");
            }
            
            object wordObj = CreateWord();
            bool headerFlag = false;

            int i = 0;
            while (i < input.Length)
            {
                // 处理 < 标签符号
                // 标签内文本还是按照正常字符处理，只不过此时color已为push状态或是tag:flag
                if (input[i] == '<')
                {
                    string text = input.Substring(i);
                    int num = text.IndexOf(">") + 1;
                    if (num > 0)
                    {
                        if (text.IndexOf(C64Color.HEADER_TAG_CONTENT) == 1)                 // <Header></Header>标签
                        {
                            PushColor(C64Color.HeaderColor);
                            headerFlag = true;
                        }
                        else if (text.IndexOf("/" + C64Color.HEADER_TAG_CONTENT) == 1)
                        {
                            PopColor();
                            headerFlag = false;
                        }
                        else if (text.IndexOf("tag") == 1 && !headerFlag)                   // <tag></tag>标签
                        {
                            SetCurrentTooltipWord(text.Substring(num).Split(new[] { '<' }, StringSplitOptions.None)[0]);
                            PushColor(ToolTipHighlightColor());
                        }
                        else if (text.IndexOf("/tag") == 1 && !headerFlag && CurrentTooltipWord() != "")
                        {
                            SetCurrentTooltipWord("");
                            PopColor();
                        }
                        else
                        {
                            string text2 = TrimBrace(text.Substring(0, num));
                            if (text2.IndexOf("color=".ToUpper()) == 1)                     // <color=#xxxxxx></color>标签
                            {
                                int startIndex = text2.IndexOf("#".ToUpper());
                                Color color;
                                if (startIndex >= 0 &&
                                    startIndex + 7 <= text2.Length &&
                                    ColorUtility.TryParseHtmlString(text2.Substring(startIndex, 7), out color))
                                {
                                    PushColor(color);
                                }
                            }
                            else if (text2.IndexOf("/color".ToUpper()) == 1)
                            {
                                PopColor();
                            }
                        }

                        if (num > 1)
                        {
                            i += num - 1;
                        }

                        if (i >= input.Length - 1)
                        {
                            if (ShouldCreateNewLine(lineCanvas, wordObj))
                            {
                                lineCanvas = CreateLine();
                            }
                            lineCanvas.add((UIElement)wordObj);
                            break;
                        }

                        i++;
                        continue;
                    }
                }
                else if (input[i] == '\t')
                {
                    lineCanvas.add((UIElement)wordObj);
                    object tabWord = CreateWord();
                    ((UICanvasHorizontal)tabWord).setWidth(GetPixelsToNextTab(lineCanvas));
                    lineCanvas.add((UIElement)tabWord);
                    wordObj = CreateWord();
                    i++;
                    continue;
                }

                // 一般字符处理流程
                if (input[i] != ' ')
                {
                    bool closingQuote = false;
                    // 若发现引号时则高亮表示引用直至发现下一个引号
                    if (input[i] == '"' && HighlightQuotes())
                    {
                        if (DrawingQuote())
                        {
                            closingQuote = true;
                        }
                        SetDrawingQuote(!DrawingQuote());
                    }

                    UIElement letter;
                    // 若为currentTooltipWord，则将当前字符转为大写 
                    if (CurrentTooltipWord() != "")
                    {
                        char ch = UppercaseTooltipWords() ? char.ToUpper(input[i]) : input[i];
                        letter = CreateLetter(ch, GetDrawColor(), instance.foregroundColors.shadowMainColor);
                    }
                    // 若为引号，则对字符进行引用着色
                    else if (DrawingQuote() || closingQuote)
                    {
                        letter = CreateLetter(input[i], C64Color.SmallTextQuoteColor, instance.foregroundColors.shadowMainColor);
                    }
                    else
                    {
                        letter = CreateLetter(input[i], GetDrawColor(), instance.foregroundColors.shadowMainColor);
                    }

                    letter.foregroundColors.hoverColor = instance.foregroundColors.hoverColor;
                    letter.foregroundColors.leftClickedColor = instance.foregroundColors.leftClickedColor;
                    letter.foregroundColors.outlineMainColor = instance.foregroundColors.outlineMainColor;
				    
                    // 若 不允许多行 或 允许多行但词宽+行宽小于控件总宽，则当前词添加此字符
                    // getWidthInLine()为当前词宽减去一个字母间距再减去一个词间距
                    if (MultiLine() || (!MultiLine() && lineCanvas.getWidth() + GetWordWidthInLine(wordObj) <= instance.getBaseWidth()))
                    {
                        ((UIElement)wordObj).padding.right = 1; 
                        ((UICanvasHorizontal)wordObj).add(letter);
                    }

                    //若当前字符还在<tag>...</tag>内部
                    if (CurrentTooltipWord() != "")
                    {
                        SetWordHighlightWord(wordObj, CurrentTooltipWord());                                    // 当前词可能不完整，后续会依照highlightWord进行判定
                        var toolTipWords = toolTipWordsField.GetValue(instance) as System.Collections.IList;
                        if (toolTipWords != null && !toolTipWords.Contains(wordObj))                             // 在toolTipWords集合中添加当前toolTip词（关键是其中highlightWord属性）（不重复添加）
                        {
                            toolTipWords.Add(wordObj);
                        }
                    }
                }

                // 若当前字符为空 或 当前字符为最后一个 或 当前词宽已大于控件宽，则需要判断是否结算换行（可认为这为词排版对象结束条件）
                // 现添加一个IsNonAlphanumericSymbol用于判断当前字符是否为不为字母数字符号，若不为字母数字符号，则当前字符可视为词，布局时具有词间距和词换行（最好还是要加个当前判断）
                if (input[i] == ' ' ||
                    i >= input.Length - 1 ||
                    (GetWordWidthInLine(wordObj) >= instance.getBaseWidth() && !instance.stretchHorizontal) ||
                    (LanguageManager.NoLetterLanguage() && IsNonAlphanumericSymbol(input[i])))
                {
                    bool needNewLine = ShouldCreateNewLine(lineCanvas, wordObj);    // 加上此词后是否超过控件宽flag
                    if (input[i] == ' ')    // 若当前字符为空，则正常将字符添加词排版对象后
                    {
                        ((UICanvasHorizontal)wordObj).add(CreateLetter(input[i], GetDrawColor(), instance.foregroundColors.shadowMainColor));
                    }

                    if (needNewLine)        // 若超宽，则添加新行后添加此词
                    {
                        lineCanvas = CreateLine();
                        if (LanguageManager.NoLetterLanguage()){
                           ((UIElement)lineCanvas).setPaddingTop(((UIElement)lineCanvas).padding.top + 1);   // 行高增加一个1pixel 
                        }
                        
                        lineCanvas.add((UIElement)wordObj);
                    }
                    else if (i == input.Length - 1)     //若为最后一个词，则添加此词后创建新行
                    {
                        lineCanvas.add((UIElement)wordObj);
                        lineCanvas = CreateLine();
                        if (LanguageManager.NoLetterLanguage())
                        {
                            ((UIElement)lineCanvas).setPaddingTop(((UIElement)lineCanvas).padding.top + 1);   // 行高增加一个1pixel
                        }
                        
                    }
                    else                                // 对于IsNonAlphanumericSymbol(input[i])字符将会走到这，可视为将此字符作为词排版
                    {
                        lineCanvas.add((UIElement)wordObj);
                    }

                    wordObj = CreateWord(); //清空词排版对象
                }

                i++;
            }

            if (lineCanvas != null && lineCanvas.getElements().Count == 0)
            {
                instance.getElements().Remove(lineCanvas);
            }
        }

        private static bool IsNonAlphanumericSymbol(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                return false;
            }

            if (c > sbyte.MaxValue)
            {
                return true;
            }

            return !char.IsLetterOrDigit(c);
        }

        /// строит приближённый паттерн по ключу
        private static string BuildPattern(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            // «Очки развития» → «Очк\w* \s+ разви\w*»
            var tokens = key.Split(new[] { ' ', '\t' },
                                StringSplitOptions.RemoveEmptyEntries);

            var parts = tokens.Select(t =>
            {
                if (string.IsNullOrEmpty(t))
                    return string.Empty;

                // берём 85 % слова (но ≥ 3 символов), остальное – любое окончание
                int keep = Math.Max(3, (int)Math.Ceiling(t.Length * 0.65));
                // Убеждаемся, что keep не превышает длину строки
                keep = Math.Min(keep, t.Length);
                
                // Для очень коротких слов (1-2 символа) используем полное слово
                if (t.Length <= 2)
                    keep = t.Length;
                
                string stem = Regex.Escape(t.Substring(0, keep));
                return stem + @"[\w\.-]*";
            }).Where(p => !string.IsNullOrEmpty(p));

            return $@"\b{string.Join(@"\s+", parts)}\b";
        }
    }
}
