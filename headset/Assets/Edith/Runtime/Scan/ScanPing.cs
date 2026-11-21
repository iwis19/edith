using UnityEngine;

namespace Edith.Scan
{
    /// The scan's ping across the room: a burst of shells that grow out from a point and light up real surfaces as
    /// they pass, drawn by the Edith/ScanPing shader from Meta's environment depth. Fire one with Burst; a shell
    /// reaches a thing `distance / Speed` seconds after it leaves, which is when a pin on that thing should appear.
    public sealed class ScanPing : MonoBehaviour
    {
        /// How fast a shell grows, in metres per second, and how far it goes before it has faded out.
        public const float Speed = 2.5f, MaxRadius = 4.5f;
        // A burst is three shells this far apart, each dimmer than the last.
        const int Shells = 3;
        const float ShellGap = 0.15f, ShellDimming = 0.5f;
        // The quad sits this far in front of the eye and is wide enough to fill the view.
        const float QuadAhead = 0.4f, QuadSize = 3f;

        static readonly int PingDataId = Shader.PropertyToID("_PingData");
        static readonly int PingBrightId = Shader.PropertyToID("_PingBright");
        static readonly int ReprojectionId = Shader.PropertyToID("_EnvironmentDepthReprojectionMatrices");
        static readonly int InverseId = Shader.PropertyToID("_EdithDepthInverse");

        struct Shell
        {
            public Vector3 Origin;
            public float Born, Brightness;
        }

        readonly Shell[] shells = new Shell[Shells];
        readonly Vector4[] data = new Vector4[Shells];
        readonly Vector4[] bright = new Vector4[Shells];
        readonly Matrix4x4[] inverse = new Matrix4x4[2];
        Transform eye;
        Material material;
        MeshRenderer quad;
        float busyUntil = -1f;

        /// Builds the ping in front of `eye`. Without the shader in the build it stays silent.
        public static ScanPing Create(GameObject host, Transform eye)
        {
            var ping = host.AddComponent<ScanPing>();
            ping.eye = eye;
            var shader = Resources.Load<Shader>("EdithScanPing");
            if (shader == null)
            {
                Debug.LogWarning("[EDITH scan] the ping shader is missing from the build");
                return ping;
            }
            ping.material = new Material(shader);
            var go = new GameObject("ScanPing", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(eye, false);
            go.transform.localPosition = Vector3.forward * QuadAhead;
            go.transform.localScale = Vector3.one * QuadSize;
            go.GetComponent<MeshFilter>().sharedMesh = Quad();
            ping.quad = go.GetComponent<MeshRenderer>();
            ping.quad.sharedMaterial = ping.material;
            ping.quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ping.quad.receiveShadows = false;
            ping.quad.enabled = false;
            for (var i = 0; i < Shells; i++) ping.shells[i].Born = -100f;
            return ping;
        }

        /// Sends a burst of shells out from `origin`.
        public void Burst(Vector3 origin)
        {
            if (quad == null) return;
            for (var i = 0; i < Shells; i++)
                shells[i] = new Shell { Origin = origin, Born = Time.time + i * ShellGap, Brightness = Mathf.Pow(ShellDimming, i) };
            busyUntil = Time.time + (Shells - 1) * ShellGap + MaxRadius / Speed;
            quad.enabled = true;
            Debug.Log($"[EDITH scan] ping from {origin}");
        }

        void OnEnable() => Application.onBeforeRender += Invert;
        void OnDisable() => Application.onBeforeRender -= Invert;

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (quad != null) Destroy(quad.gameObject);
        }

        void Update()
        {
            if (quad == null || !quad.enabled) return;
            if (Time.time > busyUntil)
            {
                quad.enabled = false;
                return;
            }
            for (var i = 0; i < Shells; i++)
            {
                var age = Time.time - shells[i].Born;
                var radius = age < 0f ? -1f : age * Speed;
                data[i] = new Vector4(shells[i].Origin.x, shells[i].Origin.y, shells[i].Origin.z, radius > MaxRadius ? -1f : radius);
                bright[i] = new Vector4(shells[i].Brightness, 0f, 0f, 0f);
            }
            material.SetVectorArray(PingDataId, data);
            material.SetVectorArray(PingBrightId, bright);
        }

        /// The shader needs to go from environment depth back to the world, so it gets the inverse of Meta's
        /// world-to-depth matrices, which Meta sets just before each frame renders.
        void Invert()
        {
            if (quad == null || !quad.enabled) return;
            var forward = Shader.GetGlobalMatrixArray(ReprojectionId);
            if (forward == null || forward.Length < 2) return;
            for (var i = 0; i < 2; i++) inverse[i] = forward[i].inverse;
            Shader.SetGlobalMatrixArray(InverseId, inverse);
        }

        static Mesh Quad()
        {
            var mesh = new Mesh { name = "Scan ping quad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return mesh;
        }
    }
}
