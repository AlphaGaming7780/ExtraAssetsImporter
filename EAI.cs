using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.PSI.Common;
using Colossal.PSI.Environment;
using Colossal.PSI.PdxSdk;
using Extra.Lib;
using Extra.Lib.Debugger;
using Extra.Lib.Localization;
using ExtraAssetsImporter.DataBase;
using ExtraAssetsImporter.Importers;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System;
using System.Collections;
using System.IO;
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

		//private bool eaiIsLoaded = false;

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

                if (Directory.Exists(pathToDataCustomDecals)) DecalsImporter.AddCustomDecalsFolder(pathToDataCustomDecals);
                if (Directory.Exists(pathToDataCustomSurfaces)) SurfacesImporter.AddCustomSurfacesFolder(pathToDataCustomSurfaces);
                if (Directory.Exists(pathToDataCustomNetLanes)) NetLanesDecalImporter.AddCustomNetLanesFolder(pathToDataCustomNetLanes);

                //GameManager.instance.RegisterUpdater(Initialize);
                EAIDataBaseManager.LoadDataBase();
                ExtraLib.AddOnInitialize(Initialize);

                updateSystem.UpdateAt<sys>(SystemUpdatePhase.MainLoop);
            } catch (Exception ex)
            {
                EAI.Logger.Error(ex); // Doing this, because the game isn't logging any error.
                throw ex; // This should still send the error to the game and so start the OnDispose.
            }

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

			//         foreach ( ModManager.ModInfo modInfo in GameManager.instance.modManager)
			//{
			//	modInfo.
			//}

			//PdxSdkPlatform pdxSdkPlatform = PlatformManager.instance.GetPSI<PdxSdkPlatform>("PdxSdk");

			if (m_Setting.Decals) ExtraLib.extraLibMonoScript.StartCoroutine(DecalsImporter.CreateCustomDecals());
			if (m_Setting.Surfaces) ExtraLib.extraLibMonoScript.StartCoroutine(SurfacesImporter.CreateCustomSurfaces());
			if (m_Setting.NetLanes) ExtraLib.extraLibMonoScript.StartCoroutine(NetLanesDecalImporter.CreateCustomNetLanes());
			ExtraLib.extraLibMonoScript.StartCoroutine(WaitForCustomStuffToFinish());
			//return true;
        }

		private static IEnumerator WaitForCustomStuffToFinish()
		{
			while( (m_Setting.Decals && !DecalsImporter.DecalsLoaded) || ( m_Setting.Surfaces && !SurfacesImporter.SurfacesIsLoaded) || ( m_Setting.NetLanes && !NetLanesDecalImporter.NetLanesLoaded)) 
			{
				yield return null;
			}
			EAI.Logger.Info("The loading of custom stuff as finished.");
			m_Setting.ResetCompatibility();
            EAIDataBaseManager.SaveValidateDataBase();
			EAIDataBaseManager.ClearNotLoadedAssetsFromFiles();

			//foreach (MaterialLibrary.MaterialDescription material in AssetDatabase.global.resources.materialLibrary.m_Materials)
			//{
			//	Logger.Info(material.m_Material.name);

			//	Logger.Info($"{material.m_Material.name} | Shader name : {material.m_Material.shader.name}");

			//	if (material.m_Material.name != "DefaultDecal" && material.m_Material.name != "CurvedDecal") continue;

			//	foreach (string s in material.m_Material.GetPropertyNames(UnityEngine.MaterialPropertyType.Int)) { Logger.Info($"{material.m_Material.name} | Int : {s}"); }
			//	foreach (string s in material.m_Material.GetPropertyNames(UnityEngine.MaterialPropertyType.Float)) { Logger.Info($"{material.m_Material.name} | Float : {s}"); }
			//	foreach (string s in material.m_Material.GetPropertyNames(UnityEngine.MaterialPropertyType.Vector)) { Logger.Info($"{material.m_Material.name} | Vector : {s}"); }
			//	foreach (string s in material.m_Material.GetPropertyNames(UnityEngine.MaterialPropertyType.Texture)) { Logger.Info($"{material.m_Material.name} | Texture : {s}"); }
			//	foreach (string s in material.m_Material.GetPropertyNames(UnityEngine.MaterialPropertyType.Matrix)) { Logger.Info($"{material.m_Material.name} | Matrix : {s}"); }
			//	foreach (string s in material.m_Material.GetPropertyNames(UnityEngine.MaterialPropertyType.ConstantBuffer)) { Logger.Info($"{material.m_Material.name} | ConstantBuffer : {s}"); }
			//	foreach (string s in material.m_Material.GetPropertyNames(UnityEngine.MaterialPropertyType.ComputeBuffer)) { Logger.Info($"{material.m_Material.name} | ComputeBuffer : {s}"); }

			//}

			yield break;
		}


        internal static void ClearData()
		{
			if (Directory.Exists(pathTempFolder))
			{
				Directory.Delete(pathTempFolder, true);
			}
            if ( EAI.m_Setting.DeleteDataBase ) EAIDataBaseManager.DeleteDatabase();
        }

		public static void LoadCustomAssets(string modPath)
		{
			if (Directory.Exists(Path.Combine(modPath, "CustomSurfaces")))	SurfacesImporter.AddCustomSurfacesFolder(Path.Combine(modPath, "CustomSurfaces"));
			if (Directory.Exists(Path.Combine(modPath, "CustomDecals")))	DecalsImporter.AddCustomDecalsFolder(Path.Combine(modPath, "CustomDecals"));
			if (Directory.Exists(Path.Combine(modPath, "CustomNetLanes")))	NetLanesDecalImporter.AddCustomNetLanesFolder(Path.Combine(modPath, "CustomNetLanes"));
        }

		public static void UnLoadCustomAssets(string modPath)
		{
			if (Directory.Exists(Path.Combine(modPath, "CustomSurfaces")))	SurfacesImporter.RemoveCustomSurfacesFolder(Path.Combine(modPath, "CustomSurfaces"));
			if (Directory.Exists(Path.Combine(modPath, "CustomDecals")))	DecalsImporter.RemoveCustomDecalsFolder(Path.Combine(modPath, "CustomDecals"));
            if (Directory.Exists(Path.Combine(modPath, "CustomNetLanes")))	NetLanesDecalImporter.RemoveCustomNetLanesFolder(Path.Combine(modPath, "CustomNetLanes"));
        }

	}
}
