// The scan's ping: shells of light that grow out from the person and light up real surfaces as they pass. Drawn on a
// quad in front of the eye. Each pixel's view ray is reprojected into Meta's environment depth to find the real
// surface behind it, and the surface glows in a thin band wherever its distance from a shell's origin matches the
// shell's radius, with a trail fading behind the front. Additive, so it only ever adds light to the room.
Shader "Edith/ScanPing"
{
    Properties
    {
        _PingColor ("Colour", Color) = (0.75, 0.91, 1, 1)
        _Band ("Front sharpness (m)", Float) = 0.12
        _Trail ("Trail length (m)", Float) = 0.9
        _Falloff ("Trail falloff", Float) = 2
        _MaxRadius ("Fade-out radius (m)", Float) = 4.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent-50" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

            #define MAX_PINGS 3

            // World position from environment-depth clip space, one per eye; ScanPing inverts Meta's matrices.
            float4x4 _EdithDepthInverse[2];
            // xyz origin, w radius in metres; a negative radius is an empty slot.
            float4 _PingData[MAX_PINGS];
            // x is the shell's brightness.
            float4 _PingBright[MAX_PINGS];
            float4 _PingColor;
            float _Band, _Trail, _Falloff, _MaxRadius;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 world : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // A point a metre along this pixel's view ray, carried into the depth camera's view.
                float3 eye = _WorldSpaceCameraPos;
                float3 probe = eye + normalize(i.world - eye);
                float4 depthSpace = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(probe, 1.0));
                float2 uv = depthSpace.xy / depthSpace.w * 0.5 + 0.5;
                if (uv.x <= 0.0 || uv.y <= 0.0 || uv.x >= 1.0 || uv.y >= 1.0) return 0;
                float raw = SampleEnvironmentDepth(uv);
                if (raw >= 0.9999) return 0;

                // The real surface there, back in world space.
                float4 surface = mul(_EdithDepthInverse[unity_StereoEyeIndex], float4(uv * 2.0 - 1.0, raw * 2.0 - 1.0, 1.0));
                float3 world = surface.xyz / surface.w;

                float glow = 0.0;
                [unroll]
                for (int p = 0; p < MAX_PINGS; p++)
                {
                    float radius = _PingData[p].w;
                    if (radius < 0.0) continue;
                    // Positive behind the front, negative ahead of it.
                    float behind = radius - distance(world, _PingData[p].xyz);
                    float front = behind < 0.0 ? saturate(1.0 + behind / _Band) : 1.0;
                    float trail = behind < 0.0 ? 1.0 : pow(saturate(1.0 - behind / _Trail), _Falloff);
                    float fade = 1.0 - saturate(radius / _MaxRadius);
                    glow += front * trail * fade * _PingBright[p].x;
                }
                return fixed4(_PingColor.rgb * saturate(glow) * _PingColor.a, 0);
            }
            ENDCG
        }
    }
}
