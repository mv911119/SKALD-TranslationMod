using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    /// <summary>
    /// Tooltip 翻译补丁。
    /// 负责在点击已翻译的关键词时，把它映射回原始 key，
    /// 再调用游戏原始 tooltip 逻辑，保证中文界面下提示仍然可用。
    /// </summary>
    [HarmonyPatch]
    public static class TooltipTranslationPatch
    {
        /// <summary>
        /// 延迟初始化翻译服务。
        /// </summary>
        private static readonly Lazy<TranslationService> _translator =
            new(() => new TranslationService());

        /// <summary>
        /// 访问 tooltip 映射缓冲区时使用的锁对象。
        /// </summary>
        private static readonly object _lockObject = new();

        /// <summary>
        /// 定位 tooltip 查询方法。
        /// 流程：先找到 `ToolTipControl` 的内部 `ToolTipCategory` 类型，
        /// 再解析 `getToolTip(string)` 方法。
        /// </summary>
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            // 查找 ToolTipControl.ToolTipCategory.getToolTip
            var toolTipControlType = AccessTools.TypeByName("ToolTipControl");
            if (toolTipControlType == null)
            {
                TranslationMod.Logger?.LogError("[TooltipTranslationPatch] ToolTipControl type not found");
                return null;
            }

            var toolTipCategoryType = toolTipControlType.GetNestedType("ToolTipCategory", BindingFlags.Public | BindingFlags.NonPublic);
            if (toolTipCategoryType == null)
            {
                TranslationMod.Logger?.LogError("[TooltipTranslationPatch] ToolTipCategory nested type not found");
                return null;
            }

            var getToolTipMethod = toolTipCategoryType.GetMethod("getToolTip", new[] { typeof(string) });
            if (getToolTipMethod == null)
            {
                TranslationMod.Logger?.LogError("[TooltipTranslationPatch] getToolTip method not found");
                return null;
            }

#if DEBUG
            TranslationMod.Logger?.LogInfo("[TooltipTranslationPatch] Successfully found getToolTip method");
#endif
            return getToolTipMethod;
        }

        /// <summary>
        /// 前置拦截 tooltip 查询。
        /// 流程：根据翻译后的关键词查询原始 key，命中时调用原方法返回结果，
        /// 未命中则回退到原始流程。
        /// </summary>
        [HarmonyPrefix]
        static bool Prefix(object __instance, string keyword, ref object __result)
        {
            try
            {
                var currentLanguagePack = LanguageManager.GetCurrentLanguagePack();
                if (currentLanguagePack == null || currentLanguagePack.Name.Equals("English", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 使用 `UITextBlockSetContentPatch` 维护的 tooltip key 映射
                string originalKeyword = null;
                bool hasMapping = false;
                
                lock (_lockObject)
                {
                    hasMapping = UITextBlockSetContentPatch.TooltipKeyBuffer.TryGetValue(keyword, out originalKeyword);
                }

                if (!hasMapping)
                {
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[TooltipTranslationPatch] No mapping found for keyword: '{keyword}', using original method");
#endif
                    return true; // 未命中映射时继续执行原方法
                }

#if DEBUG
            TranslationMod.Logger?.LogInfo($"[TooltipTranslationPatch] Translating tooltip keyword: '{keyword}' -> '{originalKeyword}'");
#endif

                // 用原始关键词调用游戏原方法
                var originalMethod = TargetMethod();
                __result = originalMethod.Invoke(__instance, new object[] { originalKeyword });
                
                return false; // 已得到结果，跳过原始调用
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[TooltipTranslationPatch] Error in Prefix: {ex.Message}");
                return true; // 异常时回退到原始逻辑
            }
        }
    }
} 
