Shader "AldenFall/Water"
{
    // v2 "BDO bar" water (Jon refs 1-5): dense detail-normal sparkle, TRUE clarity (scene-color
    // refraction + per-channel absorption so the bed is visible through shallows), and LAYERED
    // breathing shore foam (thick contact sheet -> marbled wash -> flecks) from a generated mask.
    // Needs: EmberfallURP.asset m_RequireDepthTexture=1 AND m_RequireOpaqueTexture=1, plus
    // Assets/Textures/Water/{WaterNormal,FoamMask}.png (WaterTextureGenerator). Normal xy slopes
    // live in RG decoded manually (rg*2-1) -- no NormalMap importer/compression surprises in builds.
    Properties
    {
        _NormalTex   ("Ripple normal (RG slopes)", 2D) = "bump" {}
        _FoamTex     ("Foam mask (R sheet, G streaks, B flecks)", 2D) = "black" {}
        _ShallowColor ("Shallow scatter", Color) = (0.33, 0.58, 0.56, 1)
        _DeepColor    ("Deep scatter",    Color) = (0.09, 0.27, 0.32, 1)
        _Absorb       ("Absorption per meter (RGB)", Vector) = (0.42, 0.18, 0.13, 0)
        _FresnelTint  ("Sky reflection", Color) = (0.56, 0.66, 0.72, 1)
        _FoamColor    ("Foam color",     Color) = (0.94, 0.97, 0.98, 1)
        _RefractStrength ("Refraction distortion", Float) = 0.35
        _DetailStrength  ("Detail normal strength", Float) = 0.85
        _DetailScale     ("Detail normal scale (m per tile)", Float) = 5.5
        _FoamScale       ("Foam mask scale (m per tile)", Float) = 7.0
        _RippleSpeed  ("Ripple speed",    Float) = 0.55
        _WaveHeight   ("Wave height",     Float) = 0.06
        _Sparkle      ("Sun glint gain",  Float) = 0.9
        _FlowDir      ("Flow direction (xy) + speed (z)", Vector) = (0.7, 0.7, 0.045, 0)
        _FoamZones    ("Foam zone depths m (contact, wash, flecks)", Vector) = (0.18, 0.7, 1.5, 0)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_FoamTex);   SAMPLER(sampler_FoamTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _Absorb, _FresnelTint, _FoamColor, _FlowDir, _FoamZones;
                float _RefractStrength, _DetailStrength, _DetailScale, _FoamScale;
                float _RippleSpeed, _WaveHeight, _Sparkle;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings
            {
                float4 positionHCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                float4 screenPos:TEXCOORD1;
            };

            float2 SwellSlope(float2 p, float t, out float height)
            {
                // Broad lake swell (fragment-evaluated so coarse meshes still get crest variation).
                float s1 = sin(p.x * 0.085 + p.y * 0.122 + t);
                float s2 = sin(p.x * -0.067 + p.y * 0.049 - t * 0.8);
                height = (s1 + s2) * 0.5;                       // -1..1
                float c1 = cos(p.x * 0.085 + p.y * 0.122 + t);
                float c2 = cos(p.x * -0.067 + p.y * 0.049 - t * 0.8);
                return float2(c1 * 0.085 - c2 * 0.067, c1 * 0.122 + c2 * 0.049) * _WaveHeight * 6.0;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float t = _Time.y * _RippleSpeed;
                float h;
                SwellSlope(wp.xz, t, h);
                wp.y += h * _WaveHeight * 8.0;                  // real displacement (mesh is gridded)
                OUT.positionWS = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _RippleSpeed;
                float2 wpos = IN.positionWS.xz;
                float2 flow = _FlowDir.xy * (_Time.y * _FlowDir.z);

                // --- SURFACE NORMAL: two scrolled detail-normal taps + broad swell slope. ---
                float2 uvA = wpos / _DetailScale + flow + float2(t, t * 0.62) * 0.018;
                float2 uvB = wpos / (_DetailScale * 0.31) - flow * 1.7 + float2(-t * 0.031, t * 0.024);
                float2 dnA = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvA).rg * 2.0 - 1.0;
                float2 dnB = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvB).rg * 2.0 - 1.0;
                float swellH;
                float2 swell = SwellSlope(wpos, t, swellH);
                float2 slope = (dnA + dnB * 0.65) * _DetailStrength + swell;
                float3 n = normalize(float3(slope.x, 1.0, slope.y));

                // --- WATER DEPTH along the eye ray (straight tap). ---
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surfEye = IN.screenPos.w;
                float waterDepth = max(sceneEye - surfEye, 0.0);

                // --- REFRACTED BOTTOM: distort by the normal, guard against pulling foreground. ---
                float2 refrUV = uv + n.xz * _RefractStrength * saturate(2.0 / (1.0 + surfEye * 0.15))
                                * saturate(1.2 - waterDepth * 0.18) * 0.05;
                float refrEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                if (refrEye < surfEye) { refrUV = uv; refrEye = sceneEye; }
                float refrDepth = max(refrEye - surfEye, 0.0);
                float3 bottom = SampleSceneColor(refrUV);

                // --- CLARITY: per-channel absorption of the bed + depth scatter (BDO-clear). ---
                float3 absorb = exp(-refrDepth * _Absorb.rgb * 3.0);
                float shore = 1.0 - exp(-refrDepth * 0.55);      // 0 shore -> 1 deep
                float3 scatter = lerp(_ShallowColor.rgb, _DeepColor.rgb, shore);
                float3 col = bottom * absorb + scatter * (1.0 - absorb);

                // --- SKY + SUN: fresnel tint and dense sparkle off the detail normals. ---
                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                col = lerp(col, _FresnelTint.rgb, fres * 0.35);
                Light L = GetMainLight();
                float3 h = normalize(L.direction + viewDir);
                float glint = pow(saturate(dot(n, h)), 220.0) * _Sparkle
                            + pow(saturate(dot(n, h)), 48.0) * _Sparkle * 0.12;
                col += L.color.rgb * min(glint, 1.2);

                // --- LAYERED SHORE FOAM (ref 5): contact sheet -> marbled wash -> flecks, all
                // breathing in/out and edge-scalloped by the mask so the line is never uniform. ---
                float2 fuv = wpos / _FoamScale;
                float3 foamTex = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv + flow * 2.0).rgb;
                float3 foamTex2 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv * 2.7 - flow * 3.0 + t * 0.015).rgb;
                float breathe = sin(t * 0.9 + foamTex.g * 6.283) * 0.30 + 1.0;    // wash in/out
                float dContact = _FoamZones.x * breathe;
                float dWash    = _FoamZones.y * breathe;
                float dFleck   = _FoamZones.z;
                float scallop = (foamTex.r - 0.5) * 0.35;                          // ragged zone edges
                float dd = waterDepth + scallop;
                float contact = (1.0 - smoothstep(0.0, dContact, dd));
                float wash    = (1.0 - smoothstep(dContact, dWash, dd)) * smoothstep(0.0, dContact * 0.6, dd);
                float fleck   = (1.0 - smoothstep(dWash, dFleck, dd)) * smoothstep(dContact, dWash, dd);
                float foam = saturate(
                      contact * saturate(foamTex.r * 1.6 + 0.35)
                    + wash    * foamTex.r  * 1.1
                    + fleck   * foamTex2.b * 0.9);
                // Crest foam: RARE marbled caps on only the highest swell tops -- a calm lake is
                // not ref-3 surf; a low threshold here floods the lake with white continents.
                foam = saturate(foam + saturate((swellH - 0.88) * 2.5) * foamTex.r * 0.4);
                col = lerp(col, _FoamColor.rgb, foam);

                // --- ALPHA: we composite the bed ourselves, so the surface is near-solid; only the
                // last few cm feather out so the waterline melts into the sand under the foam. ---
                float alpha = saturate(smoothstep(0.0, 0.06, waterDepth) * 0.985 + foam);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
