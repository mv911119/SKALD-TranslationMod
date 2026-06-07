using HarmonyLib;
using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    [HarmonyPatch(typeof(ListButtonControl), "createButton")]
    public static class ListButtonControlPatch
    {
        [HarmonyPostfix]
        /// <summary>
        /// 在列表按钮创建后微调底部内边距。
        /// 仅对无字母语言生效，用于缓解中文等字体显示时的垂直拥挤。
        /// </summary>
        private static void Postfix(object __result)
        {
            if (!LanguageManager.NoLetterLanguage())
            {
                return;
            }

            if (__result is UIElement button)
            {
                button.padding.bottom = 0;
            }
        }
    }
}
