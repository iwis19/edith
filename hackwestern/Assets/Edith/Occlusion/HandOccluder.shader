// Draws the tracked hand mesh into depth only, before any UI, so snapshots behind the hand are not drawn there and the
// real hand shows through passthrough with a sharp edge. The mesh is pushed out along its normals to cover the gap
// between the tracked mesh and the real hand.
Shader "Edith/HandOccluder"
{
    Properties
    {
        _Inflate ("Inflate (m)", Float) = 0.004
    }

    SubShader
    {
        Tags { "Queue"="Geometry-10" "RenderType"="Opaque" }

        ZWrite On
        ZTest LEqual
        ColorMask 0
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            float _Inflate;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz + UnityObjectToWorldNormal(v.normal) * _Inflate;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
