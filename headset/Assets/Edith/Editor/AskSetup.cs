using System;
using Edith.Ask;
using Edith.Hands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// Ask in the scene: both hands' microgestures, the voice and the controller, next to the pinch it shares the
    /// hands with. Every step converges, so running it again is safe.
    public static class AskSetup
    {
        const string PinchObjectName = "Pinch";
        const string AskObjectName = "Ask";
        public const string MicrophonePermission = "android.permission.RECORD_AUDIO";

        /// Batch entry for adding Ask to an already configured scene.
        public static void BatchAdd()
        {
            var exitCode = 1;
            try
            {
                AddToScene(ProjectSetup.ScenePath);
                BackendSettingsSetup.Write();
                ManifestSetup.EnsurePermission(MicrophonePermission);
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
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var ask = Array.Find(scene.GetRootGameObjects(), go => go.name == AskObjectName) ?? new GameObject(AskObjectName);
            // One event source per hand, the right hand's first.
            while (ask.GetComponents<OVRMicrogestureEventSource>().Length < 2) ask.AddComponent<OVRMicrogestureEventSource>();
            var sources = ask.GetComponents<OVRMicrogestureEventSource>();
            PinchSetup.SetObject(sources[0], "_hand", PinchSetup.HandOf<RightHandSource>(pinch));
            PinchSetup.SetObject(sources[1], "_hand", PinchSetup.HandOf<LeftHandSource>(pinch));
            var gestures = PinchSetup.Ensure<ThumbGestures>(ask);
            PinchSetup.SetObject(gestures, "right", sources[0]);
            PinchSetup.SetObject(gestures, "left", sources[1]);
            var voice = PinchSetup.Ensure<AskVoice>(ask);

            var controller = PinchSetup.Ensure<AskController>(ask);
            PinchSetup.SetObject(controller, "gestures", gestures);
            PinchSetup.SetObject(controller, "voice", voice);
            PinchSetup.SetObject(controller, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(controller, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PinchSetup.SetObject(controller, "material",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }
    }
}
