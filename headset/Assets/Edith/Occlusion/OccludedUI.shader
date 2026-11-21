// Unity's UI shader plus Meta's environment-depth occlusion, so a real object in front of a snapshot hides the photo
// and text behind it. Premultiplied output, because Meta's occlusion macro scales colour and alpha together.
Shader "Edith/OccludedUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _EnvironmentDepthBias ("Environment Depth Bias", Float) = 0.06
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

            #include "UnityCG.cginc"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                META_DEPTH_VERTEX_OUTPUT(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TextureSampleAdd;
            float _EnvironmentDepthBias;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(o, v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 c = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                c.rgb *= c.a;
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(i, c, _EnvironmentDepthBias);
                return c;
            }
            ENDCG
        }
    }
}
