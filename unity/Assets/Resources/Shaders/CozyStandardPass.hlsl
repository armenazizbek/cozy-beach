#ifndef COZY_STANDARD_PASS_INCLUDED
#define COZY_STANDARD_PASS_INCLUDED

// The forward pass for MeshStandardMaterial / MeshLambertMaterial. Variant shaders (sand, grass, sea, cave rock) define the
// COZY_* hooks below before including this file, the way the TypeScript patched Three's shaders with onBeforeCompile.

#include "CozyCore.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap);
TEXTURE2D(_EmissiveMap); SAMPLER(sampler_EmissiveMap);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_RoughMap); SAMPLER(sampler_RoughMap);

#ifndef COZY_EXTRA_PROPERTIES
#define COZY_EXTRA_PROPERTIES
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float4 _Emissive;
    float _Rough;
    float _Metal;
    float4 _MainTex_ST;
    float4 _Flags;   // x vertex colours, y map, z alpha map, w alpha test
    float4 _Flags2;  // x fog, y tone mapped, z flat shading, w model (0 unlit, 1 standard, 2 lambert)
    float4 _Params;  // x envMapIntensity, y bump scale, z, w
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
    float4 inst : TEXCOORD1;   // instance colour from a baked InstancedMesh
    float4 extra0 : TEXCOORD2; // first extra attribute (or instanced attribute)
    float4 extra1 : TEXCOORD3; // second extra attribute (or instance origin)
    float4 extra2 : TEXCOORD4; // third extra attribute (or the vertex before its instance matrix)
    float4 extra3 : TEXCOORD5; // fourth extra attribute (or the instance's x axis)
    float4 extra4 : TEXCOORD6; // fifth extra attribute (or the instance's z axis)
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float4 color : TEXCOORD3;
    float4 custom : TEXCOORD4;  // free for hooks
    float3 objectPos : TEXCOORD5; // object-space position in Three's axes
    float fogDepth : TEXCOORD6;
    float4 custom2 : TEXCOORD7;
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
    o.normalWS = TransformObjectToWorldNormal(normalOS);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.uv = input.uv;
    // _Flags.x: +1 vertex colours, +2 a baked InstancedMesh (only those carry instance colours in TEXCOORD1; other meshes
    // have no TEXCOORD1 and Unity feeds the shader another channel there, so it must not be read for them)
    float4 vc = fmod(_Flags.x, 2.0) > 0.5 ? input.color : float4(1, 1, 1, 1);
    // an instanced mesh's colours multiply in, as Three's instanceColor does
    if (_Flags.x > 1.5 && input.inst.w > 0.5) vc.rgb *= input.inst.rgb;
    o.color = vc;
    o.fogDepth = -TransformWorldToView(positionWS).z;
#ifdef COZY_VERTEX_END_HOOK
    COZY_VERTEX_END_HOOK
#endif
    return o;
}

float3 CozyBump(Varyings i, float3 normal, float3 posWS, float2 uv, float faceSign)
{
    // Three's bump mapping (Morten Mikkelsen's derivative method)
    float2 dSTdx = ddx(uv), dSTdy = ddy(uv);
    float Hll = _Params.y * SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv).x;
    float dBx = _Params.y * SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv + dSTdx).x - Hll;
    float dBy = _Params.y * SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv + dSTdy).x - Hll;
    float3 vSigmaX = ddx(posWS), vSigmaY = ddy(posWS);
    float3 vN = normal;
    float3 R1 = cross(vSigmaY, vN);
    float3 R2 = cross(vN, vSigmaX);
    float fDet = dot(vSigmaX, R1) * faceSign;
    float3 vGrad = sign(fDet) * (dBx * R1 + dBy * R2);
    return normalize(abs(fDet) * normal - vGrad);
}

half4 frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
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

    float faceSign = isFront ? 1.0 : -1.0;
    float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
    float3 N;
    if (_Flags2.z > 0.5)
    {
        N = normalize(cross(ddx(i.positionWS), ddy(i.positionWS)));
        if (dot(N, V) < 0) N = -N;
    }
    else N = normalize(i.normalWS) * faceSign;
    float3 geometryNormal = N;
    if (_Params.y != 0) N = CozyBump(i, N, i.positionWS, uv, faceSign);

    float roughness = _Rough;
    if (_Params.z > 0.5) roughness *= SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, uv).g;
    float3 emissive = _Emissive.rgb;
    if (_Emissive.w > 0.5) emissive *= SAMPLE_TEXTURE2D(_EmissiveMap, sampler_EmissiveMap, uv).rgb;
#ifdef COZY_SURFACE_HOOK
    COZY_SURFACE_HOOK
#endif

    float3 outgoing;
    if (_Flags2.w < 0.5) outgoing = diffuseColor.rgb;
    else
    {
        CozySurface s = CozyMakeSurface(diffuseColor.rgb, _Metal, roughness, geometryNormal, _Flags2.w > 1.5 ? 1.0 : 0.0);
        float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
        outgoing = CozyShade(i.positionWS, N, V, screenUV, s, _Params.x, true);
    }
    outgoing += emissive;
#ifdef COZY_OUTGOING_HOOK
    COZY_OUTGOING_HOOK
#endif
    float3 c = CozyFinish(outgoing, _Flags2.y, _Flags2.x, i.fogDepth);
    return half4(c, diffuseColor.a);
}

#endif
