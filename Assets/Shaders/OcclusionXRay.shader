// Draws only the parts of an object that are behind something already in the depth
// buffer, in a flat color. Used as an extra material on the player's mesh renderers
// (3D models and billboard quads) so the player stays visible through walls.
//
// _MainTex is used for its alpha only, so cut-out and transparent billboards produce a
// silhouette of the visible shape instead of the whole quad. PlayerOcclusion copies it
// from the base material when that material is alpha-tested or transparent.
//
// Queue Geometry+100 makes it draw after normal opaque geometry, so the depth test has
// something to compare against. No LightMode tag, so it renders in both the Built-in
// pipeline and URP (as SRPDefaultUnlit).
Shader "Custom/OcclusionXRay"
{
    Properties
    {
        _XRayColor ("X-Ray Color", Color) = (0.3, 0.6, 1.0, 0.75)
        _MainTex ("Shape Texture (alpha)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Name "XRay"
            ZTest Greater
            ZWrite Off
            Cull [_Cull]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _XRayColor;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a - _Cutoff);
                return _XRayColor;
            }
            ENDCG
        }
    }
}
