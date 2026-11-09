#ifndef COZY_SHADOW_PASS_INCLUDED
#define COZY_SHADOW_PASS_INCLUDED

// Shadow caster and depth passes shared by the Cozy shaders. The material's own UnityPerMaterial block must be declared
// before this is included (by the standard or basic pass file), so the SRP batcher sees one layout.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct ShadowAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
    float4 inst : TEXCOORD1;
    float4 extra0 : TEXCOORD2;
    float4 extra1 : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ShadowVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float alpha : TEXCOORD1;
    float3 objectPos : TEXCOORD2;
};

ShadowVaryings shadowVert(ShadowAttributes input)
{
    ShadowVaryings o = (ShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    float3 positionOS = input.positionOS.xyz;
    float3 normalOS = input.normalOS;
    o.objectPos = float3(positionOS.x, positionOS.y, -positionOS.z);
#ifdef COZY_SHADOW_VERTEX_HOOK
    COZY_SHADOW_VERTEX_HOOK
#endif
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = positionCS;
    o.uv = input.uv;
    o.alpha = fmod(_Flags.x, 2.0) > 0.5 ? input.color.a : 1.0;
    return o;
}

ShadowVaryings depthVert(ShadowAttributes input)
{
    ShadowVaryings o = (ShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    float3 positionOS = input.positionOS.xyz;
    float3 normalOS = input.normalOS;
    o.objectPos = float3(positionOS.x, positionOS.y, -positionOS.z);
#ifdef COZY_SHADOW_VERTEX_HOOK
    COZY_SHADOW_VERTEX_HOOK
#endif
    o.positionCS = TransformObjectToHClip(positionOS);
    o.uv = input.uv;
    o.alpha = fmod(_Flags.x, 2.0) > 0.5 ? input.color.a : 1.0;
    return o;
}

half4 shadowFrag(ShadowVaryings i) : SV_Target
{
    if (_Flags.w > 0)
    {
        float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
        float a = _Color.a * i.alpha;
        if (_Flags.y > 0.5) a *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
        if (_Flags.z > 0.5) a *= SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, uv).g;
        clip(a - _Flags.w);
    }
#ifdef COZY_SHADOW_FRAGMENT_HOOK
    COZY_SHADOW_FRAGMENT_HOOK
#endif
    return 0;
}

#endif
