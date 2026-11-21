using System;
using Edith.Pairing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// The phone pairing panel in the scene. Every step converges, so running it again is safe.
    public static class PairingSetup
    {
        const string PairingObjectName = "Pairing";

        /// Batch entry for adding the pairing panel to an already configured scene.
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

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var pairing = Array.Find(scene.GetRootGameObjects(), go => go.name == PairingObjectName)
                ?? new GameObject(PairingObjectName, typeof(RectTransform));
            var panel = PinchSetup.Ensure<PhonePairing>(pairing);
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
