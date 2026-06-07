using HarmonyLib;
using System.Reflection;

namespace TranslationMod.Patches
{
    [HarmonyPatch]
    public static class SkaldObjectListPatch
    {
        private static readonly FieldInfo MaxPageSizeField =
            AccessTools.Field(typeof(SkaldObjectList), "maxPageSize");

        [HarmonyPatch(typeof(SkaldObjectList), MethodType.Constructor, new System.Type[] { })]
        [HarmonyPostfix]
        /// <summary>
        /// 无参构造后设置默认分页大小，避免中文字体导致每页项数异常。
        /// </summary>
        private static void CtorPostfix_NoArgs(SkaldObjectList __instance)
        {
            SetDefaultMaxPageSize(__instance);
        }

        [HarmonyPatch(typeof(SkaldObjectList), MethodType.Constructor, new[] { typeof(string) })]
        [HarmonyPostfix]
        /// <summary>
        /// 带标题构造后设置默认分页大小，与无参构造保持一致。
        /// </summary>
        private static void CtorPostfix_WithTitle(SkaldObjectList __instance)
        {
            SetDefaultMaxPageSize(__instance);
        }

        [HarmonyPatch(typeof(SkaldObjectList), "setMaxPageSize")]
        [HarmonyPrefix]
        /// <summary>
        /// 在设置分页大小前按当前字体高度重算页容量。
        /// 这样当字体高度变化时，列表仍能维持合理的可见行数。
        /// </summary>
        private static void SetMaxPageSizePrefix(ref int newSize)
        {
            
            var tiny = FontContainer.getTinyFont();
            if (tiny == null)
            {
                return;
            }

            newSize = newSize * 9 / (tiny.wordHeight + 2);
        }

        /// <summary>
        /// 直接写入列表对象的默认 `maxPageSize`。
        /// 用于统一初始化时的页大小基线。
        /// </summary>
        private static void SetDefaultMaxPageSize(SkaldObjectList instance)
        {
            if (instance == null || MaxPageSizeField == null)
            {
                return;
            }

            MaxPageSizeField.SetValue(instance, 13);
        }
    }
}
