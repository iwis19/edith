using System;
using Edith.Ask;
using Edith.Capture;
using Edith.Hands;
using Edith.Look;
using Edith.Scan;
using Meta.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// Scan in the scene: the controller that reads both palms, the headset camera and depth. Every step converges,
    /// so running it again is safe.
    public static class ScanSetup
    {
        const string PinchObjectName = "Pinch";
        const string CameraObjectName = "HeadsetCamera";
        const string RaycastObjectName = "EnvironmentRaycast";
        const string ScanObjectName = "Scan";
        const string AskObjectName = "Ask";
        const string LookObjectName = "Look";

        /// Batch entry for adding Scan to an already configured scene.
        public static void BatchAdd()
        {
            var exitCode = 1;
            try
            {
                AddToScene(ProjectSetup.ScenePath);
                BackendSettingsSetup.Write();
                AssetDatabase.SaveAssets();
                exitCode = 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(exitCode);
        }

        public static void AddToScene(string scenePath)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject Root(string name, string setup) => Array.Find(scene.GetRootGameObjects(), go => go.name == name)
                ?? throw new InvalidOperationException($"{scenePath} has no {name} object; run {setup} first");

            var pinch = Root(PinchObjectName, nameof(PinchSetup));
            var cameraObject = Root(CameraObjectName, nameof(LookSetup));
            var raycast = Root(RaycastObjectName, nameof(LookSetup)).GetComponent<EnvironmentRaycastManager>();
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var scan = Array.Find(scene.GetRootGameObjects(), go => go.name == ScanObjectName) ?? new GameObject(ScanObjectName);
            var controller = PinchSetup.Ensure<ScanController>(scan);
            PinchSetup.SetObject(controller, "leftHand", pinch.GetComponent<LeftHandSource>());
            PinchSetup.SetObject(controller, "rightHand", pinch.GetComponent<RightHandSource>());
            PinchSetup.SetObject(controller, "headsetCamera", cameraObject.GetComponent<HeadsetCamera>());
            PinchSetup.SetObject(controller, "cameraAccess", cameraObject.GetComponent<PassthroughCameraAccess>());
            PinchSetup.SetObject(controller, "environmentRaycast", raycast);
            PinchSetup.SetObject(controller, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(controller, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PinchSetup.SetObject(controller, "material",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));
            PinchSetup.SetObject(Root(AskObjectName, nameof(AskSetup)).GetComponent<AskController>(), "scan", controller);
            // A quick pinch on a pin opens it as a Look.
            PinchSetup.SetObject(Root(LookObjectName, nameof(LookSetup)).GetComponent<LookController>(), "scan", controller);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }
    }
}
