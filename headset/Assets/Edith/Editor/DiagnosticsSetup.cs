using System;
using Edith.Capture;
using Edith.Diagnostics;
using Edith.Grab;
using Edith.Hands;
using Edith.Look;
using Edith.Pairing;
using Edith.Pinch;
using Edith.Scan;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// The status panel in the scene. It only does anything in a build made with EDITH_DIAGNOSTICS, and destroys
    /// itself in every other. Every step converges, so running it again is safe.
    public static class DiagnosticsSetup
    {
        const string DiagnosticsObjectName = "Diagnostics";

        /// Batch entry for adding the status panel to an already configured scene.
        public static void BatchAdd()
        {
            var exitCode = 1;
            try
            {
                AddToScene(ProjectSetup.ScenePath);
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

            T Find<T>(string setup) where T : Component => UnityEngine.Object.FindAnyObjectByType<T>()
                ?? throw new InvalidOperationException($"{scenePath} has no {typeof(T).Name}; run {setup} first");

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var host = Array.Find(scene.GetRootGameObjects(), go => go.name == DiagnosticsObjectName)
                ?? new GameObject(DiagnosticsObjectName, typeof(RectTransform));
            var panel = PinchSetup.Ensure<StatusPanel>(host);
            PinchSetup.SetObject(panel, "pinch", Find<PinchInput>(nameof(PinchSetup)));
            PinchSetup.SetObject(panel, "grab", Find<GrabInput>(nameof(GrabSetup)));
            PinchSetup.SetObject(panel, "look", Find<LookController>(nameof(LookSetup)));
            PinchSetup.SetObject(panel, "headsetCamera", Find<HeadsetCamera>(nameof(LookSetup)));
            PinchSetup.SetObject(panel, "pairing", Find<PhonePairing>(nameof(PairingSetup)));
            PinchSetup.SetObject(panel, "scan", Find<ScanController>(nameof(ScanSetup)));
            PinchSetup.SetObject(panel, "leftHand", Find<LeftHandSource>(nameof(PinchSetup)));
            PinchSetup.SetObject(panel, "rightHand", Find<RightHandSource>(nameof(PinchSetup)));
            PinchSetup.SetObject(panel, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(panel, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PinchSetup.SetObject(panel, "material",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }
    }
}
