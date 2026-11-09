#ifndef COZY_PARTICLES_PASS_INCLUDED
#define COZY_PARTICLES_PASS_INCLUDED

#include "CozyCore.hlsl"

CBUFFER_START(UnityPerMaterial)
    float uSize;
    float _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float2 corner : TEXCOORD0;
    float4 aAlpha : TEXCOORD2;
    float4 aColor : TEXCOORD3;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float alpha : TEXCOORD1;
    float3 color : TEXCOORD2;
};

Varyings vert(Attributes v)
{
    Varyings o;
    float3 vs = TransformWorldToView(TransformObjectToWorld(v.positionOS.xyz));
    float4 cs = TransformWViewToHClip(vs);
    // gl_PointSize = uSize * (500 / -mv.z), in device pixels
    float sizePx = uSize * (500.0 / max(0.1, -vs.z));
    float2 offset = (v.corner - 0.5) * sizePx / _CozyScreen.xy * 2.0;
    offset.y *= _ProjectionParams.x;
    cs.xy += offset * cs.w;
    o.positionCS = cs;
    o.uv = v.corner;
    o.alpha = v.aAlpha.x;
    o.color = v.aColor.xyz;
    return o;
}

#endif
