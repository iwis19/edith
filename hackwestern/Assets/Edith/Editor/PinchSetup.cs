using System;
using Edith.Hands;
using Edith.Pinch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// Right-hand tracking and pinch-and-drag in the scene. Every step converges, so running it again is safe.
    public static class PinchSetup
    {
        const string OutlineMaterialPath = "Assets/Edith/Pinch/PinchOutline.mat";
        const string PinchObjectName = "Pinch";
        const string RightHandName = "HandRight";

        public static void AddToScene(string scenePath)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");
            var hand = RightHand(rig);

            var pinch = Array.Find(scene.GetRootGameObjects(), go => go.name == PinchObjectName) ?? new GameObject(PinchObjectName);
            var source = Ensure<RightHandSource>(pinch);
            SetObject(source, "hand", hand);
            SetObject(source, "skeleton", hand.GetComponent<OVRSkeleton>());
            SetObject(source, "eye", rig.centerEyeAnchor);

            var input = Ensure<PinchInput>(pinch);
            SetObject(input, "hand", source);

            var line = Ensure<LineRenderer>(pinch);
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = OutlineMaterial();
            SetObject(Ensure<PinchOutline>(pinch), "input", input);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }

        /// The right OVRHand already on the rig, or a new one with an OpenXR skeleton and no mesh.
        static OVRHand RightHand(OVRCameraRig rig)
        {
            foreach (var existing in rig.rightHandAnchor.GetComponentsInChildren<OVRHand>(true))
                if (new SerializedObject(existing).FindProperty("HandType").intValue == (int)OVRHand.Hand.HandRight)
                    return existing;

            var go = new GameObject(RightHandName);
            go.transform.SetParent(rig.rightHandAnchor, false);
            var hand = go.AddComponent<OVRHand>();
            SetInt(hand, "HandType", (int)OVRHand.Hand.HandRight);
            SetInt(go.AddComponent<OVRSkeleton>(), "_skeletonType", (int)OVRSkeleton.SkeletonType.XRHandRight);
            return hand;
        }

        static Material OutlineMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (material != null) return material;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutlineMaterialPath));
            material = new Material(Shader.Find("Sprites/Default") ?? throw new InvalidOperationException("Missing shader Sprites/Default"));
            AssetDatabase.CreateAsset(material, OutlineMaterialPath);
            return material;
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            // A missing component is Unity's fake null, which ?? does not see.
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            (so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name} has no {field}")).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(UnityEngine.Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            (so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name} has no {field}")).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
