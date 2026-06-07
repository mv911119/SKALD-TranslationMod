using BepInEx.Configuration;

namespace TranslationMod.Configuration
{
    public class PluginConfig
    {
        private readonly ConfigFile _configFile;
        
        // 语言包目录配置项
        public ConfigEntry<string> LanguagePacksPath { get; private set; }

        /// <summary>
        /// 构造插件配置对象。
        /// 流程：持有传入的配置文件实例，并注册语言包目录配置项供后续读取。
        /// </summary>
        public PluginConfig(ConfigFile configFile)
        {
            _configFile = configFile;

            // 绑定语言包目录配置，路径相对插件目录解析
            LanguagePacksPath = _configFile.Bind(
                ConfigKeys.GeneralSection, 
                "LanguagePacksPath", 
                "LanguagePacks", 
                "Path to language packs directory (relative to plugin folder)"
            );
        }

        /// <summary>
        /// 检查配置对象是否有效。
        /// 当前仅验证语言包路径配置项是否成功创建。
        /// </summary>
        public bool IsValid() => LanguagePacksPath != null;

        /// <summary>
        /// 保存当前配置。
        /// 直接透传给底层配置文件对象执行写盘。
        /// </summary>
        public void Save()
        {
            _configFile?.Save();
        }
    }
} 
