using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Edith.Occlusion
{
    /// Requests spatial data (USE_SCENE), which Meta's EnvironmentDepthManager waits for before it reads depth, then
    /// logs whether occlusion came up: a missing link anywhere in the depth chain fails silently.
    public sealed class SpatialDataPermission : MonoBehaviour
    {
        public const string ScenePermission = "com.oculus.permission.USE_SCENE";
        const float DepthWaitSeconds = 5f;

        /// True once the spatial data prompt has been answered or was not needed.
        public static bool Asked { get; private set; }

        IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                var answered = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => answered = true;
                callbacks.PermissionDenied += _ => answered = true;
                callbacks.PermissionDeniedAndDontAskAgain += _ => answered = true;
                Permission.RequestUserPermission(ScenePermission, callbacks);
                yield return new WaitUntil(() => answered);
            }
            Asked = true;
            if (!Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                Debug.Log("[EDITH depth] spatial data permission denied, so real objects will not cover snapshots");
                yield break;
            }
#endif
            var deadline = Time.realtimeSinceStartup + DepthWaitSeconds;
            yield return new WaitUntil(() => DepthOn() || Time.realtimeSinceStartup > deadline);
            Debug.Log($"[EDITH depth] occlusion {(DepthOn() ? "on" : $"still off after {DepthWaitSeconds} s")}");
        }

        // EnvironmentDepthManager enables one of these global keywords once the first depth texture arrives.
        static bool DepthOn() => Shader.IsKeywordEnabled("SOFT_OCCLUSION") || Shader.IsKeywordEnabled("HARD_OCCLUSION");
    }
}
