// The sky dome (src/world/Sky.ts SKY_VERT / SKY_FRAG). A Three ShaderMaterial with no colour-space chunk writes its numbers to the
// screen as they are, so the linear uniform colours show as if they were sRGB; that is reproduced here.
Shader "Cozy/SkyDome"
{
    Properties
    {
        uTop ("Top", Vector) = (0.3, 0.5, 0.8, 1)
        uBottom ("Bottom", Vector) = (0.8, 0.9, 1, 1)
        [HideInInspector] _Cull ("Cull", Float) = 1
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _SrcBlendA ("SrcA", Float) = 1
        [HideInInspector] _DstBlendA ("DstA", Float) = 0
        [HideInInspector] _ColorMask ("ColorMask", Float) = 15
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Background" "Queue" = "Geometry" }

        Pass
        {
            Name "Sky"
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
            #include "CozyCore.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 uTop;
                float4 uBottom;
                float _Cull, _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.dir = normalize(v.positionOS.xyz);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float h = saturate(normalize(i.dir).y * 1.6 + 0.12);
                float3 c = lerp(uBottom.rgb, uTop.rgb, pow(h, 0.7));
                return half4(CozySRGBToLinear(c), 1);
            }
            ENDHLSL
        }
    }
}
