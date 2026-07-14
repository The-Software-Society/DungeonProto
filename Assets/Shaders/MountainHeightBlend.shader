Shader "AldenFall/MountainHeightBlend"
{
    // Blends colour by WORLD HEIGHT so every mountain reads as green lower slopes -> dirt/rock
    // -> snow-capped peak (not one flat colour). Triplanar rock texture gives surface detail
    // regardless of the mesh UVs.
    Properties
    {
        _RockTex ("Rock texture", 2D) = "white" {}
        _Tile ("Triplanar tile", Float) = 0.06
        _GreenColor ("Green (low, flat)", Color) = (0.20,0.30,0.15,1)
        _DirtColor ("Dirt (low, steep)", Color) = (0.32,0.26,0.18,1)
        _RockColor ("Rock (mid)", Color) = (0.46,0.43,0.39,1)
        _SnowColor ("Snow (high, flat)", Color) = (0.92,0.92,0.95,1)
        _GreenTop ("Green top Y", Float) = 24
        _RockTop ("Snow start Y", Float) = 105
        _Band ("Blend band", Float) = 16
        _TexBoost ("Texture boost", Float) = 1.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_RockTex); SAMPLER(sampler_RockTex);
            CBUFFER_START(UnityPerMaterial)
                float _Tile; float4 _GreenColor; float4 _DirtColor; float4 _RockColor; float4 _SnowColor;
                float _GreenTop; float _RockTop; float _Band; float _TexBoost;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float3 Triplanar(float3 wp, float3 wn)
            {
                float3 bw = pow(abs(wn), 4.0);
                bw /= max(bw.x + bw.y + bw.z, 1e-4);
                float3 cx = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, wp.zy * _Tile).rgb;
                float3 cy = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, wp.xz * _Tile).rgb;
                float3 cz = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, wp.xy * _Tile).rgb;
                return cx * bw.x + cy * bw.y + cz * bw.z;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wn = normalize(IN.normalWS);
                float slope = 1.0 - saturate(wn.y);                 // 0 flat, 1 vertical
                float detail = Triplanar(IN.positionWS, wn).r;      // 0..~0.5 rock luminance
                float y = IN.positionWS.y;

                float toRock = smoothstep(_GreenTop - _Band, _GreenTop + _Band, y);
                float toSnow = smoothstep(_RockTop - _Band, _RockTop + _Band, y);
                // Lower band: green on gentle slopes, dirt where steep.
                float3 low = lerp(_GreenColor.rgb, _DirtColor.rgb, saturate(slope * 1.7));
                float3 col = lerp(low, _RockColor.rgb, toRock);
                // Snow caps the high ground; thins on the very steepest faces but still shows.
                col = lerp(col, _SnowColor.rgb, toSnow * saturate(1.35 - slope * 0.7));
                // Rock texture as light/dark surface DETAIL around 1.0 (never darkens to black).
                col *= (0.82 + detail * _TexBoost);

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(wn, mainLight.direction)) * 0.7 + 0.3;
                float3 ambient = SampleSH(wn) + 0.18;
                float3 lit = col * (mainLight.color.rgb * ndl + ambient);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
