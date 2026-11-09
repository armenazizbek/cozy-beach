// Whitecaps (src/world/Whitecaps.ts VERT / FRAG). The instances are baked into one mesh by the bridge; the per-instance aAlpha
// arrives in TEXCOORD2.x. A Three ShaderMaterial without colour-space conversion writes its colour as sRGB, reproduced here.
Shader "Cozy/Whitecaps"
{
    Properties
    {
        uMap ("Map", 2D) = "white" {}
        uColor ("Color", Vector) = (1, 1, 1, 1)
        [HideInInspector] _Cull ("Cull", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
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
            Name "Whitecaps"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ColorMask [_ColorMask]
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "CozyCore.hlsl"

            TEXTURE2D(uMap); SAMPLER(sampleruMap);

            CBUFFER_START(UnityPerMaterial)
                float4 uMap_ST;
                float4 uColor;
                float _Cull, _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask, _OffsetFactor, _OffsetUnits;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 inst : TEXCOORD2; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float alpha : TEXCOORD1; float depth : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 vs = TransformWorldToView(ws);
                o.depth = -vs.z;
                o.positionCS = TransformWViewToHClip(vs);
                o.uv = v.uv;
                o.alpha = v.inst.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float mask = SAMPLE_TEXTURE2D(uMap, sampleruMap, i.uv).a;
                float far = 1.0 - smoothstep(140.0, 230.0, i.depth);
                float a = mask * i.alpha * far;
                clip(a - 0.01);
                return half4(CozySRGBToLinear(uColor.rgb), a);
            }
            ENDHLSL
        }
    }
}
