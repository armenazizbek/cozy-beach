#ifndef COZY_BASIC_PASS_INCLUDED
#define COZY_BASIC_PASS_INCLUDED

// MeshBasicMaterial and LineBasicMaterial: colour, map and vertex colours, no lights; fog and tone mapping as Three applies them.
// Uses the same property layout as the standard pass so materials can switch between them.

#include "CozyCore.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap);

#ifndef COZY_EXTRA_PROPERTIES
#define COZY_EXTRA_PROPERTIES
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float4 _Emissive;
    float _Rough;
    float _Metal;
    float4 _MainTex_ST;
    float4 _Flags;
    float4 _Flags2;
    float4 _Params;
    float _Cull, _ShadowCull, _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask, _OffsetFactor, _OffsetUnits;
    COZY_EXTRA_PROPERTIES
CBUFFER_END

#ifdef COZY_EXTRA_DECLARATIONS
COZY_EXTRA_DECLARATIONS
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
    float4 inst : TEXCOORD1;
    float4 extra0 : TEXCOORD2;
    float4 extra1 : TEXCOORD3;
    float4 extra2 : TEXCOORD4;
    float4 extra3 : TEXCOORD5;
    float4 extra4 : TEXCOORD6;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
    float fogDepth : TEXCOORD2;
    float4 custom : TEXCOORD3;
    float3 positionWS : TEXCOORD4;
    float3 objectPos : TEXCOORD5;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings vert(Attributes input)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 positionOS = input.positionOS.xyz;
    float3 normalOS = input.normalOS;
    o.objectPos = float3(positionOS.x, positionOS.y, -positionOS.z);
#ifdef COZY_VERTEX_HOOK
    COZY_VERTEX_HOOK
#endif
    float3 positionWS = TransformObjectToWorld(positionOS);
    o.positionWS = positionWS;
    o.positionCS = TransformWorldToHClip(positionWS);
    o.uv = input.uv;
    // _Flags.x: +1 vertex colours, +2 a baked InstancedMesh (only those carry instance colours in TEXCOORD1; other meshes
    // have no TEXCOORD1 and Unity feeds the shader another channel there, so it must not be read for them)
    float4 vc = fmod(_Flags.x, 2.0) > 0.5 ? input.color : float4(1, 1, 1, 1);
    if (_Flags.x > 1.5 && input.inst.w > 0.5) vc.rgb *= input.inst.rgb;
    o.color = vc;
    o.fogDepth = -TransformWorldToView(positionWS).z;
#ifdef COZY_VERTEX_END_HOOK
    COZY_VERTEX_END_HOOK
#endif
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
    float4 diffuseColor = _Color;
    if (_Flags.y > 0.5) diffuseColor *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
#ifdef COZY_ALBEDO_HOOK
    COZY_ALBEDO_HOOK
#endif
    diffuseColor *= i.color;
    if (_Flags.z > 0.5) diffuseColor.a *= SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, uv).g;
    if (_Flags.w > 0) clip(diffuseColor.a - _Flags.w);
    float3 outgoing = diffuseColor.rgb;
#ifdef COZY_OUTGOING_HOOK
    COZY_OUTGOING_HOOK
#endif
    float3 c = CozyFinish(outgoing, _Flags2.y, _Flags2.x, i.fogDepth);
    return half4(c, diffuseColor.a);
}

#endif
