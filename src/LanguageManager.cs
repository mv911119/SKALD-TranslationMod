using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TranslationMod.Configuration;
using TranslationMod.Patches;

namespace TranslationMod
{
    /// <summary>
    /// 语言管理器。
    /// 负责维护当前语言状态、加载语言包、同步游戏设置，并向外广播语言切换事件。
    /// </summary>
    public static class LanguageManager
    {
        public static event Action OnLanguageChanged;
        
        private static bool _isLanguageReady = false;
        private static FieldInfo stateField;
        private static FieldInfo alternativesField;
        
        // 当前语言由游戏状态决定，仅保存在内存中
        private static string _currentLanguageCode = ConfigKeys.EnglishLanguageCode;
        private static string _currentLanguageName = ConfigKeys.EnglishLanguageName;
        
        // 已加载语言包缓存
        private static readonly ConcurrentDictionary<string, LanguagePack> _languagePacks = new();
        private static string _pluginDirectory;

        /// <summary>
        /// 初始化语言系统。
        /// 流程：定位插件目录，通过反射缓存游戏语言设置相关字段，
        /// 再初始化字体补丁，为后续语言切换和字体替换做准备。
        /// </summary>
        public static void Initialize()
        {
            if (_isLanguageReady) return;

            try
            {
                // 初始化插件目录路径
                _pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                // 通过反射获取 CarouselSetting 的私有字段，后续用来读取语言选项状态
                var carouselSettingType = AccessTools.Inner(typeof(GlobalSettings.SettingsCollection), GameConstants.CarouselSettingType);
                if (carouselSettingType == null)
                {
                    TranslationMod.Logger?.LogError("[LanguageManager] Could not find CarouselSetting type via reflection");
                    return;
                }

                stateField = AccessTools.Field(carouselSettingType, GameConstants.CarouselStateField);
                alternativesField = AccessTools.Field(carouselSettingType, GameConstants.CarouselAlternativesField);
                
                if (stateField == null || alternativesField == null)
                {
                    TranslationMod.Logger?.LogError("[LanguageManager] Could not find required fields via reflection");
                    return;
                }
                
                // 初始化字体补丁
                FontAssetPatch.Initialize();
                
                _isLanguageReady = true;
#if DEBUG
                TranslationMod.Logger?.LogInfo("[LanguageManager] Initialized successfully");
#endif
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguageManager] Failed to initialize: {e.Message}");
            }
        }

        /// <summary>
        /// 切换当前语言。
        /// 流程：先根据语言名解析语言代码，再刷新当前内存状态，
        /// 加载对应语言包，最后通知订阅者执行界面和字体刷新。
        /// </summary>
        /// <param name="newLanguageName">新的语言显示名</param>
        public static void SwitchLanguage(string newLanguageName)
        {
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[LanguageManager] Attempting to switch language to: {newLanguageName}");
#endif
            
            string newLanguageCode = GetLanguageCodeByName(newLanguageName) 
                                     ?? ConfigKeys.EnglishLanguageCode;

            // 将当前语言状态保存到内存，供翻译与字体逻辑读取
            _currentLanguageCode = newLanguageCode;
            _currentLanguageName = newLanguageName;
            
            LoadLanguagePackByCode(newLanguageCode);
            
            OnLanguageChanged?.Invoke();
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[LanguageManager] Language switched to '{newLanguageName}' ({newLanguageCode}). Event invoked.");
#endif
        }

        /// <summary>
        /// 获取当前语言代码。
        /// 该值以游戏当前语言设置为准，而不是插件配置文件。
        /// </summary>
        public static string GetCurrentLanguageCode()
        {
            return _currentLanguageCode;
        }

        /// <summary>
        /// 获取当前语言名称。
        /// 该值以游戏当前语言设置为准，而不是插件配置文件。
        /// </summary>
        public static string GetCurrentLanguage()
        {
            return _currentLanguageName;
        }

        // 无字母语言（例如中文、韩语、日语等）
        public static bool NoLetterLanguage()
        {   
            if (_currentLanguageName.Equals("Chinese", StringComparison.OrdinalIgnoreCase) ||
               _currentLanguageName.Equals("Japanese", StringComparison.OrdinalIgnoreCase) ||
               _currentLanguageName.Equals("Korean", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取当前已加载的语言包。
        /// 若系统尚未初始化或当前为英文，则可能返回空。
        /// </summary>
        public static LanguagePack GetCurrentLanguagePack()
        {
            if (!_isLanguageReady)
            {
                // TranslationMod.Logger.LogWarning("[LanguageManager] GetCurrentLanguagePack called before language system is ready.");
                return null;
            }
            return GetLanguagePack(GetCurrentLanguageCode());
        }

        /// <summary>
        /// 主动触发语言切换事件。
        /// 用于在不重新解析语言的情况下，强制刷新依赖语言状态的组件。
        /// </summary>
        public static void TriggerLanguageChange()
        {
            OnLanguageChanged?.Invoke();
#if DEBUG
            TranslationMod.Logger?.LogInfo("[LanguageManager] Language change event triggered manually");
#endif
        }

        /// <summary>
        /// 仅更新当前语言状态，不主动加载语言包。
        /// 主要用于与游戏配置做同步时，先修正内存中的语言信息。
        /// </summary>
        /// <param name="languageName">语言名称</param>
        /// <param name="languageCode">语言代码，可为空</param>
        internal static void UpdateCurrentLanguage(string languageName, string languageCode = null)
        {
            if (string.IsNullOrEmpty(languageName))
                return;

            _currentLanguageName = languageName;
            
            if (!string.IsNullOrEmpty(languageCode))
            {
                _currentLanguageCode = languageCode;
            }
            else
            {
                // 未显式提供语言代码时，根据名称反查
                _currentLanguageCode = GetLanguageCodeByName(languageName) 
                                     ?? ConfigKeys.EnglishLanguageCode;
            }
            
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[LanguageManager] Current language updated to: '{_currentLanguageName}' ({_currentLanguageCode})");
#endif
        }

        /// <summary>
        /// 将语言管理器状态与当前游戏设置同步。
        /// 流程：读取游戏设置中的语言选项，解析当前项，
        /// 成功则执行切换，失败则回退到英文。
        /// </summary>
        public static void SynchronizeWithGame()
        {
            try
            {
#if DEBUG
                TranslationMod.Logger?.LogInfo("[LanguageManager] Starting initial language synchronization with the game");
#endif
                
                var gameplaySettings = GlobalSettings.getGamePlaySettings();
                if (gameplaySettings == null)
                {
                    TranslationMod.Logger?.LogWarning("[LanguageManager] GamePlaySettings not available for synchronization");
                    return;
                }

                var languageSetting = gameplaySettings.getObject(GameConstants.LanguageSettingId);
                if (languageSetting == null)
                {
#if DEBUG
                    TranslationMod.Logger?.LogInfo("[LanguageManager] Language setting not found in game, using default English");
#endif
                    UpdateCurrentLanguage(ConfigKeys.EnglishLanguageName, ConfigKeys.EnglishLanguageCode);
                    return;
                }

                // 从游戏设置对象中提取当前语言名称
                string currentGameLanguage = ExtractLanguageFromSetting(languageSetting);
                if (!string.IsNullOrEmpty(currentGameLanguage))
                {
#if DEBUG
                    TranslationMod.Logger?.LogInfo($"[LanguageManager] Synchronized with game language: '{currentGameLanguage}'");
#endif
                    SwitchLanguage(currentGameLanguage);
                }
                else
                {
                    TranslationMod.Logger?.LogWarning("[LanguageManager] Could not extract language from game setting, using default");
                    UpdateCurrentLanguage(ConfigKeys.EnglishLanguageName, ConfigKeys.EnglishLanguageCode);
                }
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguageManager] Error during game synchronization: {e.Message}");
                // 异常时回退为英文，保证系统处于可用状态
                UpdateCurrentLanguage(ConfigKeys.EnglishLanguageName, ConfigKeys.EnglishLanguageCode);
            }
        }

        /// <summary>
        /// 从游戏语言设置对象中提取当前语言名称。
        /// 流程：反射读取当前索引和可选项列表，再根据索引取出选中的语言名。
        /// </summary>
        private static string ExtractLanguageFromSetting(object languageSetting)
        {
            try
            {
                if (languageSetting == null) return null;
                
                var instanceType = languageSetting.GetType();
                
                // 读取当前选项索引与候选语言列表
                var stateField = AccessTools.Field(instanceType, GameConstants.CarouselStateField);
                var alternativesField = AccessTools.Field(instanceType, GameConstants.CarouselAlternativesField);
                
                if (stateField == null || alternativesField == null)
                {
                    TranslationMod.Logger?.LogDebug("[ExtractLanguageFromSetting] Required fields not found");
                    return null;
                }
                
                var stateValue = stateField.GetValue(languageSetting);
                var alternativesValue = alternativesField.GetValue(languageSetting);
                
                if (stateValue is int currentIndex && alternativesValue is System.Collections.IList alternatives)
                {
                    if (currentIndex >= 0 && currentIndex < alternatives.Count)
                    {
                        string selectedLanguage = alternatives[currentIndex]?.ToString();
#if DEBUG
                        TranslationMod.Logger?.LogDebug($"[ExtractLanguageFromSetting] Extracted language: '{selectedLanguage}' at index {currentIndex}");
#endif
                        return selectedLanguage;
                    }
                }
                
                return null;
            }
            catch (Exception e)
            {
#if DEBUG
                TranslationMod.Logger?.LogDebug($"[ExtractLanguageFromSetting] Error: {e.Message}");
#endif
                return null;
            }
        }
        
        /// <summary>
        /// 按语言代码加载语言包。
        /// 流程：英文直接清空缓存返回；否则扫描语言包目录，
        /// 找到匹配代码的包后实例化并缓存。
        /// </summary>
        /// <param name="languageCode">语言代码</param>
        public static void LoadLanguagePackByCode(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode) || languageCode.Equals(ConfigKeys.EnglishLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                _languagePacks.Clear();
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[LanguageManager] Switched to English. No language pack needed.");
#endif
                return;
            }

            _languagePacks.Clear();
            
            try
            {
                string languagePacksPath = Path.Combine(_pluginDirectory, ConfigurationManager.PluginConfig.LanguagePacksPath.Value);
                
                if (!Directory.Exists(languagePacksPath))
                {
                    TranslationMod.Logger?.LogWarning($"[LanguageManager] Language packs directory not found: {languagePacksPath}");
                    return;
                }

                var directories = Directory.GetDirectories(languagePacksPath);
                
                foreach (var directory in directories)
                {
                    string configFile = Path.Combine(directory, ConfigKeys.LanguagePackConfigFileName);
                    
                    if (File.Exists(configFile))
                    {
                        try
                        {
                            var languagePack = new LanguagePack(configFile, directory);
                            if (languagePack.IsValid() && 
                                string.Equals(languagePack.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
                            {
                                _languagePacks[languageCode] = languagePack;
#if DEBUG
                                TranslationMod.Logger?.LogInfo($"[LanguageManager] Successfully loaded language pack: {languagePack.Name} ({languageCode})");
#endif
                                return;
                            }
                        }
                        catch (Exception e)
                        {
                            TranslationMod.Logger?.LogWarning($"[LanguageManager] Failed to load language pack from '{directory}': {e.Message}");
                        }
                    }
                }
                
                TranslationMod.Logger?.LogWarning($"[LanguageManager] No language pack found for code: {languageCode}");
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguageManager] Error loading language pack {languageCode}: {e.Message}");
            }
        }

        /// <summary>
        /// 根据语言代码获取已缓存的语言包。
        /// 英文或未找到时返回空。
        /// </summary>
        public static LanguagePack GetLanguagePack(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode) || languageCode.Equals(ConfigKeys.EnglishLanguageCode, StringComparison.OrdinalIgnoreCase))
                return null;
            
            _languagePacks.TryGetValue(languageCode, out LanguagePack languagePack);
            return languagePack;
        }

        /// <summary>
        /// 获取可用语言名称列表。
        /// 流程：扫描语言包目录，读取每个包的配置，并收集有效包的显示名。
        /// </summary>
        public static List<string> GetAvailableLanguageNames()
        {
            var result = new List<string>();
            
            try
            {
                string languagePacksPath = Path.Combine(_pluginDirectory, ConfigurationManager.PluginConfig.LanguagePacksPath.Value);
                
                if (!Directory.Exists(languagePacksPath))
                {
                    return result;
                }

                var directories = Directory.GetDirectories(languagePacksPath);
                TranslationMod.Logger?.LogInfo($"[LanguageManager] Found {directories.Length} language packs");
                foreach (var directory in directories)
                {
                    string configFile = Path.Combine(directory, ConfigKeys.LanguagePackConfigFileName);
                    
                    if (File.Exists(configFile))
                    {
                        try
                        {
                            TranslationMod.Logger?.LogInfo($"[LanguageManager] Loading language pack from '{directory}'");
                            var languagePack = new LanguagePack(configFile, directory);
                            if (languagePack.IsValid() && !string.IsNullOrEmpty(languagePack.Name))
                            {
                                TranslationMod.Logger?.LogInfo($"[LanguageManager] Language pack: {languagePack.Name}");
                                result.Add(languagePack.Name);
                            }
                            else TranslationMod.Logger?.LogInfo($"[LanguageManager] Language pack is invalid: {languagePack.Name}");
                        }
                        catch (Exception e)
                        {
                            TranslationMod.Logger?.LogWarning($"[LanguageManager] Failed to load language pack from '{directory}': {e.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguageManager] Error getting available languages: {e.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// 根据语言显示名查找语言代码。
        /// 流程：遍历语言包目录并读取配置，找到同名包后返回其代码。
        /// </summary>
        public static string GetLanguageCodeByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return ConfigKeys.EnglishLanguageCode;
                
            if (name.Equals(ConfigKeys.EnglishLanguageName, StringComparison.OrdinalIgnoreCase))
                return ConfigKeys.EnglishLanguageCode;
            
            try
            {
                string languagePacksPath = Path.Combine(_pluginDirectory, ConfigurationManager.PluginConfig.LanguagePacksPath.Value);
                
                if (!Directory.Exists(languagePacksPath))
                {
                    return ConfigKeys.EnglishLanguageCode;
                }

                var directories = Directory.GetDirectories(languagePacksPath);
                
                foreach (var directory in directories)
                {
                    string configFile = Path.Combine(directory, ConfigKeys.LanguagePackConfigFileName);
                    
                    if (File.Exists(configFile))
                    {
                        try
                        {
                            var languagePack = new LanguagePack(configFile, directory);
                            if (languagePack.IsValid() && 
                                string.Equals(languagePack.Name, name, StringComparison.OrdinalIgnoreCase))
                            {
                                return languagePack.LanguageCode;
                            }
                        }
                        catch (Exception e)
                        {
                            TranslationMod.Logger?.LogWarning($"[LanguageManager] Failed to check language pack from '{directory}': {e.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguageManager] Error getting language code by name: {e.Message}");
            }
            
            return ConfigKeys.EnglishLanguageCode;
        }

        /// <summary>
        /// 获取插件根目录。
        /// 供其他模块拼接字体、翻译文件等资源路径时使用。
        /// </summary>
        public static string GetPluginDirectory()
        {
            return _pluginDirectory;
        }

        /// <summary>
        /// 判断指定语言代码是否受支持。
        /// 流程：英文恒为支持，其他语言通过扫描语言包目录确认是否存在有效包。
        /// </summary>
        public static bool IsLanguageSupported(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                return false;
                
            if (languageCode.Equals(ConfigKeys.EnglishLanguageCode, StringComparison.OrdinalIgnoreCase))
                return true; // English is always supported
                
            try
            {
                string languagePacksPath = Path.Combine(_pluginDirectory, ConfigurationManager.PluginConfig.LanguagePacksPath.Value);
                
                if (!Directory.Exists(languagePacksPath))
                {
                    return false;
                }

                var directories = Directory.GetDirectories(languagePacksPath);
                
                foreach (var directory in directories)
                {
                    string configFile = Path.Combine(directory, ConfigKeys.LanguagePackConfigFileName);
                    
                    if (File.Exists(configFile))
                    {
                        try
                        {
                            var languagePack = new LanguagePack(configFile, directory);
                            if (languagePack.IsValid() && 
                                string.Equals(languagePack.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                        catch (Exception e)
                        {
                            TranslationMod.Logger?.LogWarning($"[LanguageManager] Failed to check language pack from '{directory}': {e.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguageManager] Error checking language support: {e.Message}");
            }
            
            return false;
        }
    }
} 
