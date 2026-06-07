using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using TranslationMod.Configuration;

namespace TranslationMod.Patches
{
    /// <summary>
    /// 对 `BarkControl.Bark` 构造函数做前置拦截。
    /// 负责在气泡文本创建前翻译消息，并按需记录日志避免重复输出。
    /// </summary>
    [HarmonyPatch]
    public static class BarkPatch
    {
        /// <summary>
        /// 已记录消息集合，用于去重日志。
        /// </summary>
        private static readonly HashSet<string> _loggedMessages = new HashSet<string>();
        
        /// <summary>
        /// `_loggedMessages` 的同步锁对象。
        /// </summary>
        private static readonly object _lockObject = new object();
        
        /// <summary>
        /// 延迟初始化翻译服务，避免补丁加载时过早创建依赖。
        /// </summary>
        private static readonly Lazy<TranslationService> _translator =
            new(() => new TranslationService());
        /// <summary>
        /// 解析补丁目标构造函数。
        /// 流程：先找到 `BarkControl` 的内部 `Bark` 类型，再匹配指定参数签名的构造函数。
        /// </summary>
        [HarmonyTargetMethod]
        static System.Reflection.MethodBase TargetMethod()
        {
            // 获取 BarkControl 类型
            var barkControlType = AccessTools.TypeByName("BarkControl");
            if (barkControlType == null)
            {
                TranslationMod.Logger?.LogError("[BarkPatch] Cannot find BarkControl type");
                return null;
            }

            // 查找内部 Bark 类型
            var barkType = barkControlType.GetNestedType("Bark", System.Reflection.BindingFlags.NonPublic);
            if (barkType == null)
            {
                TranslationMod.Logger?.LogError("[BarkPatch] Cannot find nested Bark type in BarkControl");
                return null;
            }

            // 获取目标构造函数签名
            var constructor = AccessTools.Constructor(barkType, new Type[] {
                typeof(string),  // message
                typeof(int),     // x
                typeof(int),     // y
                typeof(Color),   // textColor
                typeof(Color),   // shadowColor
                typeof(int)      // delay
            });

            if (constructor == null)
            {
                TranslationMod.Logger?.LogError("[BarkPatch] Cannot find Bark constructor with expected signature");
                return null;
            }

#if DEBUG
            TranslationMod.Logger?.LogInfo("[BarkPatch] Successfully found Bark constructor for patching");
#endif
            return constructor;
        }

        /// <summary>
        /// 构造函数前置逻辑。
        /// 流程：保留原消息用于日志，必要时翻译文本，再决定是否记录一次去重日志，
        /// 最后继续执行原始构造函数。
        /// </summary>
        [HarmonyPrefix]
        static bool Prefix(ref string __0, int __1, int __2, Color __3, Color __4, int __5)
        {
            try
            {
                // 保存原始消息，便于日志对比
                string originalMessage = __0;
                
                // 非英文语言下先翻译消息内容
                var currentLanguagePack = LanguageManager.GetCurrentLanguagePack();
                if (currentLanguagePack != null && !currentLanguagePack.Name.Equals("English", StringComparison.OrdinalIgnoreCase))
                {
                    // 直接替换构造函数入参，使原逻辑消费翻译后的文本
                    string translatedMessage = _translator.Value.Process(__0);
                    __0 = translatedMessage; // Change parameter to pass translated text to constructor
                }
                
                // 需要时才记录一次日志，避免刷屏
                if (ShouldLogMessage(originalMessage))
                {
#if DEBUG
                    if (originalMessage != __0)
                    {
                        // 记录翻译前后对照
                        TranslationMod.Logger?.LogInfo($"[BarkPatch] Bark constructor called with message: '{originalMessage}' -> '{__0}' at position ({__1}, {__2}) with delay: {__5}");
                    }
                    else
                    {
                        // 记录未翻译原文
                        TranslationMod.Logger?.LogInfo($"[BarkPatch] Bark constructor called with message: '{originalMessage}' at position ({__1}, {__2}) with delay: {__5}");
                    }
#endif
                }
                
                // 继续执行原始构造函数
                return true;
            }
            catch (Exception ex)
            {
                TranslationMod.Logger?.LogError($"[BarkPatch] Error in Bark constructor prefix: {ex.Message}");
                // 出错时也放行原构造函数，避免影响游戏显示
                return true;
            }
        }

        /// <summary>
        /// 判断消息是否需要写入日志。
        /// 流程：过滤空值后在锁内执行去重检查，首次出现则加入集合并返回真。
        /// </summary>
        private static bool ShouldLogMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            lock (_lockObject)
            {
                // 已记录过的消息不再重复输出
                if (_loggedMessages.Contains(message))
                {
                    return false;
                }

                // 首次出现时加入集合
                _loggedMessages.Add(message);
                return true;
            }
        }
    }
} 
