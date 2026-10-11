// Draws only the parts of an object that are behind something already in the depth
// buffer, in a flat color. Used as an extra material on the player so it stays visible
// through walls.
//
// Queue Geometry+100 makes it draw after normal opaque geometry (walls, the player's own
// lit pass), so the depth test has something to compare against. No LightMode tag, so it
// renders in both the Built-in pipeline and URP (as SRPDefaultUnlit).
Shader "Custom/OcclusionXRay"
{
    Properties
    {
        _XRayColor ("X-Ray Color", Color) = (0.3, 0.6, 1.0, 0.75)
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Name "XRay"
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _XRayColor;

            struct appdata
            {
                float4 vertex : POSITION;
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
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _XRayColor;
            }
            ENDCG
        }
    }
}
