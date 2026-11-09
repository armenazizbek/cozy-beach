// The open sea (src/world/Ocean.ts): MeshStandardMaterial plus its onBeforeCompile patch. Three long swells lift the surface (the
// same sums as Ocean.swellAt in C#), ripples from a tiling bump texture tilt the normal at three sizes, the colour follows the
// sea-bed depth (aDepth, in TEXCOORD2.x), the sun's glint is held back, the horizon takes the sky's colour and the haze is lighter
// than over the land.
Shader "Cozy/Ocean"
{
    Properties
    {
        _Color ("Color (linear) + opacity", Vector) = (1, 1, 1, 1)
        _Emissive ("Emissive", Vector) = (0, 0, 0, 0)
        _Rough ("Roughness", Float) = 0.22
        _Metal ("Metalness", Float) = 0.15
        _Flags ("Flags", Vector) = (0, 0, 0, 0)
        _Flags2 ("Flags2", Vector) = (1, 1, 0, 1)
        _Params ("Params", Vector) = (1, 0, 0, 1)
        uRipple ("Ripple", 2D) = "grey" {}
        uTime ("Time", Float) = 0
        uShallow ("Shallow", Vector) = (0, 0.3, 0.4, 1)
        uMid ("Mid", Vector) = (0, 0.2, 0.5, 1)
        uDeep ("Deep", Vector) = (0, 0.1, 0.4, 1)
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _ShadowCull ("Shadow Cull", Float) = 1
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src", Float) = 5
        [HideInInspector] _DstBlend ("Dst", Float) = 10
        [HideInInspector] _SrcBlendA ("SrcA", Float) = 1
        [HideInInspector] _DstBlendA ("DstA", Float) = 10
        [HideInInspector] _ColorMask ("ColorMask", Float) = 15
        [HideInInspector] _OffsetFactor ("OffsetFactor", Float) = 0
        [HideInInspector] _OffsetUnits ("OffsetUnits", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "CozyCore.hlsl"

            TEXTURE2D(uRipple); SAMPLER(sampleruRipple);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Emissive;
                float _Rough;
                float _Metal;
                float4 _Flags;
                float4 _Flags2;
                float4 _Params;
                float4 uRipple_ST;
                float uTime;
                float4 uShallow;
                float4 uMid;
                float4 uDeep;
                float _Cull, _ShadowCull, _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask, _OffsetFactor, _OffsetUnits;
            CBUFFER_END

            static const float4 uSwell[3] = { float4(0.1, 0.1, 0.16, 0.9), float4(0.07, -0.17, 0.23, 1.2), float4(0.045, 0.31, 0.21, 1.6) };

            float swellHeight(float2 p, float t)
            {
                float h = 0.0;
                [unroll] for (int k = 0; k < 3; k++) h += uSwell[k].x * sin(dot(p, uSwell[k].yz) + t * uSwell[k].w);
                return h;
            }

            float2 swellSlope(float2 p, float t)
            {
                float2 g = 0;
                [unroll] for (int k = 0; k < 3; k++) g += uSwell[k].x * uSwell[k].yz * cos(dot(p, uSwell[k].yz) + t * uSwell[k].w);
                return g;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 extra0 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 sea : TEXCOORD1;     // Three's world x, lift, Three's world z
                float seaDepth : TEXCOORD2;
                float fogDepth : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float2 seaAt = float2(ws.x, -ws.z);
                float lift = swellHeight(seaAt, uTime) * smoothstep(0.05, 0.7, v.extra0.x);
                ws.y += lift;
                o.positionWS = ws;
                o.sea = float3(seaAt.x, lift, seaAt.y);
                o.seaDepth = v.extra0.x;
                o.positionCS = TransformWorldToHClip(ws);
                o.fogDepth = -TransformWorldToView(ws).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 seaP = i.sea.xz;
                float3 seaWide = SAMPLE_TEXTURE2D(uRipple, sampleruRipple, seaP / 34.0 + uTime * float2(0.006, -0.011)).xyz;
                float3 seaMid = SAMPLE_TEXTURE2D(uRipple, sampleruRipple, seaP / 11.0 + uTime * float2(-0.017, -0.026)).xyz;
                float3 seaFine = SAMPLE_TEXTURE2D(uRipple, sampleruRipple, seaP / 4.1 + uTime * float2(0.035, -0.052)).xyz;
                float seaD = clamp(i.seaDepth, 0.0, 40.0);
                float3 seaColor = lerp(uShallow.rgb, uMid.rgb, smoothstep(0.02, 0.7, seaD));
                seaColor = lerp(seaColor, uDeep.rgb, smoothstep(0.8, 4.0, seaD) * 0.85);
                seaColor *= 0.72 + 0.3 * seaWide.z + 0.2 * seaMid.z + 0.08 * seaFine.z;
                seaColor += float3(0.05, 0.09, 0.11) * smoothstep(0.0, 0.2, i.sea.y);
                float seaFoam = smoothstep(0.74, 0.84, seaMid.z * 0.62 + seaFine.z * 0.32 + i.sea.y * 0.5 + 0.04);
                seaFoam *= smoothstep(0.3, 0.9, seaD) * 0.3;
                float3 albedo = lerp(seaColor, float3(0.92, 0.96, 0.98), seaFoam);
                float alpha = lerp(0.8, 0.96, smoothstep(0.0, 0.3, seaD));

                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 flatN = float3(0, 1, 0);
                float2 tilt = swellSlope(i.sea.xz, uTime) * smoothstep(0.05, 0.7, i.seaDepth);
                tilt += (seaWide.xy - 0.5) * 0.18 + (seaMid.xy - 0.5) * 0.24 + (seaFine.xy - 0.5) * 0.2;
                float3 n3 = normalize(float3(-tilt.x, 1.0, -tilt.y));
                float3 N = float3(n3.x, n3.y, -n3.z);

                CozySurface s = CozyMakeSurface(albedo, _Metal, _Rough, flatN, 0);
                float3 diffuse, directSpec, indirectSpec;
                CozyShadeSplit(i.positionWS, N, V, GetNormalizedScreenSpaceUV(i.positionCS), s, _Params.x, true, diffuse, directSpec, indirectSpec);
                // keep the sun's reflection a glitter on the water rather than a white sheet across it
                directSpec *= 0.3;
                float3 outgoing = diffuse + directSpec + indirectSpec + _Emissive.rgb;
                // the sea takes on the colour of the sky toward the horizon
                float graze = pow(1.0 - max(dot(N, V), 0.0), 5.0);
                outgoing = lerp(outgoing, _CozyFogColor.rgb, graze * 0.14);

                float3 c = outgoing;
                if (_Flags2.y > 0.5 && _CozyTone.x > 0.5) c = CozyACESFilmic(c);
                if (_CozyFogParams.w > 0.5)
                {
                    // the haze sits over the land: the sea keeps its colour far longer than the beach does
                    float haze = max(_CozyFogParams.z * 0.2, 0.004);
                    haze = 1.0 - exp(-haze * haze * i.fogDepth * i.fogDepth);
                    float3 sc = CozyLinearToSRGB(max(c, 0.0));
                    sc = lerp(sc, _CozyFogColor.rgb, haze);
                    c = CozySRGBToLinear(sc);
                }
                return half4(c, alpha);
            }
            ENDHLSL
        }
    }
}
