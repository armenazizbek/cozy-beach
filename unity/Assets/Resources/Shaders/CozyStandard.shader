// Three.js MeshStandardMaterial / MeshPhysicalMaterial / MeshLambertMaterial, lit the way Three lights them.
Shader "Cozy/Standard"
{
    Properties
    {
        _Color ("Color (linear) + opacity", Vector) = (1, 1, 1, 1)
        _Emissive ("Emissive (linear, times intensity); w = has map", Vector) = (0, 0, 0, 0)
        _Rough ("Roughness", Float) = 1
        _Metal ("Metalness", Float) = 0
        _MainTex ("Map", 2D) = "white" {}
        _AlphaMap ("Alpha Map", 2D) = "white" {}
        _EmissiveMap ("Emissive Map", 2D) = "white" {}
        _BumpMap ("Bump Map", 2D) = "black" {}
        _RoughMap ("Roughness Map", 2D) = "white" {}
        _Flags ("Vertex colours, map, alpha map, alpha test", Vector) = (0, 0, 0, 0)
        _Flags2 ("Fog, tone mapped, flat, model", Vector) = (1, 1, 0, 1)
        _Params ("Env intensity, bump scale, rough map", Vector) = (1, 0, 0, 1)
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
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include "CozyStandardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_ShadowCull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "CozyStandardPass.hlsl"
            #include "CozyShadowPass.hlsl"
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
            #pragma multi_compile_instancing
            #include "CozyStandardPass.hlsl"
            #include "CozyShadowPass.hlsl"
            ENDHLSL
        }
    }
}
