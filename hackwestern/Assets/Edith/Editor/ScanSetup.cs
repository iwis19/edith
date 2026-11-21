using System;
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
    /// Scan in the scene: left-hand tracking beside the right hand's, and the controller that reads both palms, the
    /// headset camera and depth. Every step converges, so running it again is safe.
    public static class ScanSetup
    {
        const string PinchObjectName = "Pinch";
        const string CameraObjectName = "HeadsetCamera";
        const string RaycastObjectName = "EnvironmentRaycast";
        const string ScanObjectName = "Scan";
        const string LookObjectName = "Look";
        const string LeftHandName = "HandLeft";

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

            var rightHand = Root(PinchObjectName, nameof(PinchSetup)).GetComponent<RightHandSource>();
            var cameraObject = Root(CameraObjectName, nameof(LookSetup));
            var raycast = Root(RaycastObjectName, nameof(LookSetup)).GetComponent<EnvironmentRaycastManager>();
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var scan = Array.Find(scene.GetRootGameObjects(), go => go.name == ScanObjectName) ?? new GameObject(ScanObjectName);
            var hand = LeftHand(rig);
            var leftHand = PinchSetup.Ensure<LeftHandSource>(scan);
            PinchSetup.SetObject(leftHand, "hand", hand);
            PinchSetup.SetObject(leftHand, "skeleton", hand.GetComponent<OVRSkeleton>());
            PinchSetup.SetObject(leftHand, "eye", rig.centerEyeAnchor);

            var controller = PinchSetup.Ensure<ScanController>(scan);
            PinchSetup.SetObject(controller, "leftHand", leftHand);
            PinchSetup.SetObject(controller, "rightHand", rightHand);
            PinchSetup.SetObject(controller, "headsetCamera", cameraObject.GetComponent<HeadsetCamera>());
            PinchSetup.SetObject(controller, "cameraAccess", cameraObject.GetComponent<PassthroughCameraAccess>());
            PinchSetup.SetObject(controller, "environmentRaycast", raycast);
            PinchSetup.SetObject(controller, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(controller, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PinchSetup.SetObject(controller, "material",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));
            // A quick pinch on a pin opens it as a Look.
            PinchSetup.SetObject(Root(LookObjectName, nameof(LookSetup)).GetComponent<LookController>(), "scan", controller);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }

        /// The left OVRHand already on the rig, or a new one with an OpenXR skeleton and no mesh.
        static OVRHand LeftHand(OVRCameraRig rig)
        {
            foreach (var existing in rig.leftHandAnchor.GetComponentsInChildren<OVRHand>(true))
                if (new SerializedObject(existing).FindProperty("HandType").intValue == (int)OVRHand.Hand.HandLeft)
                    return existing;

            var go = new GameObject(LeftHandName);
            go.transform.SetParent(rig.leftHandAnchor, false);
            var hand = go.AddComponent<OVRHand>();
            SetInt(hand, "HandType", (int)OVRHand.Hand.HandLeft);
            SetInt(go.AddComponent<OVRSkeleton>(), "_skeletonType", (int)OVRSkeleton.SkeletonType.XRHandLeft);
            return hand;
        }

        static void SetInt(UnityEngine.Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            (so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name} has no {field}")).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
