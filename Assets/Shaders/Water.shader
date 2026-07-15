Shader "AldenFall/Water"
{
    // CHEAP, MATTE, non-repeating stylized water. Self-contained (no scene depth). Designed to be
    // fill-rate friendly (few instructions/pixel) because water covers most of the screen when
    // swimming, and to AVOID the regular diagonal bands + pulsing specular "star" the old
    // 27-sin/high-spec version produced. Gentle detuned ripples, a broad soft sheen (no sharp
    // sparkle), translucent depth-ish tint via fresnel.
    Properties
    {
        _ShallowColor ("Shallow tint",   Color) = (0.26, 0.50, 0.55, 0.6)
        _DeepColor    ("Water body",     Color) = (0.09, 0.26, 0.36, 0.85)
        _FresnelTint  ("Sky reflection", Color) = (0.42, 0.58, 0.68, 1)
        _RippleSpeed  ("Ripple speed",    Float) = 0.5
        _RippleStrength("Ripple strength",Float) = 0.10
        _WaveHeight   ("Wave height",     Float) = 0.05
        _Sheen        ("Sheen gain",      Float) = 0.10
        _Opacity      ("Base opacity",    Range(0,1)) = 0.72
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
                float4 _ShallowColor, _DeepColor, _FresnelTint;
                float _RippleSpeed, _RippleStrength, _WaveHeight, _Sheen, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 positionWS:TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float t = _Time.y * _RippleSpeed;
                // Gentle non-harmonic swell (2 cheap sines, detuned so they don't beat uniformly).
                wp.y += (sin(wp.x * 0.09 + wp.z * 0.13 + t) + sin(wp.x * -0.07 + wp.z * 0.05 - t * 0.8)) * _WaveHeight;
                OUT.positionWS = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.positionWS.xz;
                float t = _Time.y * _RippleSpeed;
                // THREE detuned directional ripples (irrational-ish freqs/dirs -> long beat, no
                // regular grid). Cheap: 3 sins total for the surface normal.
                float a = sin(p.x *  0.113 + p.y *  0.071 + t);
                float b = sin(p.x * -0.052 + p.y *  0.131 - t * 0.83);
                float c = sin(p.x *  0.087 + p.y * -0.159 + t * 1.27);
                float2 slope = float2(a * 0.6 + c * 0.4, b * 0.6 - c * 0.4) * _RippleStrength;
                float3 n = normalize(float3(slope.x, 1.0, slope.y));

                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 4.0);

                float3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, fres * 0.35);
                col = lerp(col, _FresnelTint.rgb, fres * 0.45);          // gentle sky tint at grazing

                // BROAD, soft sheen instead of a sharp specular star (low power, low gain).
                Light L = GetMainLight();
                float3 h = normalize(L.direction + viewDir);
                float sheen = pow(saturate(dot(n, h)), 16.0) * _Sheen;
                col += L.color.rgb * sheen;

                float alpha = saturate(lerp(_Opacity, 0.9, fres));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
