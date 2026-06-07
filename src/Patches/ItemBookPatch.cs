using HarmonyLib;
using System;
using System.Collections.Generic;
using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    /// <summary>
    /// 书籍内容补丁。
    /// 在 `ItemBook.getContent` 返回后翻译每个片段，并在过长时按游戏限制拆分。
    /// </summary>
    [HarmonyPatch(typeof(ItemBook), "getContent")]
    public static class ItemBookPatch
    {
        /// <summary>
        /// 延迟初始化翻译服务。
        /// </summary>
        private static readonly Lazy<TranslationService> _translator =
            new(() => new TranslationService());

        /// <summary>
        /// 单条文本在拆分前允许的最大长度。
        /// </summary>
        private const int MAX_STRING_LENGTH = 295;

        /// <summary>
        /// `getContent` 后置处理。
        /// 流程：读取原始书籍内容，逐项翻译返回列表，必要时拆分长文本，
        /// 最后把新列表写回 `__result`。
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(ItemBook __instance, ref object __result)
        {
            try
            {
                // 读取当前书籍对象的原始内容
                var rawData = __instance.getRawData();
                
                // 输出书籍调试信息
                TranslationMod.Logger?.LogInfo($"[ItemBookPatch] ItemBook.getContent() called:");
                TranslationMod.Logger?.LogInfo($"  - getRawData() result: {rawData.content}");
                
                // 返回结果不为空时逐项翻译
                if (__result != null)
                {
                    var translatedResult = new List<string>();
                    TranslationMod.Logger?.LogInfo($"  - getContent() result:");
                    List<string> list = __result as List<string>;
                    foreach (string item in list)
                    {
                        // 跳过空字符串
                        if (string.IsNullOrWhiteSpace(item)) continue;
                        
                        string translatedItem = _translator.Value.Process(item);
                        TranslationMod.Logger?.LogInfo($"  - Item: {item}");
                        TranslationMod.Logger?.LogInfo($"  - Translated: {translatedItem}");
                        
                        TranslationMod.Logger?.LogInfo($"[ItemBookPatch] {translatedItem.Length}");
                        // 超过上限时拆分为多段
                        if (translatedItem.Length > MAX_STRING_LENGTH)
                        {
                            var splitItems = TextDataExtractor.SplitText(translatedItem, MAX_STRING_LENGTH);
                            translatedResult.AddRange(splitItems);
                            
                            TranslationMod.Logger?.LogInfo($"  - Split into {splitItems.Count} parts due to length ({translatedItem.Length} > {MAX_STRING_LENGTH})");
                        }
                        else
                        {
                            translatedResult.Add(translatedItem);
                        }
                    }
                    
                    // 保证结果数量为偶数，兼容原界面展示逻辑
                    if (translatedResult.Count % 2 != 0)
                    {
                        translatedResult.Add(" ");
                    }
                    
                    // 将翻译后的内容列表回写给原返回值
                    __result = translatedResult;
                    
#if DEBUG
                    TranslationMod.Logger?.LogDebug($"[ItemBookPatch] Original list count: {list.Count}, New list count: {translatedResult.Count}");
#endif
                }
                else
                {
                    TranslationMod.Logger?.LogInfo($"  - getContent() result: null");
                }
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[ItemBookPatch] Error in Postfix patch: {ex.Message}");
#if DEBUG
                TranslationMod.Logger?.LogError($"[ItemBookPatch] Stack trace: {ex.StackTrace}");
#endif
            }
        }
    }
} 
