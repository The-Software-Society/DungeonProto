Shader "AldenFall/FoamPatch"
{
    // Animated white-water froth for flat quads (waterfall plunge churn, lip line, downstream
    // trails). Procedural (texture-free): two scrolling value-noise octaves masked by a soft
    // radial/edge falloff so the quad reads as churning foam, never as a card.
    Properties
    {
        _FoamColor ("Foam color", Color) = (0.93, 0.96, 0.98, 1)
        _Churn     ("Churn speed", Float) = 1.0
        _Density   ("Foam density", Range(0,1)) = 0.65
        _Stretch   ("Streak stretch (1 = round patch)", Float) = 1.0
        _EdgeSoft  ("Edge falloff softness", Range(0.05,1)) = 0.45
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

            CBUFFER_START(UnityPerMaterial)
                float4 _FoamColor;
                float _Churn, _Density, _Stretch, _EdgeSoft;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; };

            float hash21(float2 p) { p = frac(p * float2(123.34, 345.45)); p += dot(p, p + 34.345); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _Churn;
                float2 nuv = IN.uv * float2(6.0 / max(_Stretch, 0.001), 6.0);
                float n = vnoise(nuv + float2(0, -t * 1.3)) * 0.65
                        + vnoise(nuv * 2.7 + float2(t * 0.4, -t * 2.2)) * 0.35;
                // Soft falloff to the quad edges (elliptical) -- no visible card outline.
                float2 d = abs(IN.uv - 0.5) * 2.0;
                float edge = 1.0 - smoothstep(1.0 - _EdgeSoft, 1.0, length(d));
                float a = smoothstep(1.0 - _Density, 1.0, n) * edge;
                return half4(_FoamColor.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
