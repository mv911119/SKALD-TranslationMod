using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    [HarmonyPatch(typeof(ImageButtonControl), "createButton")]
    public static class ImageButtonControlPatch
    {
        private static readonly Type UITextButtonType = AccessTools.Inner(typeof(UIButtonControlBase), "UITextButton");
        private static readonly ConstructorInfo UITextButtonCtor = UITextButtonType == null
            ? null
            : AccessTools.Constructor(UITextButtonType, new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(Color32) });
        private static readonly FieldInfo ImagePathField = AccessTools.Field(typeof(ImageButtonControl), "imagePath");
        private static readonly FieldInfo HoverImagePathField = AccessTools.Field(typeof(ImageButtonControl), "hoverImagePath");
        private static readonly FieldInfo PressedImagePathField = AccessTools.Field(typeof(ImageButtonControl), "pressedImagePath");

        [HarmonyPrefix]
        /// <summary>
        /// 重写图片按钮创建流程。
        /// 流程：读取按钮贴图路径，按当前语言决定按钮高度，
        /// 构造自定义按钮对象并设置三态贴图后直接返回。
        /// </summary>
        private static bool Prefix(object __instance, ref object __result)
        {
            if (UITextButtonCtor == null || __instance == null)
            {
                return true;
            }

            string imagePath = ImagePathField?.GetValue(__instance) as string ?? "";
            string hoverImagePath = HoverImagePathField?.GetValue(__instance) as string ?? "";
            string pressedImagePath = PressedImagePathField?.GetValue(__instance) as string ?? "";

            int height = 9;
            if (LanguageManager.NoLetterLanguage())
            {
                height = 11;
            }

            var button = UITextButtonCtor.Invoke(new object[] { 0, 0, 9, height, C64Color.GrayLight }) as UIElement;
            if (button == null)
            {
                return true;
            }

            var padding = button.padding;
            padding.bottom = 2;
            button.padding = padding;

            button.backgroundTexture = TextureTools.loadTextureData(imagePath);
            button.backgroundTextureHover = TextureTools.loadTextureData(hoverImagePath);
            button.backgroundTexturePressed = TextureTools.loadTextureData(pressedImagePath);
            button.setAllowDoubleClick(false);

            __result = button;
            return false;
        }
    }
}
