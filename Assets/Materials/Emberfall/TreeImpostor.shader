// Cylindrical billboard impostor for distant trees. The quad's origin sits at the tree BASE;
// it stays upright (world +Y) and yaw-rotates to face the camera, so a whole forest of these
// reads as real trees from any ground angle. The texture is RENDERED FROM THE REAL FBX by
// TreeImpostorBaker (two-pass alpha), so this is real geometry hazing into the fog -- the
// BDO / New World distance technique, NOT a cube/cone/card stand-in (see CLAUDE.md HARD RULE).
Shader "AldenFall/TreeImpostor"
{
    Properties
    {
        _BaseMap ("Impostor", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.4
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float4 _Tint;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                // Base pivot in world space (mesh verts: x in [-0.5,0.5], y in [0,1]).
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                // World-space size baked into the object-to-world columns (width = x, height = y).
                float sx = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float sy = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));
                // Cylindrical facing: upright, yaw toward the camera.
                float3 toCam = GetCameraPositionWS() - originWS;
                toCam.y = 0;
                float3 look = normalize(toCam + float3(0.0001, 0, 0.0001));
                float3 up = float3(0, 1, 0);
                float3 right = normalize(cross(up, look));
                float3 posWS = originWS + right * (IN.positionOS.x * sx) + up * (IN.positionOS.y * sy);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _Tint;
                clip(c.a - _Cutoff);
                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif
                c.rgb = MixFog(c.rgb, IN.fogFactor);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
