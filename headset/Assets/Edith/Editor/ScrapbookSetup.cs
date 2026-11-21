using System;
using Edith.Ask;
using Edith.Hands;
using Edith.Look;
using Edith.Pinch;
using Edith.Scan;
using Edith.Scrapbook;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// The Scrapbook in the scene: the panel, wired to both hands, the pinch and the thumb gestures, and the hand
    /// gate it holds while open, which Look, Scan and the pinch outline check. Every step converges, so running it
    /// again is safe.
    public static class ScrapbookSetup
    {
        const string ScrapbookObjectName = "Scrapbook";

        /// Batch entry for adding the Scrapbook to an already configured scene.
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

            T Find<T>(string setup) where T : Component => UnityEngine.Object.FindAnyObjectByType<T>()
                ?? throw new InvalidOperationException($"{scenePath} has no {typeof(T).Name}; run {setup} first");

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var host = Array.Find(scene.GetRootGameObjects(), go => go.name == ScrapbookObjectName) ?? new GameObject(ScrapbookObjectName);
            var gate = PinchSetup.Ensure<HandGate>(host);
            var panel = PinchSetup.Ensure<ScrapbookPanel>(host);
            PinchSetup.SetObject(panel, "leftHand", Find<LeftHandSource>(nameof(ScanSetup)));
            PinchSetup.SetObject(panel, "rightHand", Find<RightHandSource>(nameof(PinchSetup)));
            PinchSetup.SetObject(panel, "pinch", Find<PinchInput>(nameof(PinchSetup)));
            PinchSetup.SetObject(panel, "gestures", Find<ThumbGestures>(nameof(AskSetup)));
            PinchSetup.SetObject(panel, "gate", gate);
            PinchSetup.SetObject(panel, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(panel, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PinchSetup.SetObject(panel, "material",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));

            PinchSetup.SetObject(Find<LookController>(nameof(LookSetup)), "gate", gate);
            PinchSetup.SetObject(Find<ScanController>(nameof(ScanSetup)), "gate", gate);
            PinchSetup.SetObject(Find<PinchOutline>(nameof(PinchSetup)), "gate", gate);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }
    }
}
