using HarmonyLib;

namespace TranslationMod.Patches
{
    [HarmonyPatch(typeof(CharacterBuilderBaseState))]
    public static class CharacterBuilderBaseStatePatch
    {
        [HarmonyPatch("setGUIData")]
        [HarmonyPostfix]
        /// <summary>
        /// 在角色构建界面刷新 GUI 数据后，同步左右滚动条与当前可见列表。
        /// 流程：读取列表与 GUI 控件，更新左侧滚动索引，重建可见按钮，再刷新右侧描述滚动条。
        /// </summary>
        private static void SetGuiDataPostfix(object __instance)
        {
            try
            {
                var stateType = __instance.GetType();
                var listField = AccessTools.Field(stateType, "list");
                var guiControlField = AccessTools.Field(stateType, "guiControl") ??
                                      AccessTools.Field(typeof(StateBase), "guiControl");

                var list = listField?.GetValue(__instance) as SkaldObjectList;
                var guiControl = guiControlField?.GetValue(__instance) as GUIControl;

                if (list == null || guiControl == null)
                {
                    return;
                }

                // Keep left list scrollbar in sync with full list count and apply resulting page index.
                int scrollIndex = guiControl.updateLeftScrollBarAndReturnIndex(list.getCount());
                if (scrollIndex != -1)
                {
                    list.setScrollIndex(scrollIndex);
                }

                // Rebuild visible page after scroll change.
                guiControl.setListButtons(list.getScrolledStringList());

                // Keep description scrollbar reactive on GUI refresh.
                guiControl.updateRightScrollBar();
            }
            catch (System.Exception ex)
            {
                TranslationMod.Logger?.LogError($"[CharacterBuilderBaseStatePatch] setGUIData patch failed: {ex.Message}");
            }
        }
    }
}
