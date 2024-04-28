using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.PSI.Environment;
using Extra.Lib;
using Extra.Lib.Debugger;
using Extra.Lib.Localization;
using Extra.Lib.UI;
using ExtraAssetsImporter.Importers;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Settings;
using System.Drawing;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Logger = Extra.Lib.Debugger.Logger;

namespace ExtraAssetsImporter
{
	public class EAI : IMod
	{
		private static ILog log = LogManager.GetLogger($"{nameof(ExtraAssetsImporter)}").SetShowsErrorsInUI(false);
		internal static Logger Logger { get; private set; } = new(log, true);
		static internal readonly string ELTGameDataPath = $"{EnvPath.kStreamingDataPath}\\Mods\\EAI";                                                                          
		internal static Setting m_Setting;

		internal static string ResourcesIcons { get; private set; }
		public void OnLoad(UpdateSystem updateSystem)
		{
			Logger.Info(nameof(OnLoad));
			ClearData();

			ExtraLocalization.LoadLocalization(Logger, Assembly.GetExecutingAssembly(), false);

			m_Setting = new Setting(this);
			m_Setting.RegisterInOptionsUI();

			AssetDatabase.global.LoadSettings("settings", m_Setting, new Setting(this));

			if (!GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset)) return;
			Logger.Info($"Current mod asset at {asset.path}");

			FileInfo fileInfo = new(asset.path);

			ResourcesIcons = Path.Combine(fileInfo.DirectoryName, "Icons");
			Icons.LoadIcons(fileInfo.DirectoryName);

			string pathToData = Path.Combine(EnvPath.kUserDataPath, "ModsData", nameof(ExtraAssetsImporter));
			string pathToDataCustomDecals = Path.Combine(pathToData, "CustomDecals");
			string pathToDataCustomSurfaces = Path.Combine(pathToData, "CustomSurfaces");

			if (Directory.Exists(pathToDataCustomDecals)) DecalsImporter.AddCustomDecalsFolder(pathToDataCustomDecals);
			if (Directory.Exists(pathToDataCustomSurfaces)) SurfacesImporter.AddCustomSurfacesFolder(pathToDataCustomSurfaces);

			ExtraLib.AddOnMainMenu(OnMainMenu);

			updateSystem.UpdateAt<sys>(SystemUpdatePhase.MainLoop);

		}

		public void OnDispose()
		{
			Logger.Info(nameof(OnDispose));
			ClearData();
		}

		private void OnMainMenu()
		{
			if (m_Setting.Decals) ExtraLib.extraLibMonoScript.StartCoroutine(DecalsImporter.CreateCustomDecals());
            if (m_Setting.Surfaces) ExtraLib.extraLibMonoScript.StartCoroutine(SurfacesImporter.CreateCustomSurfaces());
		}

		internal static void ClearData()
		{
			if (Directory.Exists(ELTGameDataPath))
			{
				Directory.Delete(ELTGameDataPath, true);
			}
			//if (Directory.Exists(ELTUserDataPath))
			//{
			//	Directory.Delete(ELTUserDataPath, true);
			//}
		}

		public static void LoadCustomAssets(string modPath)
		{
			if (Directory.Exists(modPath + "\\CustomSurfaces")) SurfacesImporter.AddCustomSurfacesFolder(modPath + "\\CustomSurfaces");
			if (Directory.Exists(modPath + "\\CustomDecals")) DecalsImporter.AddCustomDecalsFolder(modPath + "\\CustomDecals");
		}

		public static void UnLoadCustomAssets(string modPath)
		{
			if (Directory.Exists(modPath + "\\CustomSurfaces")) SurfacesImporter.RemoveCustomSurfacesFolder(modPath + "\\CustomSurfaces");
			if (Directory.Exists(modPath + "\\CustomDecals")) DecalsImporter.RemoveCustomDecalsFolder(modPath + "\\CustomDecals");
		}

	}
}
