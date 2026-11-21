using System;
using Edith.Hands;
using Edith.Pairing;
using Edith.Pinch;
using Edith.Welcome;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// The first-launch welcome in the scene. It needs the pinch input and the hands from PinchSetup and the pairing
    /// panel from PairingSetup. Every step converges, so running it again is safe.
    public static class WelcomeSetup
    {
        const string WelcomeObjectName = "Welcome";
        const string PinchObjectName = "Pinch";
        const string PairingObjectName = "Pairing";

        /// Batch entry for adding the welcome to an already configured scene.
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

            var pinch = Array.Find(scene.GetRootGameObjects(), go => go.name == PinchObjectName)
                ?? throw new InvalidOperationException($"{scenePath} has no {PinchObjectName} object; run PinchSetup first");
            var pairing = Array.Find(scene.GetRootGameObjects(), go => go.name == PairingObjectName)?.GetComponent<PhonePairing>()
                ?? throw new InvalidOperationException($"{scenePath} has no PhonePairing; run PairingSetup first");
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var host = Array.Find(scene.GetRootGameObjects(), go => go.name == WelcomeObjectName)
                ?? new GameObject(WelcomeObjectName, typeof(RectTransform));
            var welcome = PinchSetup.Ensure<WelcomeController>(host);
            PinchSetup.SetObject(welcome, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(welcome, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PinchSetup.SetObject(welcome, "material",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));
            PinchSetup.SetObject(welcome, "pairing", pairing);
            PinchSetup.SetObject(welcome, "pinch", pinch.GetComponent<PinchInput>());
            PinchSetup.SetObject(welcome, "right", pinch.GetComponent<RightHandSource>()
                ?? throw new InvalidOperationException($"{PinchObjectName} has no RightHandSource; run PinchSetup first"));
            PinchSetup.SetObject(welcome, "left", pinch.GetComponent<LeftHandSource>()
                ?? throw new InvalidOperationException($"{PinchObjectName} has no LeftHandSource; run PinchSetup first"));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }

        /// EDITH > Reset Welcome runs the welcome again from the first step, as a new profile would.
        [MenuItem("EDITH/Reset Welcome")]
        static void ResetWelcome()
        {
            WelcomeStore.Reset();
            Debug.Log("[EDITH welcome] forgotten; it runs again at the next launch");
        }
    }

    /// Keeps the pixel-art sprites crisp: no smoothing, no compression, no mip-maps.
    sealed class EdithUiImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/EdithUI/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
