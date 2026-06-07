using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TranslationMod;

namespace TranslationMod.Patches
{
    [HarmonyPatch]
    public static class LanguageSettingWatcher
    {
        private static string _lastLanguage = "";
        private static bool _processing = false;

        /// <summary>
        /// 搜索需要监听的语言设置相关方法。
        /// 流程：通过反射定位 CarouselSetting 上可能改变语言状态的几个入口，
        /// 收集成功匹配的方法供 Harmony 统一打补丁。
        /// </summary>
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();
            
            try
            {
                var carouselSettingType = AccessTools.Inner(typeof(GlobalSettings.SettingsCollection), GameConstants.CarouselSettingType);
                if (carouselSettingType == null)
                {
                    TranslationMod.Logger?.LogWarning("[LanguageSettingWatcher] Could not find CarouselSetting type - this is normal if game structure changed");
                    return methods;
                }

                // 查找设置状态的方法，兼容不同命名
                var setStateToMethod = AccessTools.Method(carouselSettingType, GameConstants.SetStateToMethod, new[] { typeof(int) })
                                    ?? AccessTools.Method(carouselSettingType, "setState", new[] { typeof(int) })
                                    ?? AccessTools.Method(carouselSettingType, "SetState", new[] { typeof(int) });
                if (setStateToMethod != null)
                {
                    methods.Add(setStateToMethod);
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[LanguageSettingWatcher] Found method for patching: {setStateToMethod.Name}");
#endif
                }

                // 查找切换到下一个状态的方法
                var incrementStateMethod = AccessTools.Method(carouselSettingType, GameConstants.IncrementStateMethod, new[] { typeof(int) })
                                        ?? AccessTools.Method(carouselSettingType, "increment", new[] { typeof(int) })
                                        ?? AccessTools.Method(carouselSettingType, "Increment", new[] { typeof(int) });
                if (incrementStateMethod != null)
                {
                    methods.Add(incrementStateMethod);
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[LanguageSettingWatcher] Found method for patching: {incrementStateMethod.Name}");
#endif
                }

                // 查找应用存档设置的方法
                var applySettingSaveDataMethod = AccessTools.Method(carouselSettingType, GameConstants.ApplySettingSaveDataMethod)
                                            ?? AccessTools.Method(carouselSettingType, "applySaveData")
                                            ?? AccessTools.Method(carouselSettingType, "ApplySaveData");
                if (applySettingSaveDataMethod != null)
                {
                    methods.Add(applySettingSaveDataMethod);
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[LanguageSettingWatcher] Found method for patching: {applySettingSaveDataMethod.Name}");
#endif
                }

                if (methods.Count == 0)
                {
#if DEBUG
                    TranslationMod.Logger?.LogInfo("[LanguageSettingWatcher] No suitable methods found to patch - language detection may not work automatically");
#endif
                }
                else
                {
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[LanguageSettingWatcher] Successfully found {methods.Count} methods to patch");
#endif
                }
            }
            catch (System.Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguageSettingWatcher] Error finding target methods: {e.Message}");
            }
            
            return methods;
        }

        [HarmonyPostfix]
        /// <summary>
        /// 在语言设置相关方法执行后检查当前选择。
        /// 流程：先确认当前对象确实是语言设置项，再读取选中语言，
        /// 与上次不同则触发语言切换。
        /// </summary>
        public static void AfterCarouselCall(object __instance)
        {
            if (_processing) return;
            try
            {
                if (!IsLanguageSetting(__instance)) return;
                _processing = true;
                string name = GetSelectedLanguage(__instance);
                if (!string.IsNullOrEmpty(name) && name != _lastLanguage)
                {
                    LanguageManager.SwitchLanguage(name);
                    _lastLanguage = name;
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[LanguageSettingWatcher] switched to {name}");
#endif
                }
            }
            finally { _processing = false; }
        }

        /// <summary>
        /// 判断给定设置对象是否为语言设置项。
        /// 流程：校验类型名，并检查候选项里是否包含英文作为基准选项。
        /// </summary>
        private static bool IsLanguageSetting(object inst)
        {
            var type = inst.GetType();
            if (type.Name != "CarouselSetting") return false;
            var altField = AccessTools.Field(type, GameConstants.CarouselAlternativesField);
            if (altField?.GetValue(inst) is System.Collections.IList list && list.Count >= 2)
            {
                foreach (var o in list) if (o?.ToString() == GameConstants.EnglishLanguageName) return true;
            }
            return false;
        }

        /// <summary>
        /// 读取当前设置对象选中的语言名称。
        /// 通过状态索引和候选项列表计算最终结果。
        /// </summary>
        private static string GetSelectedLanguage(object inst)
        {
            var type = inst.GetType();
            var stateField = AccessTools.Field(type, GameConstants.CarouselStateField);
            var altField = AccessTools.Field(type, GameConstants.CarouselAlternativesField);
            if (stateField?.GetValue(inst) is int index && altField?.GetValue(inst) is System.Collections.IList list)
            {
                if (index >= 0 && index < list.Count) return list[index]?.ToString();
            }
            return null;
        }
    }
} 
