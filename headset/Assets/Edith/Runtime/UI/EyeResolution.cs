using UnityEngine;
using UnityEngine.XR;

namespace Edith.UI
{
    /// Renders each eye at more than the runtime's default resolution, so small text on the panels stays sharp.
    public static class EyeResolution
    {
        public const float Scale = 1.3f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Raise()
        {
            if (!XRSettings.enabled) return;
            XRSettings.eyeTextureResolutionScale = Scale;
            Debug.Log($"[EDITH ui] eye texture scale {XRSettings.eyeTextureResolutionScale:F2}, " +
                $"{XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, MSAA {QualitySettings.antiAliasing}x");
        }
    }
}
