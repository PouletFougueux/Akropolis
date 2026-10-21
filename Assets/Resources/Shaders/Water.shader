// Animated water surface for river tokens, cel-shaded: flat colour bands, crisp foam and glints.
// World-space waves so neighbouring tiles flow together.
Shader "Akropolis/Water"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.13, 0.42, 0.72, 1)
        _Mid ("Mid", Color) = (0.24, 0.6, 0.86, 1)
        _Shallow ("Shallow", Color) = (0.45, 0.8, 0.95, 1)
        _Foam ("Foam", Color) = (0.95, 0.98, 1, 1)
        _Speed ("Speed", Float) = 1
        _FoamAmount ("Edge foam", Range(0, 1)) = 0.85
        _Connected ("Joined edges (bit mask)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            // Always win the depth test against the token's own top face (no flicker of the matte token).
            Offset -1, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Mid, _Shallow, _Foam;
                float _Speed, _FoamAmount, _Connected;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 local : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                // Only ever rises above the rest height, so it never dips into the token below.
                ws.y += (sin(_Time.y * 1.6 + ws.x * 2.3 + ws.z * 1.7) * 0.5 + 0.5) * 0.005;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.local = v.positionOS.xz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 p = i.positionWS.xz;
                float w1 = sin(p.x * 7.0 + t * 1.3) * sin(p.y * 6.3 - t * 1.1);
                float w2 = sin((p.x + p.y) * 11.0 - t * 2.0) * 0.5;
                float n = saturate((w1 + w2) * 0.5 + 0.5);

                // Three flat bands instead of a smooth gradient.
                float3 col = n < 0.38 ? _Deep.rgb : (n < 0.72 ? _Mid.rgb : _Shallow.rgb);

                // Crisp ripple lines.
                float ripple = step(abs(w1 + w2 * 0.6), 0.07);
                col = lerp(col, _Shallow.rgb * 1.12, ripple * 0.7);

                // Hard-edged foam along the hex border.
                float r = length(i.local);
                float wobble = 0.035 * sin(atan2(i.local.y, i.local.x) * 6.0 + t * 3.0);
                // No foam on edges that touch another water tile: the river flows through.
                float ang = atan2(i.local.y, i.local.x);
                if (ang < 0.0) ang += 6.2831853;
                float edge = min(floor(ang / 1.0471976), 5.0);
                float joined = fmod(floor(_Connected / exp2(edge) + 0.001), 2.0);
                col = lerp(col, _Foam.rgb, step(0.84, r + wobble) * _FoamAmount * (1.0 - joined));

                // Toon lighting: two light levels and a sharp sun glint.
                float3 nrm = normalize(float3(-cos(p.x * 7.0 + t * 1.3) * 0.15, 1.0, cos(p.y * 6.3 - t * 1.1) * 0.15));
                Light light = GetMainLight();
                float ndl = saturate(dot(nrm, light.direction));
                col *= ndl > 0.35 ? 1.0 : 0.85;
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 h = normalize(light.direction + viewDir);
                col += step(0.985, saturate(dot(nrm, h))) * 0.55 * light.color;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
