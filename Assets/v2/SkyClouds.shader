Shader "Building/SkyClouds"
{
    // Ciel 2D procédural : dégradé vertical + nuages (bruit fractal) qui défilent.
    // Aucune texture. Pour URP uniquement. À afficher sur un quad enfant de la caméra (voir SkyBackground).
    Properties
    {
        _TopColor ("Top Color", Color) = (0.25, 0.55, 0.95, 1)
        _HorizonColor ("Horizon Color", Color) = (0.78, 0.90, 1, 1)
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudShadeColor ("Cloud Shade Color", Color) = (0.78, 0.84, 0.93, 1)
        _CloudScale ("Cloud Scale", Float) = 0.08
        _CloudSpeed ("Cloud Speed (x, y)", Vector) = (0.4, 0, 0, 0)
        _Coverage ("Coverage", Range(0, 1)) = 0.5
        _Softness ("Softness", Range(0.01, 0.5)) = 0.2
        _Offset ("Offset (parallax)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _HorizonColor;
                float4 _CloudColor;
                float4 _CloudShadeColor;
                float  _CloudScale;
                float4 _CloudSpeed;
                float  _Coverage;
                float  _Softness;
                float4 _Offset;
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
                float2 screenPos  : TEXCOORD1; // position en unités monde, alignée sur l'écran
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;

                // Échelle du quad (enfant de la caméra) : donne des coordonnées en unités monde
                // qui ne tournent pas avec la rotation de la vue.
                float2 scale = float2(length(unity_ObjectToWorld._m00_m10_m20),
                                      length(unity_ObjectToWorld._m01_m11_m21));
                OUT.screenPos = IN.positionOS.xy * scale;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * valueNoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Ciel : horizon clair en bas, bleu plus profond en haut
                half3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, smoothstep(0.0, 1.0, IN.uv.y));

                // Nuages
                float2 p = (IN.screenPos + _Offset.xy + _CloudSpeed.xy * _Time.y) * _CloudScale;
                p.y *= 1.8; // nuages plus larges que hauts
                float n = fbm(p);

                float threshold = lerp(0.75, 0.35, _Coverage);
                float cloudMask = smoothstep(threshold, threshold + _Softness, n);
                float body = saturate((n - threshold) / 0.25);
                half3 cloud = lerp(_CloudShadeColor.rgb, _CloudColor.rgb, body);

                return half4(lerp(sky, cloud, cloudMask), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
