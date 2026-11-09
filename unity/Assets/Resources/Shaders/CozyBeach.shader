// The sand (src/world/Beach.ts makeMaterial): MeshStandardMaterial with the onBeforeCompile patch that blends a damp-sand texture,
// sampled in world space, wherever the ground has been dug (the per-vertex "damp" attribute, packed into TEXCOORD2.x).
Shader "Cozy/Beach"
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
        _DampMap ("Damp Map", 2D) = "white" {}
        _Flags ("Flags", Vector) = (0, 0, 0, 0)
        _Flags2 ("Flags2", Vector) = (1, 1, 0, 1)
        _Params ("Params", Vector) = (1, 0, 0, 1)
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
    #define COZY_EXTRA_PROPERTIES float4 _DampMap_ST;
    #define COZY_EXTRA_DECLARATIONS TEXTURE2D(_DampMap); SAMPLER(sampler_DampMap);
    #define COZY_VERTEX_END_HOOK o.custom.x = input.extra0.x;
    #define COZY_ALBEDO_HOOK \
        { \
            float2 duv = float2(i.positionWS.x, -i.positionWS.z) * 0.9; \
            float3 dt = lerp(SAMPLE_TEXTURE2D(_DampMap, sampler_DampMap, duv).rgb, SAMPLE_TEXTURE2D(_DampMap, sampler_DampMap, duv * 3.3 + 0.37).rgb, 0.45); \
            diffuseColor.rgb *= lerp(float3(1, 1, 1), dt * 1.55, smoothstep(0.04, 0.55, i.custom.x)); \
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
