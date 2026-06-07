using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace TranslationMod.Configuration
{
    /// <summary>
    /// 语言包对象。
    /// 负责读取 JSON 配置，暴露字体与翻译目录，并提供字符映射等资源访问能力。
    /// </summary>
    public class LanguagePack
    {
        private readonly string _configFilePath;
        private readonly string _packPath;
        private LanguagePackData _data;

        // 语言基础信息
        public string LanguageCode => _data?.LanguageCode ?? "";
        public string Name => _data?.Name ?? "";
        public string Description => _data?.Description ?? "";
        public string Version => _data?.Version ?? "1.0.0";

        // 资源路径配置
        public string FontFilesPath => _data?.FontFilesPath ?? ConfigKeys.DefaultFontsDir;
        public string TranslationFilesPath => _data?.TranslationFilesPath ?? ConfigKeys.DefaultTranslationsDir;
        
        // 语言包目录路径
        public string DirectoryPath => _packPath;

        /// <summary>
        /// 构造语言包实例。
        /// 流程：记录配置路径与语言包根目录，并立即读取 JSON 配置内容。
        /// </summary>
        public LanguagePack(string configFilePath, string packPath)
        {
            _configFilePath = configFilePath;
            _packPath = packPath;
            LoadFromFile();
        }

        /// <summary>
        /// 从 JSON 文件加载语言包配置。
        /// 流程：若文件存在则反序列化，否则创建默认配置并记录警告。
        /// </summary>
        private void LoadFromFile()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string jsonContent = File.ReadAllText(_configFilePath);
                    _data = JsonConvert.DeserializeObject<LanguagePackData>(jsonContent) ?? new LanguagePackData();
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[LanguagePack] Loaded language pack from JSON: {Name}");
#endif
                }
                else
                {
                    _data = new LanguagePackData();
                    TranslationMod.Logger?.LogWarning($"[LanguagePack] Config file not found, using defaults: {_configFilePath}");
                }
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguagePack] Error loading JSON config: {e.Message}");
                _data = new LanguagePackData();
            }
        }

        /// <summary>
        /// Data structure for language pack JSON configuration
        /// </summary>
        [JsonObject]
        public class LanguagePackData
        {
            [JsonProperty("languageCode")]
            public string LanguageCode { get; set; } = "";

            [JsonProperty("name")]
            public string Name { get; set; } = "";

            [JsonProperty("description")]
            public string Description { get; set; } = "";

            [JsonProperty("version")]
            public string Version { get; set; } = "1.0.0";

            [JsonProperty("fontFilesPath")]
            public string FontFilesPath { get; set; } = ConfigKeys.DefaultFontsDir;

            [JsonProperty("translationFilesPath")]
            public string TranslationFilesPath { get; set; } = ConfigKeys.DefaultTranslationsDir;

            [JsonProperty("characterChart")]
            public Dictionary<string, int> CharacterChart { get; set; } = new Dictionary<string, int>();
        }

        /// <summary>
        /// 读取字符映射表。
        /// 流程：将 JSON 中的字符串键转换为单字符键值对，过滤非法项后返回结果。
        /// </summary>
        public Dictionary<char, int> GetCharacterChart()
        {
            var result = new Dictionary<char, int>();

            try
            {
                if (_data?.CharacterChart != null)
                {
                    foreach (var kvp in _data.CharacterChart)
                    {
                        // 键必须是单个字符
                        if (kvp.Key.Length == 1)
                        {
                            result[kvp.Key[0]] = kvp.Value;
#if DEBUG
                    TranslationMod.Logger?.LogDebug($"[LanguagePack] Mapped character '{kvp.Key[0]}' to position {kvp.Value}");
#endif
                        }
                        else
                        {
                            TranslationMod.Logger?.LogWarning($"[LanguagePack] Skipped invalid character key: '{kvp.Key}' (length: {kvp.Key.Length}, expected: 1 character)");
                        }
                    }
                }

                if (result.Count == 0)
                {
                    TranslationMod.Logger?.LogWarning($"[LanguagePack] CharacterChart is empty for language '{Name}'");
                }
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguagePack] Error parsing character chart for '{Name}': {e.Message}");
            }

#if DEBUG
                TranslationMod.Logger?.LogInfo($"[LanguagePack] Loaded {result.Count} characters from CharacterChart for language '{Name}'");
#endif
            return result;
        }

        /// <summary>
        /// 获取字体目录绝对路径。
        /// </summary>
        public string GetFontsPath()
        {
            return Path.Combine(_packPath, FontFilesPath);
        }

        /// <summary>
        /// 获取翻译文件目录绝对路径。
        /// </summary>
        public string GetTranslationsPath()
        {
            return Path.Combine(_packPath, TranslationFilesPath);
        }

        /// <summary>
        /// 校验语言包是否有效。
        /// 流程：检查必要字段、语言代码格式，以及字体和翻译目录是否可用；
        /// 缺失目录时会自动补建。
        /// </summary>
        public bool IsValid()
        {
            try
            {
                // 检查必要字段
                if (string.IsNullOrEmpty(LanguageCode) || 
                    string.IsNullOrEmpty(Name))
                {
                    return false;
                }

                // 检查语言代码格式是否为 2 到 3 个字符
                if (LanguageCode.Length < 2 || LanguageCode.Length > 3)
                {
                    return false;
                }

                // 检查资源目录是否存在
                if (!Directory.Exists(GetFontsPath()) || !Directory.Exists(GetTranslationsPath()))
                {
                    TranslationMod.Logger?.LogWarning($"[LanguagePack] Missing directories for language pack: {Name}");
                    // 自动创建缺失目录
                    Directory.CreateDirectory(GetFontsPath());
                    Directory.CreateDirectory(GetTranslationsPath());
                }

                return true;
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguagePack] Error validating language pack: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存语言包配置到 JSON 文件。
        /// 流程：将当前数据序列化后写回原配置路径。
        /// </summary>
        public void Save()
        {
            try
            {
                string jsonContent = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(_configFilePath, jsonContent);
#if DEBUG
            TranslationMod.Logger?.LogInfo($"[LanguagePack] Saved language pack: {Name}");
#endif
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[LanguagePack] Error saving language pack: {e.Message}");
            }
        }
    }
} 
