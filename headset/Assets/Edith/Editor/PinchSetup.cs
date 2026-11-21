using System;
using Edith.Hands;
using Edith.Pinch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Edith.Editor
{
    /// Tracking of both hands and pinch-and-drag in the scene. Every step converges, so running it again is safe.
    public static class PinchSetup
    {
        const string OutlineMaterialPath = "Assets/Edith/Pinch/PinchOutline.mat";
        const string PinchObjectName = "Pinch";
        const string RightHandName = "HandRight";
        const string LeftHandName = "HandLeft";

        public static void AddToScene(string scenePath)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");

            var pinch = Array.Find(scene.GetRootGameObjects(), go => go.name == PinchObjectName) ?? new GameObject(PinchObjectName);
            var right = Ensure<RightHandSource>(pinch);
            Wire(right, rig, Hand(rig.rightHandAnchor, OVRHand.Hand.HandRight, OVRSkeleton.SkeletonType.XRHandRight, RightHandName));
            var left = Ensure<LeftHandSource>(pinch);
            Wire(left, rig, Hand(rig.leftHandAnchor, OVRHand.Hand.HandLeft, OVRSkeleton.SkeletonType.XRHandLeft, LeftHandName));
            // The Pinch object holds the only hand sources, so each hand has one reader.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var stray in root.GetComponentsInChildren<HandSource>(true))
                    if (stray != right && stray != left) UnityEngine.Object.DestroyImmediate(stray);

            var input = Ensure<PinchInput>(pinch);
            SetObject(input, "right", right);
            SetObject(input, "left", left);

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

        static void Wire(HandSource source, OVRCameraRig rig, OVRHand hand)
        {
            SetObject(source, "hand", hand);
            SetObject(source, "skeleton", hand.GetComponent<OVRSkeleton>());
            SetObject(source, "eye", rig.centerEyeAnchor);
        }

        /// The OVRHand of that side already under `anchor`, or a new one with an OpenXR skeleton and no mesh.
        static OVRHand Hand(Transform anchor, OVRHand.Hand side, OVRSkeleton.SkeletonType skeleton, string name)
        {
            foreach (var existing in anchor.GetComponentsInChildren<OVRHand>(true))
                if (new SerializedObject(existing).FindProperty("HandType").intValue == (int)side)
                    return existing;

            var go = new GameObject(name);
            go.transform.SetParent(anchor, false);
            var hand = go.AddComponent<OVRHand>();
            SetInt(hand, "HandType", (int)side);
            SetInt(go.AddComponent<OVRSkeleton>(), "_skeletonType", (int)skeleton);
            return hand;
        }

        /// The OVRHand a hand source on `host` reads.
        internal static OVRHand HandOf<T>(GameObject host) where T : HandSource
        {
            var source = host.GetComponent<T>();
            if (source == null) throw new InvalidOperationException($"{host.name} has no {typeof(T).Name}; run PinchSetup first");
            return new SerializedObject(source).FindProperty("hand").objectReferenceValue as OVRHand
                ?? throw new InvalidOperationException($"{typeof(T).Name} has no hand");
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

        internal static T Ensure<T>(GameObject go) where T : Component
        {
            // A missing component is Unity's fake null, which ?? does not see.
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        internal static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
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
