using HarmonyLib;
using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine;
using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    [HarmonyPatch]
    public static class FontAssetPatch
    {
        private static bool _isSubscribed = false;
        // 标记是否已经完成字体预加载
        private static bool _fontsPreloaded = false;

        /// <summary>
        /// 初始化字体资源补丁。
        /// 流程：订阅语言切换事件，并立即预加载当前语言所需字体贴图。
        /// </summary>
        public static void Initialize()
        {
            if (_isSubscribed) return;
            
            LanguageManager.OnLanguageChanged += OnLanguageChangedHandler;
            _isSubscribed = true;
#if DEBUG
        TranslationMod.Logger?.LogInfo("[FontAssetPatch] Initialized and subscribed to language change events");
#endif            
            PreloadFonts();
        }

        /// <summary>
        /// 响应语言切换事件。
        /// 流程：清空字体替换状态并重新预加载新语言对应字体。
        /// </summary>
        private static void OnLanguageChangedHandler()
        {
            ClearReplacedFonts();
#if DEBUG
        TranslationMod.Logger?.LogInfo("[FontAssetPatch] Language changed, cleared replaced fonts list");
#endif
        }
        
        /// <summary>
        /// 清除当前字体预加载状态并重新加载。
        /// 用于语言切换后强制刷新字体缓存。
        /// </summary>
        public static void ClearReplacedFonts()
        {
            _fontsPreloaded = false;
            PreloadFonts();
#if DEBUG
        TranslationMod.Logger?.LogInfo("[FontAssetPatch] Cleared fonts cache and reloaded fonts");
#endif
        }
        
        /// <summary>
        /// 读取 PNG 文件并构造成游戏可用的 `TextureData`。
        /// 流程：加载字节流，创建临时纹理，按游戏原逻辑生成 `TextureData`，最后释放临时资源。
        /// </summary>
        private static TextureTools.TextureData LoadPngAsTextureData(string filePath)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                
                // 按游戏原生方式创建纹理对象
                Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Point; // 像素风资源关闭平滑
                texture.wrapMode = TextureWrapMode.Clamp;
                
                if (texture.LoadImage(fileData))
                {
                    // 应用贴图数据
                    texture.Apply(false, false);
                    
                    // 使用游戏原有构造方式生成 TextureData
                    var textureData = new TextureTools.TextureData(texture);
                    
                    // 释放临时纹理
                    UnityEngine.Object.Destroy(texture);
                    return textureData;
                }
                else
                {
                    TranslationMod.Logger?.LogError($"[FontAssetPatch] Failed to load image data from '{filePath}'. File might be corrupted or not a valid PNG.");
                    UnityEngine.Object.Destroy(texture); // 释放资源
                }
            }
            catch (IOException e)
            {
                TranslationMod.Logger?.LogError($"[FontAssetPatch] IO Error loading texture '{filePath}': {e.Message}");
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[FontAssetPatch] Unexpected error loading texture '{filePath}': {e.Message}");
            }
            return null;
        }

        /// <summary>
        /// 通过反射将自定义字体贴图注入游戏纹理缓冲区。
        /// 流程：找到全局图片缓存与 `addTexture` 方法，再以指定键名写入。
        /// </summary>
        private static bool AddTextureToGameBuffer(string path, TextureTools.TextureData textureData)
        {
            try
            {
                // 通过反射获取全局贴图缓冲区
                var bufferField = AccessTools.Field(typeof(TextureTools), "fullImageBuffer");
                if (bufferField == null)
                {
                    TranslationMod.Logger?.LogError("[FontAssetPatch] Could not find fullImageBuffer field in TextureTools");
                    return false;
                }

                var bufferInstance = bufferField.GetValue(null);
                if (bufferInstance == null)
                {
                    TranslationMod.Logger?.LogError("[FontAssetPatch] fullImageBuffer instance is null");
                    return false;
                }

                // 通过反射定位 addTexture 方法
                var addTextureMethod = AccessTools.Method(bufferInstance.GetType(), "addTexture");
                if (addTextureMethod == null)
                {
                    TranslationMod.Logger?.LogError($"[FontAssetPatch] Could not find addTexture method in {bufferInstance.GetType().Name}");
                    return false;
                }

                // 用自定义路径与贴图数据写入缓冲区
                addTextureMethod.Invoke(bufferInstance, new object[] { path, textureData });
                
#if DEBUG
            TranslationMod.Logger?.LogDebug($"[FontAssetPatch] Successfully added custom texture to game buffer for path: {path}");
#endif
                return true;
            }
            catch (Exception e)
            {
                TranslationMod.Logger?.LogError($"[FontAssetPatch] Error adding texture to game buffer: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 预加载目标字体到游戏缓冲区。
        /// 流程：先清理旧缓冲，再遍历语言包字体目录，读取目标 PNG，
        /// 按游戏期望的资源键名注入缓存。
        /// </summary>
        private static void PreloadFonts()
        {
            if (_fontsPreloaded) return;

            // 注入前先清空内部缓冲，确保后续子图从新贴图生成
            TextureTools.clearBuffer();
            try
            {
                var languagePack = LanguageManager.GetCurrentLanguagePack();
                if (languagePack == null)
                {
                    TranslationMod.Logger?.LogWarning("[FontAssetPatch] Language pack is null, cannot preload fonts");
                    return;
                }

                string fontsPath = languagePack.GetFontsPath();
                if (string.IsNullOrEmpty(fontsPath) || !System.IO.Directory.Exists(fontsPath))
                {
                    TranslationMod.Logger?.LogWarning($"[FontAssetPatch] Fonts directory not found: {fontsPath}");
                    return;
                }

                foreach (var fontFile in FontConstants.TargetFontFiles)
                {
                    string customFontPathPng = System.IO.Path.Combine(fontsPath, fontFile + ".png");

                    if (!System.IO.File.Exists(customFontPathPng))
                    {
                        continue; // 语言包不一定包含所有目标字体
                    }

                    var textureData = LoadPngAsTextureData(customFontPathPng);
                    if (textureData == null)
                    {
                        continue; // 当前文件加载失败则跳过
                    }

                    // 缓冲区键名需要与游戏请求的资源路径保持一致
                    string bufferKey = $"Images/CustomFonts/{fontFile}";
                    if (fontFile == "Logo")
                    {
                        bufferKey = $"Images/Backgrounds/Logo";
                    }

                    if (AddTextureToGameBuffer(bufferKey, textureData))
                    {
                        TranslationMod.Logger?.LogInfo($"[FontAssetPatch] Preloaded and injected font '{fontFile}'");
                    }
                }

                _fontsPreloaded = true;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[FontAssetPatch] Error while preloading fonts: {ex.Message}");
            }
        }
    }
} 
