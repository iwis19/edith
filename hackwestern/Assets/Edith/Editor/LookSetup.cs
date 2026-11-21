using System;
using Edith.Ask;
using Edith.Capture;
using Edith.Grab;
using Edith.Look;
using Edith.Pinch;
using Meta.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// Photo snapshots in the scene: the headset camera, the depth raycast that lines its crop up with the framed
    /// object, and the controller that hangs a snapshot where a box was drawn. Every step converges, so running it
    /// again is safe.
    public static class LookSetup
    {
        const string PinchObjectName = "Pinch";
        const string AskObjectName = "Ask";
        const string LookObjectName = "Look";
        const string CameraObjectName = "HeadsetCamera";
        const string RaycastObjectName = "EnvironmentRaycast";

        /// Batch entry for adding photo snapshots to an already configured scene.
        public static void BatchAdd()
        {
            var exitCode = 1;
            try
            {
                EnableCameraAccess();
                AddToScene(ProjectSetup.ScenePath);
                ManifestSetup.EnsurePermission(HeadsetCamera.HeadsetCameraPermission);
                AssetDatabase.SaveAssets();
                exitCode = 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(exitCode);
        }

        /// Declares the headset camera permission in the manifest Meta generates.
        public static void EnableCameraAccess()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            config.isPassthroughCameraAccessEnabled = true;
            OVRProjectConfig.CommitProjectConfig(config);
        }

        public static void AddToScene(string scenePath)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var pinch = Array.Find(scene.GetRootGameObjects(), go => go.name == PinchObjectName)
                ?? throw new InvalidOperationException($"{scenePath} has no {PinchObjectName} object; run PinchSetup first");
            var grab = pinch.GetComponent<SnapshotGrab>()
                ?? throw new InvalidOperationException($"{PinchObjectName} has no SnapshotGrab; run GrabSetup first");
            var ask = Array.Find(scene.GetRootGameObjects(), go => go.name == AskObjectName)?.GetComponent<AskController>()
                ?? throw new InvalidOperationException($"{scenePath} has no AskController; run AskSetup first");
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            // Off until HeadsetCamera has the user's permission: the camera fails to start without it.
            var cameraObject = Root(scene, CameraObjectName);
            var access = PinchSetup.Ensure<PassthroughCameraAccess>(cameraObject);
            access.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            access.enabled = false;
            EditorUtility.SetDirty(access);
            var camera = PinchSetup.Ensure<HeadsetCamera>(cameraObject);
            PinchSetup.SetObject(camera, "cameraAccess", access);

            var raycast = PinchSetup.Ensure<EnvironmentRaycastManager>(Root(scene, RaycastObjectName));

            var look = PinchSetup.Ensure<LookController>(Root(scene, LookObjectName));
            PinchSetup.SetObject(look, "pinch", pinch.GetComponent<PinchInput>());
            PinchSetup.SetObject(look, "headsetCamera", camera);
            PinchSetup.SetObject(look, "grab", grab);
            PinchSetup.SetObject(look, "ask", ask);
            PinchSetup.SetObject(look, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(look, "environmentRaycast", raycast);
            PinchSetup.SetObject(look, "occludedMaterial",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }

        static GameObject Root(Scene scene, string name) =>
            Array.Find(scene.GetRootGameObjects(), go => go.name == name) ?? new GameObject(name);
    }
}
