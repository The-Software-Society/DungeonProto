Shader "AldenFall/Environment/ForestTerrain"
{
    Properties
    {
        _DirtMap("Dirt Albedo", 2D) = "white" {}
        _DirtNormal("Dirt Normal", 2D) = "bump" {}
        _GrassMap("Grass Soil Albedo", 2D) = "white" {}
        _GrassNormal("Grass Soil Normal", 2D) = "bump" {}
        _LeafMap("Leaf Litter Albedo", 2D) = "white" {}
        _LeafNormal("Leaf Litter Normal", 2D) = "bump" {}
        _ControlMap("RGB Control: Grass, Leaf, Dirt", 2D) = "black" {}
        _Tiling("Physical Tiling", Float) = 6
        _NormalStrength("Normal Strength", Range(0,2)) = 1
        _MacroTint("Macro Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DirtMap); SAMPLER(sampler_DirtMap);
            TEXTURE2D(_DirtNormal); SAMPLER(sampler_DirtNormal);
            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassNormal); SAMPLER(sampler_GrassNormal);
            TEXTURE2D(_LeafMap); SAMPLER(sampler_LeafMap);
            TEXTURE2D(_LeafNormal); SAMPLER(sampler_LeafNormal);
            TEXTURE2D(_ControlMap); SAMPLER(sampler_ControlMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MacroTint;
                float _Tiling;
                float _NormalStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv = input.uv; o.shadowCoord = GetShadowCoord(p);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 weights = SAMPLE_TEXTURE2D(_ControlMap, sampler_ControlMap, i.uv).rgb;
                weights = max(weights, 0.001h); weights /= dot(weights, 1.0h);
                float2 detailUV = i.positionWS.xz * (_Tiling / 12.0);
                half3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, detailUV).rgb;
                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, detailUV * 1.07).rgb;
                half3 leaf = SAMPLE_TEXTURE2D(_LeafMap, sampler_LeafMap, detailUV * 0.93).rgb;
                half3 albedo = (grass * weights.r + leaf * weights.g + dirt * weights.b) * _MacroTint.rgb;

                half3 nd = UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal, sampler_DirtNormal, detailUV), _NormalStrength);
                half3 ng = UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, detailUV * 1.07), _NormalStrength);
                half3 nl = UnpackNormalScale(SAMPLE_TEXTURE2D(_LeafNormal, sampler_LeafNormal, detailUV * 0.93), _NormalStrength);
                half3 normalTS = normalize(ng * weights.r + nl * weights.g + nd * weights.b);
                half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS));
                normalWS = NormalizeNormalPerPixel(normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS; inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = i.shadowCoord; inputData.fogCoord = i.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.bakedGI = SampleSH(normalWS);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo; surface.alpha = 1; surface.metallic = 0;
                surface.specular = 0; surface.smoothness = dot(weights, half3(0.05, 0.025, 0.08));
                surface.normalTS = normalTS; surface.occlusion = 1;
                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
