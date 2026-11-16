// A school of fish at the surface (World/SeaLife.cs): Cozy/Standard, every fish of the school one part of a single mesh. The
// vertex shader does what each fish's objects used to do on the CPU every frame: the tail wags about its root, the fish turns
// its head from side to side, drifts about its place in the school and bobs. The school's object carries where the school is
// and which way it heads. aFish (TEXCOORD2): the fish's place in the school (x, z), its phase and its size (0 once it has been
// caught); aKind (TEXCOORD3).x is 1 on the tail. Positions are in the fish model's own space, in Three's axes.
Shader "Cozy/FishSchool"
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
    #define COZY_FISH_SWIM \
        { \
            float ph = input.extra0.z; \
            float size = input.extra0.w; \
            float3 p = float3(positionOS.x, positionOS.y, -positionOS.z); \
            float3 n = float3(normalOS.x, normalOS.y, -normalOS.z); \
            if (input.extra1.x > 0.5) \
            { \
                float ta = sin(uTime * 14.0 + ph) * 0.7; \
                float c = cos(ta), s = sin(ta); \
                p = float3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c - 0.42); \
                n = float3(n.x * c + n.z * s, n.y, -n.x * s + n.z * c); \
            } \
            p = float3(-p.x, p.y, -p.z) * size; \
            n = float3(-n.x, n.y, -n.z); \
            float wa = sin(uTime * 6.0 + ph) * 0.12; \
            float cw = cos(wa), sw = sin(wa); \
            p = float3(p.x * cw + p.z * sw, p.y, -p.x * sw + p.z * cw); \
            n = float3(n.x * cw + n.z * sw, n.y, -n.x * sw + n.z * cw); \
            p += float3(input.extra0.x + sin(uTime * 1.3 + ph) * 0.35, 0.02 + sin(uTime * 5.0 + ph) * 0.02, input.extra0.y + cos(uTime * 1.1 + ph) * 0.35); \
            positionOS = float3(p.x, p.y, -p.z); \
            normalOS = float3(n.x, n.y, -n.z); \
        }
    #define COZY_VERTEX_HOOK COZY_FISH_SWIM
    #define COZY_SHADOW_VERTEX_HOOK COZY_FISH_SWIM
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
            #include "CozyStandardPass.hlsl"
            #include "CozyShadowPass.hlsl"
            ENDHLSL
        }
    }
}
