// Beach grass (src/world/Grass.ts): MeshLambertMaterial, swaying with the onBeforeCompile patch. The tufts are an InstancedMesh
// baked into one mesh, so the instance origin is TEXCOORD3, the vertex's own position TEXCOORD4 and the instance's x and z axes
// TEXCOORD5 and 6 (all in Three's axes except the origin, which is in Unity's).
Shader "Cozy/Grass"
{
    Properties
    {
        _Color ("Color (linear) + opacity", Vector) = (1, 1, 1, 1)
        _Emissive ("Emissive", Vector) = (0, 0, 0, 0)
        _Rough ("Roughness", Float) = 1
        _Metal ("Metalness", Float) = 0
        _MainTex ("Map", 2D) = "white" {}
        _AlphaMap ("Alpha Map", 2D) = "white" {}
        _EmissiveMap ("Emissive Map", 2D) = "white" {}
        _BumpMap ("Bump Map", 2D) = "black" {}
        _RoughMap ("Roughness Map", 2D) = "white" {}
        _Flags ("Flags", Vector) = (0, 0, 0, 0)
        _Flags2 ("Flags2", Vector) = (1, 1, 0, 2)
        _Params ("Params", Vector) = (1, 0, 0, 1)
        uTime ("Time", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _ShadowCull ("Shadow Cull", Float) = 1
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _SrcBlendA ("SrcA", Float) = 1
        [HideInInspector] _DstBlendA ("DstA", Float) = 0
        [HideInInspector] _ColorMask ("ColorMask", Float) = 15
        [HideInInspector] _OffsetFactor ("OffsetFactor", Float) = 0
        [HideInInspector] _OffsetUnits ("OffsetUnits", Float) = 0
    }

    HLSLINCLUDE
    #define COZY_EXTRA_PROPERTIES float uTime;
    #define COZY_VERTEX_HOOK \
        { \
            float ph = input.extra1.x * 0.37 + (-input.extra1.z) * 0.29; \
            float ly = input.extra2.y; \
            float dx = sin(uTime * 1.7 + ph) * 0.05 * ly; \
            float dz = cos(uTime * 1.3 + ph * 1.3) * 0.035 * ly; \
            float3 d3 = input.extra3.xyz * dx + input.extra4.xyz * dz; \
            positionOS += float3(d3.x, d3.y, -d3.z); \
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

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
            #include "CozyStandardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex depthVert
            #pragma fragment shadowFrag
            #include "CozyStandardPass.hlsl"
            #include "CozyShadowPass.hlsl"
            ENDHLSL
        }
    }
}
