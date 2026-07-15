Shader "AldenFall/Water"
{
    // SELF-CONTAINED stylized water: procedural surface swell (vertex waves) + animated ripple
    // normals driving a moving sun sparkle and a fresnel sky-reflection, semi-transparent so you
    // see into it. Deliberately does NOT sample the scene depth texture - depth-based water is
    // fragile in player builds (variant stripping / depth-prepass config) and was rendering FLAT
    // with no ripples in the shipped build. This version ripples + reads as translucent water in
    // ANY build, editor or standalone.
    Properties
    {
        _ShallowColor ("Shallow tint",   Color) = (0.30, 0.55, 0.58, 0.6)
        _DeepColor    ("Water body",     Color) = (0.10, 0.30, 0.42, 0.85)
        _FresnelTint  ("Sky reflection", Color) = (0.58, 0.74, 0.86, 1)
        _FoamColor    ("Crest foam",     Color) = (0.92, 0.97, 1.0, 1.0)
        _RippleScale  ("Ripple scale",    Float) = 0.5
        _RippleSpeed  ("Ripple speed",    Float) = 0.7
        _RippleStrength("Ripple strength",Float) = 0.5
        _WaveHeight   ("Wave height",     Float) = 0.10
        _WaveScale    ("Wave scale",      Float) = 0.12
        _SpecSharp    ("Sparkle sharpness",Range(8,600)) = 200
        _SpecGain     ("Sparkle gain",    Float) = 0.8
        _Opacity      ("Base opacity",    Range(0,1)) = 0.62
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _FresnelTint, _FoamColor;
                float _RippleScale, _RippleSpeed, _RippleStrength;
                float _WaveHeight, _WaveScale, _SpecSharp, _SpecGain, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings {
                float4 positionHCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                float  wave:TEXCOORD1;
            };

            float WaveH(float2 p, float t)
            {
                return sin(p.x * _WaveScale + t) * cos(p.y * _WaveScale * 0.8 + t * 1.1) * _WaveHeight
                     + sin((p.x + p.y) * _WaveScale * 1.7 + t * 1.6) * _WaveHeight * 0.4;
            }

            // Procedural ripple normal from a sum of travelling sines (animated by _Time).
            float3 RippleNormal(float2 p, float t)
            {
                float2 q = p * _RippleScale;
                float e = 0.2;
                float h0 = sin(q.x + t) + sin(q.y * 1.3 - t * 1.2) + sin((q.x + q.y) * 0.7 + t * 0.6);
                float hx = sin(q.x + e + t) + sin(q.y * 1.3 - t * 1.2) + sin((q.x + e + q.y) * 0.7 + t * 0.6);
                float hy = sin(q.x + t) + sin((q.y + e) * 1.3 - t * 1.2) + sin((q.x + q.y + e) * 0.7 + t * 0.6);
                return normalize(float3((h0 - hx) * _RippleStrength, 1.0, (h0 - hy) * _RippleStrength));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float w = WaveH(wp.xz, _Time.y * _RippleSpeed);
                wp.y += w;
                OUT.positionWS = wp;
                OUT.wave = w;
                OUT.positionHCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _RippleSpeed;
                float3 rn = RippleNormal(IN.positionWS.xz, t);
                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);

                // Fresnel: look straight down -> see the water body; grazing -> sky reflection.
                float fres = pow(1.0 - saturate(dot(rn, viewDir)), 4.0);
                float3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(rn.y * 0.0 + fres * 0.35));
                col = lerp(col, _FresnelTint.rgb, fres * 0.55);

                // Moving sun sparkle off the rippled surface.
                Light mainLight = GetMainLight();
                float3 hv = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(rn, hv)), _SpecSharp) * _SpecGain;
                col += mainLight.color.rgb * spec;

                // Subtle white foam on the highest wave crests (no depth needed).
                float crest = smoothstep(_WaveHeight * 0.55, _WaveHeight * 1.1, IN.wave);
                col = lerp(col, _FoamColor.rgb, crest * 0.35);

                float alpha = saturate(lerp(_Opacity, 0.95, fres) + spec + crest * 0.3);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
