// The heart-shaped particles that float up when the dog is petted (src/core/Particles.ts HEART_FRAG).
Shader "Cozy/ParticlesHeart"
{
    Properties
    {
        uSize ("Size", Float) = 0.17
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
            Name "Hearts"
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
                float2 p = (pc - 0.5) * 2.0;
                float x = p.x * 1.25;
                float y = -p.y * 1.25 + 0.25;
                // GLSL's pow() of a negative base is NaN, so in the browser everything inside the unit circle is kept; the same here
                float b = x * x + y * y - 1.0;
                float f = b * b * b - x * x * y * y * y;
                if (b >= 0.0 && f > 0.0) discard;
                float shade = 0.85 + 0.25 * saturate(-p.y * 0.5 + 0.3);
                return half4(CozySRGBToLinear(i.color * shade), i.alpha);
            }
            ENDHLSL
        }
    }
}
