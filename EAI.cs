using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Colossal.Logging;
using Colossal.PSI.Environment;
using ExtraAssetsImporter.AssetImporter;
using ExtraAssetsImporter.AssetImporter.Importers;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.Importers;
using ExtraAssetsImporter.MOD.AssetImporter.Importers;
using ExtraLib;
using ExtraLib.Debugger;
using ExtraLib.Helpers;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ExtraAssetsImporter
{
	public class EAI : IMod
	{
		private static readonly ILog log = LogManager.GetLogger($"{nameof(ExtraAssetsImporter)}").SetShowsErrorsInUI(false);
#if DEBUG
		internal static Logger Logger = new(log, true);
#else
        internal static Logger Logger = new(log, false);
#endif

        internal static Setting m_Setting;
		internal static string ResourcesIcons { get; private set; }

		internal static string pathModsData = Path.Combine(EnvPath.kUserDataPath, "ModsData", nameof(ExtraAssetsImporter));
        internal static string pathTempFolder => Path.Combine(AssetDataBaseEAI.kRootPath, "TempAssetsFolder");

        internal static TextureStreamingSystem textureStreamingSystem;

        //private static HashSet<string> modPathsLoaded = new HashSet<string>();

        public void OnLoad(UpdateSystem updateSystem)
		{
			try
			{
                Logger.Info(nameof(OnLoad));

                string oldModsPath = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Mods");
                string oldDataPath = Path.Combine(oldModsPath, "EAI");

                if (Directory.Exists(oldDataPath))
                {
                    Directory.Delete(oldDataPath, true);
                }

                if (Directory.Exists(oldModsPath) && Directory.GetDirectories(oldModsPath).Length == 0 && Directory.GetFiles(oldModsPath).Length == 0)
                {
                    Directory.Delete(oldModsPath, false);
                }


                var oldLocation = Path.Combine(EnvPath.kUserDataPath, "ModsSettings", nameof(ExtraAssetsImporter), "settings.coc");

                if (File.Exists(oldLocation))
                {
                    var correctLocation = Path.Combine(EnvPath.kUserDataPath, $"{nameof(ExtraAssetsImporter)}.coc");

                    if (File.Exists(correctLocation))
                    {
                        File.Delete(oldLocation);
                    }
                    else
                    {
                        //Directory.CreateDirectory(Path.GetDirectoryName(correctLocation));
                        File.Move(oldLocation, correctLocation);
                    }

                    string parent = Path.GetDirectoryName(oldLocation);

                    Directory.Delete(parent, true);
                    //if (Directory.GetDirectories(Path.GetDirectoryName(parent)).Length == 0 && Directory.GetFiles(Path.GetDirectoryName(parent)).Length == 0)
                    //{
                    //    Directory.Delete(parent);
                    //}

                }

                if (!GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset)) return;
                Logger.Info($"Current mod asset at {asset.path}");

                ExtraLocalization.LoadLocalization(Logger, Assembly.GetExecutingAssembly(), false);

                m_Setting = new Setting(this);
                m_Setting.RegisterInOptionsUI();
                AssetDatabase.global.LoadSettings(nameof(ExtraAssetsImporter), m_Setting, new Setting(this));

                // MOVE DATABASE OUT OF THE GAME FOLDER
                oldModsPath = Path.Combine(EnvPath.kContentPath, "Mods");
                oldDataPath = Path.Combine(oldModsPath, "EAI");

                if (Directory.Exists(oldDataPath))
                {
                    try
                    {
                        Directory.Move(oldDataPath, m_Setting.DatabasePath ?? new EAIDataBase().ActualDataBasePath);
                    }
                    catch
                    {
                        Directory.Delete(oldDataPath, true);
                    }
                }

                if (Directory.Exists(oldModsPath) && Directory.GetDirectories(oldModsPath).Length == 0 && Directory.GetFiles(oldModsPath).Length == 0)
                {
                    Directory.Delete(oldModsPath, false);
                }



                FileInfo fileInfo = new(asset.path);

                ResourcesIcons = Path.Combine(fileInfo.DirectoryName, "Icons");
                Icons.LoadIcons(fileInfo.DirectoryName);

                string pathToDataCustomDecals = Path.Combine(pathModsData, "CustomDecals");
                string pathToDataCustomSurfaces = Path.Combine(pathModsData, "CustomSurfaces");
                string pathToDataCustomNetLanes = Path.Combine(pathModsData, "CustomNetLanes");

                AssetsImporterManager.AddImporter<LocalizationImporter>();
                AssetsImporterManager.AddImporter<AssetPackImporter>();

                AssetsImporterManager.AddImporter<DecalsImporterNew>();
                AssetsImporterManager.AddImporter<NetLanesDecalImporterNew>();
                AssetsImporterManager.AddImporter<SurfacesImporterNew>();

                if (m_Setting.UseNewImporters) AssetsImporterManager.AddAssetFolder(pathModsData);

                Directory.CreateDirectory(pathToDataCustomDecals);
                Directory.CreateDirectory(pathToDataCustomSurfaces);
                Directory.CreateDirectory(pathToDataCustomNetLanes);

                if(m_Setting.UseOldImporters)
                {
                    if (Directory.Exists(pathToDataCustomDecals) && m_Setting.Decals) DecalsImporter.AddCustomDecalsFolder(pathToDataCustomDecals);
                    if (Directory.Exists(pathToDataCustomSurfaces) && m_Setting.Surfaces) SurfacesImporter.AddCustomSurfacesFolder(pathToDataCustomSurfaces);
                    if (Directory.Exists(pathToDataCustomNetLanes) && m_Setting.NetLanes) NetLanesDecalImporter.AddCustomNetLanesFolder(pathToDataCustomNetLanes);
                }

                textureStreamingSystem = updateSystem.World.GetOrCreateSystemManaged<TextureStreamingSystem>(); // to use VT, should not be used normally.
                
                EAIDataBaseManager.LoadDataBase();
                EL.AddOnInitialize(Initialize);

                updateSystem.UpdateAt<sys>(SystemUpdatePhase.MainLoop);

            } catch (Exception ex)
            {
                EAI.Logger.Error(ex); // Doing this, because the game isn't logging any error.
                throw ex; // This should still send the error to the game and so start the OnDispose.
            }

            EAI.Logger.Info("EAI loaded successfully.");
        }

		public void OnDispose()
		{
			Logger.Info(nameof(OnDispose));
			EAIDataBaseManager.CheckIfDataBaseNeedToBeRelocated(false);
            ClearData();
		}

		internal static void Initialize()
		{
            EAI.Logger.Info("Start loading custom stuff.");

            // Load the custom assets with the new importers
            if (m_Setting.UseNewImporters)
            {
                // Auto load custom assets into new importer if they have the correct folder names
                AutoImportCustomAssets();

                AssetsImporterManager.LoadCustomAssets();
            }

            if(m_Setting.UseOldImporters)
            {
                if (m_Setting.Decals) EL.extraLibMonoScript.StartCoroutine(DecalsImporter.CreateCustomDecals());
                if (m_Setting.Surfaces) EL.extraLibMonoScript.StartCoroutine(SurfacesImporter.CreateCustomSurfaces());
                if (m_Setting.NetLanes) EL.extraLibMonoScript.StartCoroutine(NetLanesDecalImporter.CreateCustomNetLanes());
            }

            EL.extraLibMonoScript.StartCoroutine(AssetsImporterManager.WaitForImportersToFinish());

        }


        internal static void ClearData()
		{
			if (Directory.Exists(pathTempFolder))
			{
				Directory.Delete(pathTempFolder, true);
			}
            if ( EAI.m_Setting.DeleteDataBase ) EAIDataBaseManager.DeleteDatabase();
        }

        internal static void AutoImportCustomAssets()
        {
            string[] modsPaths =
            {
                Path.Combine(AssetDatabase.user.rootPath, "Mods"), // Local mods development path
                Path.Combine(AssetDatabase.user.rootPath, ".cache", "Mods", "mods_subscribed") // PDX mods subscribed path
            };

            var folders = modsPaths
                .Where(modsPath => Directory.Exists(modsPath))
                .SelectMany(modsPath => Directory.EnumerateDirectories(modsPath));

            foreach (string folder in folders)
            {
                EAI.Logger.Info($"Loading asset at : {folder}");
                AssetsImporterManager.AddAssetFolder(folder);
            }
        }

        public static void LoadCustomAssets(string modPath)
		{
            AssetsImporterManager.AddAssetFolder(modPath);

            // Also load old importer paths
            if (Directory.Exists(Path.Combine(modPath, "CustomSurfaces"))) SurfacesImporter.AddCustomSurfacesFolder(Path.Combine(modPath, "CustomSurfaces"));
            if (Directory.Exists(Path.Combine(modPath, "CustomDecals"))) DecalsImporter.AddCustomDecalsFolder(Path.Combine(modPath, "CustomDecals"));
            if (Directory.Exists(Path.Combine(modPath, "CustomNetLanes"))) NetLanesDecalImporter.AddCustomNetLanesFolder(Path.Combine(modPath, "CustomNetLanes"));

            // Patch 1.3.3f1 fix, mods OnLoad is called before EAI is OnLoad method is called.
            //if (m_Setting.UseNewImporters) AssetsImporterManager.AddAssetFolder(modPath);

            //if (m_Setting.UseOldImporters)
            //{
            //    if (m_Setting.Surfaces  && Directory.Exists(Path.Combine(modPath, "CustomSurfaces"))) SurfacesImporter.AddCustomSurfacesFolder(Path.Combine(modPath, "CustomSurfaces"));
            //    if (m_Setting.Decals    && Directory.Exists(Path.Combine(modPath, "CustomDecals"))) DecalsImporter.AddCustomDecalsFolder(Path.Combine(modPath, "CustomDecals"));
            //    if (m_Setting.NetLanes  && Directory.Exists(Path.Combine(modPath, "CustomNetLanes"))) NetLanesDecalImporter.AddCustomNetLanesFolder(Path.Combine(modPath, "CustomNetLanes"));
            //}
        }

        [Obsolete("Not needed anymore, you can remove this")]
		public static void UnLoadCustomAssets(string modPath)
		{
			//if (Directory.Exists(Path.Combine(modPath, "CustomSurfaces")))	SurfacesImporter.RemoveCustomSurfacesFolder(Path.Combine(modPath, "CustomSurfaces"));
			//if (Directory.Exists(Path.Combine(modPath, "CustomDecals")))	DecalsImporter.RemoveCustomDecalsFolder(Path.Combine(modPath, "CustomDecals"));
            //if (Directory.Exists(Path.Combine(modPath, "CustomNetLanes"))) Importers.NetLanesDecalImporter.RemoveCustomNetLanesFolder(Path.Combine(modPath, "CustomNetLanes"));
        }

	}
}
