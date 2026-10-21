// Wheat blades swaying in the wind. uv.y = height along the blade (0 base, 1 tip), uv.x = random phase.
Shader "Akropolis/Wheat"
{
    Properties
    {
        _Base ("Base", Color) = (0.62, 0.46, 0.14, 1)
        _Tip ("Tip", Color) = (0.98, 0.84, 0.38, 1)
        _WindStrength ("Wind strength", Float) = 0.05
        _WindSpeed ("Wind speed", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Base, _Tip;
                float _WindStrength, _WindSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float h = v.uv.y;
                float gust = sin(_Time.y * 0.7 + ws.x * 0.6 + ws.z * 0.4) * 0.5 + 0.5;
                float sway = sin(_Time.y * _WindSpeed + v.uv.x * 6.2832 + ws.x * 1.5 + ws.z * 1.1) * (0.35 + 0.65 * gust);
                ws.xz += float2(1.0, 0.4) * sway * _WindStrength * h * h;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 col = lerp(_Base.rgb, _Tip.rgb, i.uv.y);
                Light light = GetMainLight();
                float ndl = saturate(dot(float3(0, 1, 0), light.direction));
                col *= 0.62 + 0.38 * ndl * light.color + SampleSH(float3(0, 1, 0)) * 0.25;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
