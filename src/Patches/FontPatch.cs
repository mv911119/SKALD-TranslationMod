using HarmonyLib;
using System;
using System.Reflection;

namespace TranslationMod.Patches
{
    [HarmonyPatch]
    public static class FontPatch
    {
        [HarmonyTargetMethod]
        /// <summary>
        /// 定位 `FontContainer.getTinyFont` 作为补丁目标。
        /// 流程：先通过反射找到类型，再解析目标方法，失败时输出日志。
        /// </summary>
        private static MethodBase TargetMethod()
        {
            var fontContainerType = AccessTools.TypeByName("FontContainer");
            if (fontContainerType == null)
            {
                TranslationMod.Logger?.LogError("[FontPatch] Cannot find FontContainer type");
                return null;
            }

            var method = AccessTools.Method(fontContainerType, "getTinyFont");
            if (method == null)
            {
                TranslationMod.Logger?.LogError("[FontPatch] Cannot find FontContainer.getTinyFont method");
                return null;
            }

            return method;
        }

        [HarmonyPostfix]
        /// <summary>
        /// 在获取小字体后按需修正 `wordHeight`。
        /// 流程：仅对中日韩等无字母语言生效，优先写字段，字段不可写时再尝试属性。
        /// </summary>
        private static void Postfix(object __result)
        {
            try
            {
                if (__result == null)
                {
                    return;
                }
                
                if (LanguageManager.NoLetterLanguage()){
                    var resultType = __result.GetType();

                    var wordHeightField = AccessTools.Field(resultType, "wordHeight");
                    if (wordHeightField != null && wordHeightField.FieldType == typeof(int))
                    {
                        wordHeightField.SetValue(__result, 10);
                        return;
                    }

                    // 备用设置方案
                    var wordHeightProperty = AccessTools.Property(resultType, "wordHeight");
                    if (wordHeightProperty != null && wordHeightProperty.CanWrite && wordHeightProperty.PropertyType == typeof(int))
                    {
                        wordHeightProperty.SetValue(__result, 10, null);
                    }
                }
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[FontPatch] Failed to set wordHeight: {ex.Message}");
            }
        }
    }
}
