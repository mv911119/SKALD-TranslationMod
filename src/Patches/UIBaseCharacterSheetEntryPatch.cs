using HarmonyLib;
using System;
using System.Reflection;

namespace TranslationMod.Patches
{
    [HarmonyPatch]
    public static class UIBaseCharacterSheetEntryPatch
    {
        [HarmonyTargetMethod]
        /// <summary>
        /// 定位角色面板条目内部类的构造函数。
        /// 用于在条目创建后调整文本和按钮布局。
        /// </summary>
        private static MethodBase TargetMethod()
        {
            var editorSheetEntryType = AccessTools.Inner(typeof(UIBaseCharacterSheet), "EditorSheetEntry");
            if (editorSheetEntryType == null)
            {
                TranslationMod.Logger?.LogError("[UIBaseCharacterSheetEntryPatch] Cannot find UIBaseCharacterSheet.EditorSheetEntry type");
                return null;
            }

            var ctor = AccessTools.Constructor(editorSheetEntryType, Type.EmptyTypes);
            if (ctor == null)
            {
                TranslationMod.Logger?.LogError("[UIBaseCharacterSheetEntryPatch] Cannot find EditorSheetEntry constructor");
                return null;
            }

            return ctor;
        }

        [HarmonyPostfix]
        /// <summary>
        /// 在条目创建后修正点数文本块高度和加号列内边距。
        /// 流程：读取内部字段，结合当前小字体高度重新布局关键子控件。
        /// </summary>
        private static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                var tiny = FontContainer.getTinyFont();
                if (tiny == null)
                {
                    return;
                }

                int pointBlockHeight = tiny.wordHeight;
                int plusTopPadding = 11 - 7 + tiny.wordHeight;

                var instanceType = __instance.GetType();

                var pointBlockField = AccessTools.Field(instanceType, "pointBlock");
                var pointBlock = pointBlockField?.GetValue(__instance) as UITextBlock;
                if (pointBlock != null)
                {
                    pointBlock.setHeight(pointBlockHeight);
                }

                var plusColumnField = AccessTools.Field(instanceType, "plusColumn");
                var plusColumn = plusColumnField?.GetValue(__instance) as UICanvasVertical;
                if (plusColumn != null)
                {
                    var padding = plusColumn.padding;
                    padding.top = plusTopPadding;
                    plusColumn.padding = padding;
                }
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[UIBaseCharacterSheetEntryPatch] Failed to patch EditorSheetEntry: {ex.Message}");
            }
        }
    }
}
