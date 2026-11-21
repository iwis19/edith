using System;
using Edith.Ask;
using Edith.Hands;
using Edith.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// The navigation arrow and the finger-gun gestures in the scene, after Pinch and Ask. Every step converges, so
    /// running it again is safe.
    public static class NavigationSetup
    {
        const string NavigationObjectName = "Navigation";
        const string PinchObjectName = "Pinch";
        const string AskObjectName = "Ask";

        /// Batch entry for adding the arrow to an already configured scene.
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

        [MenuItem("EDITH/Add Navigation")]
        static void AddMenu() => AddToScene(ProjectSetup.ScenePath);

        public static void AddToScene(string scenePath)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var navigation = Array.Find(scene.GetRootGameObjects(), go => go.name == NavigationObjectName)
                ?? new GameObject(NavigationObjectName);
            var arrow = PinchSetup.Ensure<NavArrow>(navigation);
            PinchSetup.SetObject(arrow, "eye", rig.centerEyeAnchor);
            PinchSetup.SetObject(arrow, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PinchSetup.SetObject(arrow, "material",
                AssetDatabase.LoadAssetAtPath<Material>(DepthSetup.OccludedUIMaterialPath)
                ?? throw new InvalidOperationException($"Missing {DepthSetup.OccludedUIMaterialPath}; run DepthSetup first"));

            var roots = scene.GetRootGameObjects();
            var pinch = Array.Find(roots, go => go.name == PinchObjectName)
                ?? throw new InvalidOperationException($"{scenePath} has no {PinchObjectName} object; run PinchSetup first");
            var ask = Array.Find(roots, go => go.name == AskObjectName)
                ?? throw new InvalidOperationException($"{scenePath} has no {AskObjectName} object; run AskSetup first");
            var gestures = PinchSetup.Ensure<NavGestures>(navigation);
            PinchSetup.SetObject(gestures, "right", Need<RightHandSource>(pinch, "PinchSetup"));
            PinchSetup.SetObject(gestures, "left", Need<LeftHandSource>(pinch, "PinchSetup"));
            PinchSetup.SetObject(gestures, "ask", Need<AskController>(ask, "AskSetup"));
            PinchSetup.SetObject(gestures, "arrow", arrow);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }

        /// The `T` on `host`. GetComponent's missing result is not a plain null in the editor, so it is checked with ==.
        static T Need<T>(GameObject host, string setup) where T : Component
        {
            var found = host.GetComponent<T>();
            if (found == null) throw new InvalidOperationException($"{host.name} has no {typeof(T).Name}; run {setup} first");
            return found;
        }
    }
}
