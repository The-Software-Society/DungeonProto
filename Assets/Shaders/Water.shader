Shader "AldenFall/Water"
{
    // Real-ish water: depth-based colour (clear/transparent in the shallows so the bed shows,
    // deepening to blue), gentle vertex swell, animated ripple normals driving a moving sun
    // sparkle + fresnel, and a foam line at the shore/shallows. Reads as ONE body of water,
    // not a flat blue lid. Needs URP Depth texture ON.
    Properties
    {
        _ShallowColor ("Shallow (clear)", Color) = (0.30, 0.55, 0.58, 0.20)
        _DeepColor    ("Deep (blue)",     Color) = (0.04, 0.20, 0.30, 0.90)
        _DepthFade    ("Depth fade (m)",  Float) = 3.5
        _FoamColor    ("Foam",            Color) = (0.92, 0.97, 1.0, 1.0)
        _FoamDist     ("Foam edge (m)",   Float) = 0.9
        _RippleScale  ("Ripple scale",    Float) = 0.5
        _RippleSpeed  ("Ripple speed",    Float) = 0.7
        _RippleStrength("Ripple strength",Float) = 0.35
        _WaveHeight   ("Wave height",     Float) = 0.06
        _WaveScale    ("Wave scale",      Float) = 0.10
        _SpecSharp    ("Sparkle sharpness",Range(8,600)) = 220
        _SpecGain     ("Sparkle gain",    Float) = 0.7
        _FresnelTint  ("Fresnel tint",    Color) = (0.55, 0.72, 0.85, 1)
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
                float4 _ShallowColor, _DeepColor, _FoamColor, _FresnelTint;
                float _DepthFade, _FoamDist, _RippleScale, _RippleSpeed, _RippleStrength;
                float _WaveHeight, _WaveScale, _SpecSharp, _SpecGain;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings {
                float4 positionHCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                float4 screenPos:TEXCOORD1;
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
                wp.y += WaveH(wp.xz, _Time.y * _RippleSpeed);
                OUT.positionWS = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfEye = IN.screenPos.w;
                float wd = sceneEye - surfEye;                    // water column depth (m)
                // Guard: if the depth texture is unavailable (wd nonsensical), fall back to a
                // mid-depth look so the water still reads as translucent blue - NEVER white foam.
                bool hasDepth = wd > 0.02 && wd < 4000.0;
                float waterCol = hasDepth ? saturate(wd / _DepthFade) : 0.5;   // 0 shallow -> 1 deep

                float t = _Time.y * _RippleSpeed;
                float3 rn = RippleNormal(IN.positionWS.xz, t);
                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);

                // Depth-driven colour + transparency: clear (see the bed) in the shallows, opaque
                // blue in the deep — the read that makes it a body of water, not a flat sheet.
                float3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, waterCol);
                float alpha = lerp(_ShallowColor.a, _DeepColor.a, waterCol);

                // Fresnel: bluer + more opaque at grazing angles, clearer looking straight down.
                float fres = pow(1.0 - saturate(dot(rn, viewDir)), 5.0);
                col = lerp(col, _FresnelTint.rgb, fres * 0.5);
                alpha = saturate(alpha + fres * 0.35);

                // Moving sun sparkle off the ripples.
                Light mainLight = GetMainLight();
                float3 hv = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(rn, hv)), _SpecSharp) * _SpecGain;
                col += mainLight.color.rgb * spec;
                alpha = saturate(alpha + spec);

                // Foam line ONLY where depth confirms a genuine shallow (never when depth is absent).
                float foam = hasDepth ? smoothstep(0.55, 1.0, 1.0 - saturate(wd / _FoamDist)) : 0.0;
                col = lerp(col, _FoamColor.rgb, foam);
                alpha = saturate(alpha + foam);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
