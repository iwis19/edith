using System;
using Edith.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// The navigation arrow in the scene. Every step converges, so running it again is safe.
    public static class NavigationSetup
    {
        const string NavigationObjectName = "Navigation";

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

        [MenuItem("EDITH/Add Navigation Arrow")]
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

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }
    }
}
