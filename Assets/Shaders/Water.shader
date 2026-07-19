Shader "AldenFall/Water"
{
    // Real depth-fade water (Jon refs: clear shallows, visible depth gradient, moving surface,
    // white foam where water meets land). Samples _CameraDepthTexture -- EmberfallURP.asset MUST
    // keep m_RequireDepthTexture=1 or builds render without shore fade (the old flat-water-in-itch
    // bug was exactly this: editor scene view forces a depth texture, players don't).
    // Still texture-free/self-contained: ripples, sparkle and foam are procedural, so the shader
    // cannot regress from a missing texture reference in a build.
    Properties
    {
        _ShallowColor ("Shallow tint",   Color) = (0.31, 0.55, 0.54, 1)
        _DeepColor    ("Water body",     Color) = (0.05, 0.22, 0.33, 1)
        _FresnelTint  ("Sky reflection", Color) = (0.46, 0.60, 0.70, 1)
        _FoamColor    ("Foam color",     Color) = (0.92, 0.96, 0.97, 1)
        _ShoreDepth   ("Shore fade depth (m)",  Float) = 2.2
        _ShoreAlpha   ("Shallow opacity", Range(0,1)) = 0.25
        _DeepAlpha    ("Deep opacity",    Range(0,1)) = 0.93
        _FoamWidth    ("Foam band depth (m)",   Float) = 0.55
        _RippleSpeed  ("Ripple speed",    Float) = 0.55
        _RippleStrength("Ripple strength",Float) = 0.14
        _WaveHeight   ("Wave height",     Float) = 0.07
        _Sparkle      ("Sun glint gain",  Float) = 0.35
        _FlowDir      ("Flow direction (xy) + speed (z)", Vector) = (0.7, 0.7, 0.04, 0)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _FresnelTint, _FoamColor, _FlowDir;
                float _ShoreDepth, _ShoreAlpha, _DeepAlpha, _FoamWidth;
                float _RippleSpeed, _RippleStrength, _WaveHeight, _Sparkle;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings
            {
                float4 positionHCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                float4 screenPos:TEXCOORD1;
                float swell:TEXCOORD2;
            };

            // Cheap value-noise for foam breakup (no texture; 2 hash lattices, smooth interp).
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
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float t = _Time.y * _RippleSpeed;
                // Two detuned swells -- gentle, non-repeating motion; pass the crest factor for foam.
                float s = sin(wp.x * 0.09 + wp.z * 0.13 + t) + sin(wp.x * -0.07 + wp.z * 0.05 - t * 0.8);
                wp.y += s * _WaveHeight;
                OUT.swell = s * 0.5;                    // -1..1
                OUT.positionWS = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 flow = _FlowDir.xy * (_Time.y * _FlowDir.z);
                float2 p = IN.positionWS.xz + flow * 8.0;
                float t = _Time.y * _RippleSpeed;

                // Surface normal: 3 broad detuned ripples + 2 fine ones for glint (all procedural).
                float a = sin(p.x *  0.113 + p.y *  0.071 + t);
                float b = sin(p.x * -0.052 + p.y *  0.131 - t * 0.83);
                float c = sin(p.x *  0.087 + p.y * -0.159 + t * 1.27);
                float f1 = sin(p.x *  0.83  + p.y *  0.61  + t * 2.1);
                float f2 = sin(p.x * -0.71  + p.y *  0.95  - t * 1.7);
                float2 slope = float2(a * 0.6 + c * 0.4 + f1 * 0.18, b * 0.6 - c * 0.4 + f2 * 0.18) * _RippleStrength;
                float3 n = normalize(float3(slope.x, 1.0, slope.y));

                // TRUE DEPTH FADE: how much water the eye ray passes through before hitting ground.
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surfEye = IN.screenPos.w;
                float waterDepth = max(sceneEye - surfEye, 0.0);
                float shore = saturate(waterDepth / max(_ShoreDepth, 0.01));   // 0 at edge -> 1 deep

                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 4.0);

                // Clear shallows -> deep body, sky tint at grazing angles.
                float3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, shore);
                col = lerp(col, _FresnelTint.rgb, fres * 0.5);

                // Sun glint: tight-ish but clamped so it never forms the old pulsing star.
                Light L = GetMainLight();
                float3 h = normalize(L.direction + viewDir);
                float glint = pow(saturate(dot(n, h)), 64.0);
                col += L.color.rgb * min(glint * _Sparkle, 0.35);

                // SHORE FOAM: animated noisy band where the water meets ground, plus faint crest foam.
                float foamBand = 1.0 - saturate(waterDepth / max(_FoamWidth, 0.01));
                float foamN = vnoise(IN.positionWS.xz * 2.3 + flow * 14.0 + t * 0.35)
                            * vnoise(IN.positionWS.xz * 5.1 - t * 0.22);
                float foam = smoothstep(0.18, 0.75, foamBand * (0.55 + foamN))
                           + saturate(IN.swell - 0.72) * foamN * 0.8;
                col = lerp(col, _FoamColor.rgb, saturate(foam));

                // Shallows are genuinely transparent (you see the bottom); deep water reads solid.
                float alpha = lerp(_ShoreAlpha, _DeepAlpha, shore);
                alpha = saturate(alpha + fres * 0.25 + foam * 0.7);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
