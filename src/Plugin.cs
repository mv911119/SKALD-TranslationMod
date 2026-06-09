using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine.UI;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using TranslationMod.Configuration;
using TranslationMod.Patches;
using System;
using static GlobalSettings;

namespace TranslationMod
{
	[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
	public class TranslationMod : BaseUnityPlugin
	{
		internal static new ManualLogSource Logger;

		/// <summary>
		/// 插件入口。
		/// 流程：初始化配置系统，初始化语言管理器，应用 Harmony 补丁，
		/// 再同步一次当前游戏语言，确保后续翻译与字体替换按当前设置生效。
		/// </summary>
		private void Awake()
		{
			Logger = base.Logger;
			
			try
			{
				ConfigurationManager.Initialize();
				Logger.LogInfo("Configuration system initialized successfully.");

				LanguageManager.Initialize();
				Logger.LogInfo("Language manager initialized v1.1");
				
				HarmonyManager.ApplyPatches(new Harmony(MyPluginInfo.PLUGIN_GUID));
				Logger.LogInfo("Harmony patches applied successfully.");

				// 将语言管理器与当前游戏设置同步，避免启动后语言状态不一致
				LanguageManager.SynchronizeWithGame();
				Logger.LogInfo("Language manager synchronized with game settings.");

			}
			catch (System.Exception e)
			{
				Logger.LogError($"Failed to initialize plugin: {e}");
			}
			
			Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} is loaded!");
		}
	}
}

