// The shore foam band (src/world/Ocean.ts foamMat): MeshBasicMaterial whose vertices are placed along the shoreline contour in the
// vertex shader. aShoreSpan arrives in TEXCOORD2.x; uFoam = (time, tide 0..1, band width, water height).
Shader "Cozy/OceanFoam"
{
    Properties
    {
        _Color ("Color (linear) + opacity", Vector) = (1, 1, 1, 1)
        _Emissive ("Unused", Vector) = (0, 0, 0, 0)
        _Rough ("Unused", Float) = 1
        _Metal ("Unused", Float) = 0
        _MainTex ("Map", 2D) = "white" {}
        _AlphaMap ("Alpha Map", 2D) = "white" {}
        _Flags ("Flags", Vector) = (0, 0, 0, 0)
        _Flags2 ("Flags2", Vector) = (1, 1, 0, 0)
        _Params ("Params", Vector) = (1, 0, 0, 1)
        uFoam ("Foam", Vector) = (0, 0, 1, 0)
        [HideInInspector] _Cull ("Cull", Float) = 0
        [HideInInspector] _ShadowCull ("Shadow Cull", Float) = 0
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

    HLSLINCLUDE
    #define COZY_EXTRA_PROPERTIES float4 uFoam;
    #define COZY_VERTEX_HOOK \
        { \
            float3 p3 = float3(positionOS.x, positionOS.y, -positionOS.z); \
            float foamZ = p3.z + input.extra0.x * uFoam.y + sin(p3.x * 0.6 + uFoam.x * 1.4) * 0.18 + p3.y * uFoam.z; \
            positionOS = float3(p3.x, uFoam.w, -foamZ); \
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "Unlit"
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
            #include "CozyBasicPass.hlsl"
            ENDHLSL
        }
    }
}
