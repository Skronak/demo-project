Shader "Building/SpriteSheetURP"
{
    // Sprite 2D avec transparence (alpha) et animation par feuille de sprites.
    // Toutes les instances partageant le même matériau affichent la même image :
    // l'animation est calculée sur le GPU, sans aucun coût CPU.
    // Les images de la feuille sont lues de gauche à droite, puis de haut en bas.
    // Pour URP uniquement.
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1, 1, 1, 1)
        _Columns ("Columns", Float) = 1
        _Rows ("Rows", Float) = 1
        _FrameCount ("Frame Count", Float) = 1
        _Fps ("Frames Per Second", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Sprite"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Columns;
                float _Rows;
                float _FrameCount;
                float _Fps;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float cols = max(1.0, _Columns);
                float rows = max(1.0, _Rows);
                float frames = max(1.0, _FrameCount);

                float frame = fmod(floor(_Time.y * _Fps), frames);
                float col = fmod(frame, cols);
                float row = floor(frame / cols);

                // Ligne 0 = haut de la feuille (v = 1 en haut de l'image).
                float2 uv = (IN.uv + float2(col, rows - 1.0 - row)) / float2(cols, rows);

                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * _Color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
