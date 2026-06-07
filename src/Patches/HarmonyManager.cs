using HarmonyLib;
using System.Reflection;
using BepInEx.Logging;

namespace TranslationMod.Patches
{
    public static class HarmonyManager
    {
        /// <summary>
        /// 应用程序集内的全部 Harmony 补丁。
        /// 流程：先执行 `PatchAll`，再枚举已打补丁的方法并输出日志，
        /// 便于确认运行时实际生效的 Hook 列表。
        /// </summary>
        public static void ApplyPatches(Harmony harmony)
        {
            var logger = TranslationMod.Logger;
            
            if (logger == null)
            {
                throw new System.InvalidOperationException("TranslationMod.Logger is not initialized");
            }
            
            logger.LogInfo("Applying harmony patches.");

            try
            {
                // 应用当前程序集中的所有补丁
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                
                // 不依赖 LINQ，手动收集补丁结果以兼容当前运行环境
                var patchedMethodsEnumerable = harmony.GetPatchedMethods();
                var patchedMethods = new System.Collections.Generic.List<System.Reflection.MethodBase>();
                foreach (var method in patchedMethodsEnumerable)
                {
                    patchedMethods.Add(method);
                }
                
                logger.LogInfo($"Successfully patched {patchedMethods.Count} methods:");
                foreach (var method in patchedMethods)
                {
                    logger.LogInfo($"- {method.FullDescription()}");
                }
            }
            catch (System.Exception e)
            {
                logger.LogError($"Failed to apply Harmony patches: {e.Message}");
                throw;
            }
        }
    }
} 
