// Tree canopies and bushes: flat-shaded lit blobs whose tops lean with the same wind as the wheat.
Shader "Akropolis/Foliage"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.62, 0.3, 1)
        _Sway ("Sway", Float) = 0.035
        _Speed ("Speed", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Sway, _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
                float h = saturate(v.positionOS.y + 0.5);
                float gust = sin(_Time.y * 0.7 + pivot.x * 0.6 + pivot.z * 0.4) * 0.5 + 0.5;
                float sway = sin(_Time.y * _Speed + pivot.x * 0.9 + pivot.z * 0.7) * (0.4 + 0.6 * gust);
                ws.xz += float2(1.0, 0.4) * sway * _Sway * h;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight();
                float ndl = saturate(dot(n, light.direction));
                float3 col = _Color.rgb * (light.color * ndl * 0.75 + SampleSH(n) * 0.85);
                return half4(col, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
    }
}
