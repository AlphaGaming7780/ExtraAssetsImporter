using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.PSI.Environment;
using Extra.Lib;
using Extra.Lib.Debugger;
using Extra.Lib.Localization;
using ExtraAssetsImporter.Importers;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System.Collections;
using System.IO;
using System.Reflection;

namespace ExtraAssetsImporter
{
	public class EAI : IMod
	{
		private static ILog log = LogManager.GetLogger($"{nameof(ExtraAssetsImporter)}").SetShowsErrorsInUI(false);
#if DEBUG
		internal static Logger Logger = new(log, true);
#else
        internal static Logger Logger = new(log, false);
#endif
        static internal readonly string EAIGameDataPath = $"{EnvPath.kStreamingDataPath}\\Mods\\EAI";  

        internal static Setting m_Setting;
		internal static string ResourcesIcons { get; private set; }

		internal static string pathModsData;
		internal static string pathTempFolder = $"{EnvPath.kStreamingDataPath}\\Mods\\EAI\\TempAssetsFolder";

        public void OnLoad(UpdateSystem updateSystem)
		{
			Logger.Info(nameof(OnLoad));

			if (!GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset)) return;
			Logger.Info($"Current mod asset at {asset.path}");

            var oldLocation = Path.Combine(EnvPath.kUserDataPath, "ModSettings", nameof(ExtraAssetsImporter), "settings.coc");

            if (File.Exists(oldLocation))
            {
                var correctLocation = Path.Combine(EnvPath.kUserDataPath, "ModsSettings", nameof(ExtraAssetsImporter), "settings.coc");

				if (File.Exists(correctLocation))
				{
					File.Delete(oldLocation);
				}
				else
				{
                    Directory.CreateDirectory(Path.GetDirectoryName(correctLocation));
                    File.Move(oldLocation, correctLocation);
				}
            }

            ExtraLocalization.LoadLocalization(Logger, Assembly.GetExecutingAssembly(), false);

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            AssetDatabase.global.LoadSettings(nameof(ExtraAssetsImporter), m_Setting, new Setting(this));

            ClearData();

            FileInfo fileInfo = new(asset.path);

			ResourcesIcons = Path.Combine(fileInfo.DirectoryName, "Icons");
			Icons.LoadIcons(fileInfo.DirectoryName);

			pathModsData = Path.Combine(EnvPath.kUserDataPath, "ModsData", nameof(ExtraAssetsImporter));
			string pathToDataCustomDecals = Path.Combine(pathModsData, "CustomDecals");
            string pathToDataCustomSurfaces = Path.Combine(pathModsData, "CustomSurfaces");

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
            EAIDataBaseManager.LoadDataBase();
            if (m_Setting.Decals) ExtraLib.extraLibMonoScript.StartCoroutine(DecalsImporter.CreateCustomDecals());
            if (m_Setting.Surfaces) ExtraLib.extraLibMonoScript.StartCoroutine(SurfacesImporter.CreateCustomSurfaces());
			ExtraLib.extraLibMonoScript.StartCoroutine(WaitForCustomStuffToFinish());
		}

		private IEnumerator WaitForCustomStuffToFinish()
		{
			while( (m_Setting.Decals && !DecalsImporter.DecalsLoaded) || ( m_Setting.Surfaces && !SurfacesImporter.SurfacesIsLoaded)) 
			{
				yield return null;
			}
			m_Setting.ResetCompatibility();
			EAIDataBaseManager.SaveValidateDataBase();
			EAIDataBaseManager.ClearNotLoadedAssetsFromFiles();
			yield break;
		}


        internal static void ClearData()
		{
			//EAI.Logger.Info(pathTempFolder);
			if (Directory.Exists(pathTempFolder))
			{
				Directory.Delete(pathTempFolder, true);
			}
            if ( EAI.m_Setting.DeleteDataBase ) EAIDataBaseManager.DeleteDatabase();
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
