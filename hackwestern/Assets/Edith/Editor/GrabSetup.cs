using System;
using Edith.Grab;
using Edith.Hands;
using Edith.Pinch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// Fist grab in the scene, beside the pinch input it takes priority over. Every step converges, so running it again is safe.
    public static class GrabSetup
    {
        const string PinchObjectName = "Pinch";

        /// Batch entry for adding the grab to an already configured scene.
        public static void BatchAdd()
        {
            var exitCode = 1;
            try
            {
                AddToScene(ProjectSetup.ScenePath);
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

            var input = PinchSetup.Ensure<GrabInput>(pinch);
            PinchSetup.SetObject(input, "hand", pinch.GetComponent<RightHandSource>());
            PinchSetup.SetObject(PinchSetup.Ensure<SnapshotGrab>(pinch), "input", input);
            PinchSetup.SetObject(pinch.GetComponent<PinchInput>(), "grab", input);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }
    }
}
