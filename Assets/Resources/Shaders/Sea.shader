// Open sea around the island: slow world-space swell, turquoise lagoon near the shore, fog-aware.
Shader "Akropolis/Sea"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.12, 0.40, 0.66, 1)
        _Shallow ("Shallow", Color) = (0.30, 0.70, 0.82, 1)
        _Lagoon ("Lagoon", Color) = (0.45, 0.86, 0.85, 1)
        _LagoonRange ("Lagoon start/end", Vector) = (24, 60, 0, 0)
        _Speed ("Speed", Float) = 1
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Shallow, _Lagoon;
                float4 _LagoonRange;
                float _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 p = i.positionWS.xz;
                float w1 = sin(p.x * 0.9 + t * 0.9) * sin(p.y * 0.8 - t * 0.7);
                float w2 = sin((p.x + p.y) * 1.7 - t * 1.4) * 0.5;
                float3 col = lerp(_Deep.rgb, _Shallow.rgb, saturate((w1 + w2) * 0.35 + 0.5));

                float d = length(p);
                col = lerp(_Lagoon.rgb, col, smoothstep(_LagoonRange.x, _LagoonRange.y, d));
                col += pow(saturate(1.0 - abs(w1 + w2 * 0.6)), 8.0) * 0.12;

                float3 n = normalize(float3(-cos(p.x * 0.9 + t * 0.9) * 0.12, 1.0, cos(p.y * 0.8 - t * 0.7) * 0.12));
                Light light = GetMainLight();
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 h = normalize(light.direction + viewDir);
                float spec = pow(saturate(dot(n, h)), 120.0) * 0.9;
                col = col * (0.7 + 0.3 * saturate(dot(n, light.direction))) + spec * light.color;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Depth passes so the sea takes part in the depth/normals prepass (SSAO) like any opaque.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 frag() : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}
