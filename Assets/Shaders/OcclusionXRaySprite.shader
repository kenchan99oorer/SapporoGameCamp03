// Sprite version of OcclusionXRay. Used by PlayerOcclusion on a mirror SpriteRenderer
// that copies the player's sprite each frame. Built on UnitySprites.cginc, so sprite
// flipping, atlases and the renderer color work as with Sprites/Default. The x-ray
// color comes from the mirror's SpriteRenderer.color, set by PlayerOcclusion.
Shader "Custom/OcclusionXRaySprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.1
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry+100"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        ZTest Greater
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "XRay"

            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed _Cutoff;

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed a = SampleSpriteTexture(IN.texcoord).a;
                clip(a - _Cutoff);
                fixed4 c = IN.color;
                c.a *= a;
                return c;
            }
            ENDCG
        }
    }
}
