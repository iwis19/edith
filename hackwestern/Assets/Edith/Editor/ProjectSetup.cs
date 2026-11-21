using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;

namespace Edith.Editor
{
    /// Quest 3/3S project configuration and APK build. Every step converges, so running it again is safe.
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Edith/Scenes/Main.unity";
        const string ApkPath = "Builds/edith.apk";
        const string RecordingApkPath = "Builds/edith-record.apk";
        /// Compiles HandRecorder into the recording build only.
        const string RecordHandsDefine = "EDITH_RECORD_HANDS";
        const string CameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        [MenuItem("EDITH/Configure Project")]
        static async void ConfigureMenu() => await Configure();

        [MenuItem("EDITH/Build APK")]
        static void BuildMenu() => Build();

        [MenuItem("EDITH/Build Recording APK")]
        static void BuildRecordingMenu() => Build(recordHands: true);

        /// Batch entry. Run without -quit: Meta's fixes are async, so this exits the editor itself.
        public static async void BatchConfigure()
        {
            var exitCode = 1;
            try
            {
                await Configure();
                exitCode = 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(exitCode);
        }

        /// Batch entry, in a separate run from BatchConfigure: Meta's fixes change scripting defines, and the
        /// recompile that follows blocks a build in the same session.
        public static void BatchBuild()
        {
            if (!Build()) EditorApplication.Exit(1);
        }

        /// Batch entry for the recording build, which also writes every right-hand frame to the headset.
        public static void BatchBuildRecording()
        {
            if (!Build(recordHands: true)) EditorApplication.Exit(1);
        }

        public static async Task Configure()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Could not switch to Android. Is Android Build Support installed?");

            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "hackwestern";
            PlayerSettings.productName = "EDITH";
            PlayerSettings.SetApplicationIdentifier(android, "com.hackwestern.edith");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            var config = OVRProjectConfig.CachedProjectConfig;
            config.targetDeviceTypes = new List<OVRProjectConfig.DeviceType>
            {
                OVRProjectConfig.DeviceType.Quest3,
                OVRProjectConfig.DeviceType.Quest3S,
            };
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            // Horizon OS refuses to launch an app that doesn't declare hand tracking while no controllers are paired.
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.HandsOnly;
            config.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;
            // Declares USE_SCENE, which the Depth API waits for.
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(config);

            EnsureScene();
            DepthSetup.EnableFeatures();
            DepthSetup.AddToScene(ScenePath);
            PinchSetup.AddToScene(ScenePath);
            GrabSetup.AddToScene(ScenePath);
            AskSetup.AddToScene(ScenePath);
            PairingSetup.AddToScene(ScenePath);
            LookSetup.EnableCameraAccess();
            LookSetup.AddToScene(ScenePath);
            ScanSetup.AddToScene(ScenePath);
            BackendSettingsSetup.Write();
            // Meta's fixes enable rules that only pass after an earlier fix, so one pass is not enough.
            for (var pass = 0; pass < 3; pass++)
                await OVRProjectSetup.FixAllAsync(BuildTargetGroup.Android);
            ManifestSetup.EnsurePermission(AskSetup.MicrophonePermission);
            ManifestSetup.EnsurePermission(Edith.Capture.HeadsetCamera.HeadsetCameraPermission);
            FixOpenXRValidation();
            AssetDatabase.SaveAssets();
            Debug.Log("[EDITH setup] configured for Quest 3/3S");
        }

        /// Creates the scene only when it is missing, so edits made in the editor are kept.
        static void EnsureScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefab)
                    ?? throw new InvalidOperationException($"Missing {CameraRigPrefab}");
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var manager = rig.GetComponent<OVRManager>();
                manager.isInsightPassthroughEnabled = true;
                manager.shouldBoundaryVisibilityBeSuppressed = true;

                var eye = rig.GetComponent<OVRCameraRig>().centerEyeAnchor.GetComponent<Camera>();
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = Color.clear;

                new GameObject("Passthrough").AddComponent<OVRPassthroughLayer>();

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException($"Could not save {ScenePath}");
                Debug.Log($"[EDITH setup] created {ScenePath}");
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void FixOpenXRValidation()
        {
            var issues = new List<OpenXRFeature.ValidationRule>();
            for (var pass = 0; pass < 3; pass++)
            {
                issues.Clear();
                OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
                var fixable = issues.FindAll(i => i.fixIt != null && i.fixItAutomatic);
                if (fixable.Count == 0) break;
                foreach (var issue in fixable)
                {
                    Debug.Log($"[EDITH setup] OpenXR fix: {issue.message}");
                    issue.fixIt();
                }
            }
            foreach (var issue in issues)
                Debug.Log($"[EDITH setup] OpenXR {(issue.error ? "error" : "warning")} left: {issue.message}");
        }

        public static bool Build(bool recordHands = false)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = recordHands ? RecordingApkPath : ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
                extraScriptingDefines = recordHands ? new[] { RecordHandsDefine } : Array.Empty<string>(),
            });
            var summary = report.summary;
            Debug.Log($"[EDITH setup] build {summary.result} {summary.outputPath} {summary.totalSize} bytes, {summary.totalErrors} errors");
            return summary.result == BuildResult.Succeeded;
        }
    }
}
