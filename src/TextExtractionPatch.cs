using HarmonyLib;
using UnityEngine;

/// <summary>
/// 文本提取补丁。
/// 用于在合适的游戏初始化节点触发一次全量文本导出，避免重复提取。
/// </summary>
[HarmonyPatch]
public static class TextExtractionPatch
{
    private static bool textExtracted = false;

    /// <summary>
    /// 在 `GameData` 加载完成后尝试导出全部文本。
    /// 流程：先检查是否已提取，未提取时调用提取器并记录结果。
    /// </summary>
    //[HarmonyPatch(typeof(GameData), "loadData", new System.Type[] { typeof(string) })]
    //[HarmonyPostfix]
    public static void ExtractAllTextOnGameDataLoad()
    {
        if (!textExtracted)
        {
            try
            {
                Debug.Log("Starting automatic text extraction after GameData.loadData()...");
                TextDataExtractor.ExtractAllTextToPluginDirectory();
                textExtracted = true;
                Debug.Log("✓ All game text automatically extracted to plugin/text/ directory!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to extract text automatically: {ex.Message}");
                Debug.LogError($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// 在 `DataControl` 初始化阶段执行兜底提取。
    /// 流程与主提取入口一致，仅作为前一个时机未触发时的补偿方案。
    /// </summary>
    //[HarmonyPatch(typeof(DataControl), "initialize")]
    //[HarmonyPostfix]
    public static void ExtractAllTextOnDataControlInit()
    {
        if (!textExtracted)
        {
            try
            {
                Debug.Log("Starting automatic text extraction (DataControl fallback)...");
                TextDataExtractor.ExtractAllTextToPluginDirectory();
                textExtracted = true;
                Debug.Log("✓ All game text automatically extracted to plugin/text/ directory!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to extract text automatically (fallback): {ex.Message}");
            }
        }
    }
} 
