using BepInEx.Configuration;
using System;
using System.IO;
using System.Reflection;

namespace TranslationMod.Configuration
{
    public static class ConfigurationManager
    {
        private static bool _isInitialized;
        private static string _pluginDirectory;
        private static ConfigFile _configFile;
        public static PluginConfig PluginConfig { get; private set; }



        /// <summary>
        /// 初始化配置管理器。
        /// 流程：定位插件目录，加载主配置文件，构造插件配置对象，
        /// 最后确保语言包目录存在，便于后续按需读取语言资源。
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                _pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(_pluginDirectory, ConfigKeys.MainConfigFileName);

                _configFile = new ConfigFile(configPath, true);
                PluginConfig = new PluginConfig(_configFile);

                EnsureLanguagePacksDirectoryExists();
                
                _isInitialized = true;
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[ConfigurationManager] Initialized. Language packs will be loaded on demand.");
#endif
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[ConfigurationManager] Failed to initialize: {e.Message}");
                // DO NOT set _isInitialized = true on error, so initialization can be retried
                throw; // re-throw exception for proper handling in calling code
            }
        }
        
        /// <summary>
        /// 确保语言包目录存在。
        /// 流程：根据插件目录和配置项拼接目标路径，不存在时自动创建。
        /// </summary>
        private static void EnsureLanguagePacksDirectoryExists()
        {
            string languagePacksPath = Path.Combine(_pluginDirectory, PluginConfig.LanguagePacksPath.Value);
            
            if (!Directory.Exists(languagePacksPath))
            {
                Directory.CreateDirectory(languagePacksPath);
#if DEBUG
                TranslationMod.Logger?.LogInfo($"[ConfigurationManager] Created language packs directory: {languagePacksPath}");
#endif
            }
        }
        


        /// <summary>
        /// 保存当前配置文件。
        /// 流程：仅在初始化成功后调用底层 BepInEx 配置对象执行落盘。
        /// </summary>
        public static void Save()
        {
            if (!_isInitialized) return;
            _configFile.Save();
        }




    }
}
