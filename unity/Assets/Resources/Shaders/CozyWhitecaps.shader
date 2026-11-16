// Whitecaps (src/world/Whitecaps.ts VERT / FRAG). Each patch is a quad of one mesh that never changes on the CPU between
// spawns: the vertex shader drifts it, rides it on the swell, grows it and fades it (World/Whitecaps.cs). A Three ShaderMaterial
// without colour-space conversion writes its colour as sRGB, reproduced here.
Shader "Cozy/Whitecaps"
{
    Properties
    {
        uMap ("Map", 2D) = "white" {}
        uColor ("Color", Vector) = (1, 1, 1, 1)
        uClock ("Foam clock", Float) = 0
        uSwellTime ("Swell clock", Float) = 0
        uLevel ("Sea level", Float) = 0
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
                float uClock, uSwellTime, uLevel;
                float _Cull, _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask, _OffsetFactor, _OffsetUnits;
            CBUFFER_END

            // each vertex is a corner of its patch's quad (x and z from -0.5 to 0.5); aLife (TEXCOORD2) is when the patch was set
            // down on the foam's clock and how long it lasts, aPatch (TEXCOORD3) where it was set down, its heading and its length
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 life : TEXCOORD2; float4 patch : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float alpha : TEXCOORD1; float depth : TEXCOORD2; };

            // Ocean.SWELL, [amplitude, kx, kz, speed] each: keep the two the same
            float CozySwell(float x, float z)
            {
                return 0.1 * sin(x * 0.1 + z * 0.16 + uSwellTime * 0.9)
                     + 0.07 * sin(x * -0.17 + z * 0.23 + uSwellTime * 1.2)
                     + 0.045 * sin(x * 0.31 + z * 0.21 + uSwellTime * 1.6);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                // all in Three's axes until the end: drift from where it was set down, swell up quickly, hold, then melt away
                float age = uClock - v.life.x;
                float u = saturate(age / max(v.life.y, 0.001));
                float x = v.patch.x + 0.35 * age;
                float z = v.patch.y + 0.12 * age;
                float grow = 0.75 + 0.35 * u;
                float sx = v.patch.w * grow, sz = v.patch.w * grow * 0.34;
                float lx = v.positionOS.x * sx, lz = -v.positionOS.z * sz;
                float cy = cos(v.patch.z), sy = sin(v.patch.z);
                float3 three = float3(x + lx * cy + lz * sy, uLevel + CozySwell(x, z) + 0.07, z - lx * sy + lz * cy);
                float3 ws = TransformObjectToWorld(float3(three.x, three.y, -three.z));
                float3 vs = TransformWorldToView(ws);
                o.depth = -vs.z;
                o.positionCS = TransformWViewToHClip(vs);
                o.uv = v.uv;
                o.alpha = age >= 0 && age < v.life.y ? pow(max(0, sin(3.14159265 * u)), 0.7) * 0.8 : 0;
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
