using System;
using Edith.Occlusion;
using Meta.XR.EnvironmentDepth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Edith.Editor
{
    /// Meta's Depth API, so real objects in front of a snapshot hide it. Soft environment occlusion covers the room;
    /// hands come out of the depth map, whose low resolution leaves a ragged halo around fingers, and the tracked hand
    /// mesh cuts them out instead. Every step converges, so running it again is safe.
    public static class DepthSetup
    {
        /// Snapshot photos and text use this material so the depth map can hide them.
        public const string OccludedUIMaterialPath = "Assets/Edith/Occlusion/OccludedUI.mat";
        const string HandOccluderMaterialPath = "Assets/Edith/Occlusion/HandOccluder.mat";
        const string HandPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";
        const string DepthObjectName = "EnvironmentDepth";

        /// Batch entry for updating the depth objects in an already configured scene.
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

        /// The Depth API on OpenXR reads depth through Unity OpenXR: Meta's occlusion subsystem, which needs both
        /// features.
        public static void EnableFeatures()
        {
            // A newly installed package's features join the settings only when the editor refreshes them, which batch
            // mode never does on its own.
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            foreach (var feature in new OpenXRFeature[]
                     {
                         settings.GetFeature<AROcclusionFeature>(),
                         settings.GetFeature<ARSessionFeature>(),
                     })
            {
                if (feature == null)
                    throw new InvalidOperationException("Unity OpenXR: Meta occlusion or session feature is missing");
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
        }

        /// Adds the depth manager and one hand occluder per hand to the scene, then saves it. Meta's Depth API
        /// validation rules only fire while the open scene uses depth, so run this before Meta's fixes.
        public static void AddToScene(string scenePath)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            MaterialAsset(OccludedUIMaterialPath, "Edith/OccludedUI");

            var depthObject = FindRoot(scene, DepthObjectName) ?? new GameObject(DepthObjectName);
            var manager = depthObject.GetComponent<EnvironmentDepthManager>() ?? depthObject.AddComponent<EnvironmentDepthManager>();
            SetBool(manager, "_removeHands", true);
            if (depthObject.GetComponent<SpatialDataPermission>() == null)
                depthObject.AddComponent<SpatialDataPermission>();

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException($"{scenePath} has no OVRCameraRig");
            var occluder = MaterialAsset(HandOccluderMaterialPath, "Edith/HandOccluder");
            AddHandOccluder(rig, "LeftHandAnchor", OVRHand.Hand.HandLeft, OVRSkeleton.SkeletonType.XRHandLeft,
                OVRMesh.MeshType.XRHandLeft, occluder);
            AddHandOccluder(rig, "RightHandAnchor", OVRHand.Hand.HandRight, OVRSkeleton.SkeletonType.XRHandRight,
                OVRMesh.MeshType.XRHandRight, occluder);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scenePath}");
        }

        /// An invisible tracked-hand mesh drawn into depth before the UI, so a snapshot behind a hand is cut out along
        /// the hand's own outline.
        static void AddHandOccluder(OVRCameraRig rig, string anchorName, OVRHand.Hand hand,
            OVRSkeleton.SkeletonType skeleton, OVRMesh.MeshType mesh, Material material)
        {
            var anchor = rig.transform.Find($"TrackingSpace/{anchorName}")
                ?? throw new InvalidOperationException($"OVRCameraRig has no TrackingSpace/{anchorName}");
            var name = $"{hand}Occluder";
            var existing = anchor.Find(name);
            var visual = existing != null
                ? existing.gameObject
                : (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefab) ?? throw new InvalidOperationException($"Missing {HandPrefab}"),
                    anchor);
            visual.name = name;

            SetInt(visual.GetComponent<OVRHand>(), "HandType", (int)hand);
            SetInt(visual.GetComponent<OVRSkeleton>(), "_skeletonType", (int)skeleton);
            SetInt(visual.GetComponent<OVRMesh>(), "_meshType", (int)mesh);
            SetInt(visual.GetComponent<OVRMeshRenderer>(), "_confidenceBehavior", (int)OVRMeshRenderer.ConfidenceBehavior.None);
            if (visual.GetComponent<TrackedHandOccluder>() == null)
                visual.AddComponent<TrackedHandOccluder>();
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var slots = new Material[renderer.sharedMaterials.Length];
                Array.Fill(slots, material);
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static GameObject FindRoot(Scene scene, string name) =>
            Array.Find(scene.GetRootGameObjects(), go => go.name == name);

        /// The material asset at `path`, made with the shader named `shaderName` when it does not exist yet. A shader
        /// a material asset in the scene uses is kept in the player build.
        public static Material MaterialAsset(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(shaderName) ?? throw new InvalidOperationException($"Missing shader {shaderName}"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void SetBool(UnityEngine.Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            (so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name} has no {field}")).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(UnityEngine.Object target, string field, int value)
        {
            if (target == null) throw new InvalidOperationException($"Missing component for {field}");
            var so = new SerializedObject(target);
            (so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name} has no {field}")).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
