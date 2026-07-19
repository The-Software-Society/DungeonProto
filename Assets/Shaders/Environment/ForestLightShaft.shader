Shader "AldenFall/Environment/ForestLightShaft"
{
    Properties { _Color("Shaft Color", Color) = (1,0.85,0.55,0.10) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Name "AtmosphericShaft"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Color; CBUFFER_END
            struct A { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half fog:TEXCOORD1; };
            V vert(A i) { V o; VertexPositionInputs p=GetVertexPositionInputs(i.positionOS); o.positionCS=p.positionCS; o.uv=i.uv; o.fog=ComputeFogFactor(p.positionCS.z); return o; }
            half4 frag(V i):SV_Target
            {
                half edge = pow(saturate(1.0h - abs(i.uv.x * 2.0h - 1.0h)), 1.7h);
                half ends = smoothstep(0.0h, 0.18h, i.uv.y) * (1.0h - smoothstep(0.72h, 1.0h, i.uv.y));
                half grain = 0.88h + 0.12h * sin(i.uv.y * 41.0h + i.uv.x * 17.0h);
                half alpha = edge * ends * grain * _Color.a;
                return half4(_Color.rgb, alpha * (1.0h - i.fog * 0.45h));
            }
            ENDHLSL
        }
    }
}
