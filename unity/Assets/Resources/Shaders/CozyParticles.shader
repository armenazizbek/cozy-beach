// The particle pool (src/core/Particles.ts VERT / FRAG): round soft points, sized by distance. aAlpha is TEXCOORD2.x and aColor
// TEXCOORD3.xyz (extra attributes in name order); TEXCOORD0 is the corner of the point's square. Written to the screen as sRGB,
// like a Three ShaderMaterial without colour-space conversion.
Shader "Cozy/Particles"
{
    Properties
    {
        uSize ("Size", Float) = 0.15
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src", Float) = 5
        [HideInInspector] _DstBlend ("Dst", Float) = 10
        [HideInInspector] _SrcBlendA ("SrcA", Float) = 1
        [HideInInspector] _DstBlendA ("DstA", Float) = 10
        [HideInInspector] _ColorMask ("ColorMask", Float) = 15
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "Particles"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "CozyParticlesPass.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                float2 pc = float2(i.uv.x, 1.0 - i.uv.y);
                float d = length(pc - 0.5) * 2.0;
                clip(1.0 - d);
                float a = (1.0 - d * d) * i.alpha;
                return half4(CozySRGBToLinear(i.color), a);
            }
            ENDHLSL
        }
    }
}
