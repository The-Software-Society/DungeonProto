Shader "AldenFall/Waterfall"
{
    // A stylized FALLING-water sheet: vertical foam veins scrolling downward over a top-bright /
    // base-deep gradient, with heavy white churn at the base. Reads as moving water even in a
    // still frame (the veins + base foam do the work a flat blue cube never could). Meant for a
    // vertical quad: UV.y = 1 at the lip (top), 0 at the plunge (base); UV.x across the width.
    Properties
    {
        _TopColor  ("Top (bright)",   Color) = (0.62, 0.78, 0.88, 0.92)
        _BotColor  ("Base (deep)",    Color) = (0.32, 0.52, 0.64, 0.96)
        _FoamColor ("Foam",           Color) = (0.95, 0.98, 1.0, 1.0)
        _Speed     ("Flow speed",     Float) = 1.6
        _Veins     ("Vein density",   Float) = 22.0
        _VeinSharp ("Vein sharpness", Float) = 2.2
        _BaseFoam  ("Base foam height",Float) = 0.30
        _Alpha     ("Base alpha",     Range(0,1)) = 0.9
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
                float4 _TopColor, _BotColor, _FoamColor;
                float _Speed, _Veins, _VeinSharp, _BaseFoam, _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; };

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float t = _Time.y * _Speed;
                float flow = uv.y * 3.0 + t;                          // scroll downward (uv.y=1 top)

                // Vertical foam veins: layered high-freq bands across the width, wobbled + scrolled
                // so they read as streaming water columns, denser/brighter toward the base.
                float vein = sin(uv.x * _Veins + sin(flow * 1.7) * 1.2)
                           + 0.6 * sin(uv.x * _Veins * 2.3 - flow * 0.8);
                vein = pow(saturate(vein * 0.5 + 0.5), _VeinSharp);
                // Speckle churn (broken white water) that intensifies toward the base.
                float sp = hash(floor(float2(uv.x * 26.0, flow * 4.0)));
                float speck = step(0.72, sp) * (1.0 - uv.y);

                // Top-bright -> base-deep gradient (accelerating water darkens then froths white).
                float3 water = lerp(_BotColor.rgb, _TopColor.rgb, uv.y);
                float baseFoam = smoothstep(_BaseFoam, 0.0, uv.y);   // heavy froth at the plunge
                float lipFoam  = smoothstep(0.92, 1.0, uv.y);        // white cap at the lip
                float foam = saturate(vein * 0.55 + baseFoam * 0.9 + lipFoam * 0.7 + speck * 0.5);

                float3 col = lerp(water, _FoamColor.rgb, foam);
                float alpha = lerp(_Alpha, 1.0, saturate(foam));
                // Soften the vertical side edges so the sheet doesn't read as a hard-edged card.
                alpha *= smoothstep(0.0, 0.06, uv.x) * smoothstep(1.0, 0.94, uv.x);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
