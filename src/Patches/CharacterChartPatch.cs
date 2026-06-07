using HarmonyLib;
using System.Collections.Generic;
using TranslationMod;
using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    [HarmonyPatch(typeof(StringPrinter), "getCharacterChart")]
    public static class CharacterChartPatch
    {
        private static string _lastAppliedLanguageCode = "";
        private static readonly Dictionary<char, int> _addedCharacters = new();
        private static readonly Dictionary<char, int> _originalCharacters = new();
        private static bool _originalCharactersSaved = false;

        /// <summary>
        /// 在获取字符映射表后注入当前语言包的自定义字符。
        /// 流程：先缓存原始映射，再移除上一语言注入的字符，
        /// 如果当前不是英文则应用语言包映射。
        /// </summary>
        public static void Postfix(ref Dictionary<char, int> __result)
        {
            if (!SaveOriginalChart(__result)) return;

            var currentPack = LanguageManager.GetCurrentLanguagePack();
            string langCode = currentPack?.LanguageCode ?? ConfigKeys.EnglishLanguageCode;
            if (langCode == _lastAppliedLanguageCode) return;

            RemovePreviousCharacters(__result);
            if (currentPack == null || langCode == ConfigKeys.EnglishLanguageCode)
            {
                _lastAppliedLanguageCode = ConfigKeys.EnglishLanguageCode;
                return;
            }
            ApplyPackChart(currentPack, __result);
            _lastAppliedLanguageCode = langCode;
        }

        /// <summary>
        /// 首次保存原始字符映射表。
        /// 该步骤只执行一次，供后续语言切换时回滚或对比使用。
        /// </summary>
        private static bool SaveOriginalChart(Dictionary<char, int> chart)
        {
            if (_originalCharactersSaved) return true;
            foreach (var kv in chart) _originalCharacters[kv.Key] = kv.Value;
            _originalCharactersSaved = true;
            return true;
        }

        /// <summary>
        /// 移除上一轮语言包注入的字符映射。
        /// 避免不同语言之间残留错误字符位置。
        /// </summary>
        private static void RemovePreviousCharacters(Dictionary<char, int> chart)
        {
            foreach (var ch in _addedCharacters.Keys)
                chart.Remove(ch);
            _addedCharacters.Clear();
        }

        /// <summary>
        /// 将当前语言包中的字符映射写入运行时字符表。
        /// 同时记录本轮新增字符，方便下次切换时清理。
        /// </summary>
        private static void ApplyPackChart(LanguagePack pack, Dictionary<char, int> chart)
        {
            var newChart = pack.GetCharacterChart();
            if (newChart == null) return;
            foreach (var kv in newChart)
            {
                chart[kv.Key] = kv.Value;
                _addedCharacters[kv.Key] = kv.Value;
            }
        }
    }
} 
