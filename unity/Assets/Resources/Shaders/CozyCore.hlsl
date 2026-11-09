#ifndef COZY_CORE_INCLUDED
#define COZY_CORE_INCLUDED

// Shared pieces of the Three.js look: its physically based lighting (MeshStandardMaterial), ACES filmic tone mapping done in
// each material, and fog mixed in sRGB after tone mapping, all fed by globals that THREE.Bridge.SceneSync sets every frame.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float4 _CozyFogColor;   // sRGB
float4 _CozyFogParams;  // x near, y far, z density, w mode (0 none, 1 linear, 2 exp2)
float4 _CozyTone;       // x 1 = ACES filmic, y exposure
float4 _CozyHemiSky;    // linear colour * intensity, summed over hemisphere lights
float4 _CozyHemiGround;
float4 _CozyHemiUp;     // the hemisphere lights' up, in Unity world space
float4 _CozyAmbient;    // ambient lights, summed
float4 _CozyEnv;        // x average radiance of the room environment, y how much brighter above, z scene.environmentIntensity
float4 _CozyScreen;     // x, y screen size in pixels, z pixel ratio

#define COZY_PI 3.141592653589793
#define COZY_RECIPROCAL_PI 0.3183098861837907
#define COZY_EPSILON 1e-6

// ---------------------------------------------------------------- tone mapping and output

float3 CozyRRTAndODTFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 CozyACESFilmic(float3 color)
{
    // Three.js r170's fit, matrices written as rows of the transposed GLSL columns
    const float3x3 ACESInputMat = float3x3(
        float3(0.59719, 0.07600, 0.02840),
        float3(0.35458, 0.90834, 0.13383),
        float3(0.04823, 0.01566, 0.83777));
    const float3x3 ACESOutputMat = float3x3(
        float3(1.60475, -0.10208, -0.00327),
        float3(-0.53108, 1.10813, -0.07276),
        float3(-0.07367, -0.00605, 1.07602));
    color *= _CozyTone.y / 0.6;
    color = mul(color, ACESInputMat);
    color = CozyRRTAndODTFit(color);
    color = mul(color, ACESOutputMat);
    return saturate(color);
}

float3 CozyLinearToSRGB(float3 c)
{
    float3 lo = c * 12.92;
    float3 hi = pow(max(c, 0.0), 0.41666) * 1.055 - 0.055;
    return lerp(hi, lo, step(c, 0.0031308));
}

float3 CozySRGBToLinear(float3 c)
{
    float3 lo = c * 0.0773993808;
    float3 hi = pow(max(c * 0.9478672986 + 0.0521327014, 0.0), 2.4);
    return lerp(hi, lo, step(c, 0.04045));
}

// fog depth is the view-space distance along the camera's axis, as Three measures it
float CozyFogFactor(float fogDepth)
{
    if (_CozyFogParams.w > 1.5) return 1.0 - exp(-_CozyFogParams.z * _CozyFogParams.z * fogDepth * fogDepth);
    if (_CozyFogParams.w > 0.5) return smoothstep(_CozyFogParams.x, _CozyFogParams.y, fogDepth);
    return 0.0;
}

// linear colour in, linear colour out (Unity writes it to an sRGB target); tone mapping and fog the Three.js way
float3 CozyFinish(float3 linearColor, float toneMapped, float fogOn, float fogDepth)
{
    float3 c = linearColor;
    if (toneMapped > 0.5 && _CozyTone.x > 0.5) c = CozyACESFilmic(c);
    if (fogOn > 0.5 && _CozyFogParams.w > 0.5)
    {
        float3 s = CozyLinearToSRGB(max(c, 0.0));
        s = lerp(s, _CozyFogColor.rgb, CozyFogFactor(fogDepth));
        c = CozySRGBToLinear(s);
    }
    return c;
}

// ---------------------------------------------------------------- Three.js physical lighting

struct CozySurface
{
    float3 diffuseColor;   // albedo * (1 - metalness)
    float3 specularColor;  // F0
    float roughness;       // after geometry roughness
    float lambertOnly;     // 1 for MeshLambertMaterial (no specular)
};

float3 CozyBRDF_Lambert(float3 diffuseColor) { return COZY_RECIPROCAL_PI * diffuseColor; }

float3 CozyF_Schlick(float3 f0, float f90, float dotVH)
{
    float fresnel = exp2((-5.55473 * dotVH - 6.98316) * dotVH);
    return f0 * (1.0 - fresnel) + (f90 * fresnel);
}

float CozyV_GGX_SmithCorrelated(float alpha, float dotNL, float dotNV)
{
    float a2 = alpha * alpha;
    float gv = dotNL * sqrt(a2 + (1.0 - a2) * dotNV * dotNV);
    float gl = dotNV * sqrt(a2 + (1.0 - a2) * dotNL * dotNL);
    return 0.5 / max(gv + gl, COZY_EPSILON);
}

float CozyD_GGX(float alpha, float dotNH)
{
    float a2 = alpha * alpha;
    float denom = dotNH * dotNH * (a2 - 1.0) + 1.0;
    return COZY_RECIPROCAL_PI * a2 / (denom * denom);
}

float3 CozyBRDF_GGX(float3 L, float3 V, float3 N, float3 f0, float f90, float roughness)
{
    float alpha = roughness * roughness;
    float3 H = normalize(L + V);
    float dotNL = saturate(dot(N, L));
    float dotNV = saturate(dot(N, V));
    float dotNH = saturate(dot(N, H));
    float dotVH = saturate(dot(V, H));
    float3 F = CozyF_Schlick(f0, f90, dotVH);
    float Vis = CozyV_GGX_SmithCorrelated(alpha, dotNL, dotNV);
    float D = CozyD_GGX(alpha, dotNH);
    return F * (Vis * D);
}

float2 CozyDFGApprox(float3 N, float3 V, float roughness)
{
    float dotNV = saturate(dot(N, V));
    const float4 c0 = float4(-1, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * dotNV)) * r.x + r.y;
    return float2(-1.04, 1.04) * a004 + r.zw;
}

// light arriving from one direction (colour already times intensity and attenuation)
void CozyDirect(float3 L, float3 lightColor, float3 N, float3 V, CozySurface s, inout float3 diffuse, inout float3 specular)
{
    float dotNL = saturate(dot(N, L));
    float3 irradiance = dotNL * lightColor;
    diffuse += irradiance * CozyBRDF_Lambert(s.diffuseColor);
    if (s.lambertOnly < 0.5) specular += irradiance * CozyBRDF_GGX(L, V, N, s.specularColor, 1.0, s.roughness);
}

// Three's RoomEnvironment, filtered, as a soft glow that is brighter from above
float3 CozyEnvRadiance(float3 dir)
{
    return (_CozyEnv.x + _CozyEnv.y * dir.y) * _CozyEnv.z;
}

void CozyIndirect(float3 N, float3 V, CozySurface s, float envMapIntensity, inout float3 diffuse, inout float3 specular)
{
    // hemisphere and ambient lights
    float hemiWeight = 0.5 * dot(N, _CozyHemiUp.xyz) + 0.5;
    float3 irradiance = lerp(_CozyHemiGround.rgb, _CozyHemiSky.rgb, hemiWeight) + _CozyAmbient.rgb;
    diffuse += irradiance * CozyBRDF_Lambert(s.diffuseColor);

    if (_CozyEnv.z <= 0.0) return;
    // image based light: irradiance is pi times the cosine-weighted radiance, radiance is the reflected direction
    float3 iblIrradiance = COZY_PI * CozyEnvRadiance(N) * envMapIntensity;
    float3 R = reflect(-V, N);
    float3 radiance = CozyEnvRadiance(normalize(lerp(R, N, s.roughness * s.roughness))) * envMapIntensity;
    if (s.lambertOnly > 0.5)
    {
        diffuse += iblIrradiance * CozyBRDF_Lambert(s.diffuseColor);
        return;
    }
    float2 fab = CozyDFGApprox(N, V, s.roughness);
    float3 Fr = s.specularColor;
    float3 FssEss = Fr * fab.x + fab.y;
    float Ess = fab.x + fab.y;
    float Ems = 1.0 - Ess;
    float3 Favg = Fr + (1.0 - Fr) * 0.047619;
    float3 Fms = FssEss * Favg / (1.0 - Ems * Favg);
    float3 single = FssEss;
    float3 multi = Fms * Ems;
    float3 total = single + multi;
    float3 diffuseIbl = s.diffuseColor * (1.0 - max(max(total.r, total.g), total.b));
    float3 cosineWeighted = iblIrradiance * COZY_RECIPROCAL_PI;
    specular += radiance * single;
    specular += multi * cosineWeighted;
    diffuse += diffuseIbl * cosineWeighted;
}

// every light URP knows about for this pixel, plus the hemisphere, ambient and environment, kept apart as Three's
// reflectedLight is (direct diffuse + indirect diffuse, direct specular, indirect specular)
void CozyShadeSplit(float3 positionWS, float3 N, float3 V, float2 screenUV, CozySurface s, float envMapIntensity, bool receiveShadows,
    out float3 diffuseOut, out float3 directSpecularOut, out float3 indirectSpecularOut)
{
    float3 diffuse = 0, specular = 0;
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    float mainShadow = receiveShadows ? mainLight.shadowAttenuation : 1.0;
    CozyDirect(mainLight.direction, mainLight.color * mainLight.distanceAttenuation * mainShadow, N, V, s, diffuse, specular);

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;
    half4 shadowMask = half4(1, 1, 1, 1);
#if USE_CLUSTER_LIGHT_LOOP
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
        float sh = receiveShadows ? light.shadowAttenuation : 1.0;
        CozyDirect(light.direction, light.color * light.distanceAttenuation * sh, N, V, s, diffuse, specular);
    }
#endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
        float sh = receiveShadows ? light.shadowAttenuation : 1.0;
        CozyDirect(light.direction, light.color * light.distanceAttenuation * sh, N, V, s, diffuse, specular);
    LIGHT_LOOP_END
#endif

    float3 indirectSpecular = 0;
    CozyIndirect(N, V, s, envMapIntensity, diffuse, indirectSpecular);
    diffuseOut = diffuse;
    directSpecularOut = specular;
    indirectSpecularOut = indirectSpecular;
}

float3 CozyShade(float3 positionWS, float3 N, float3 V, float2 screenUV, CozySurface s, float envMapIntensity, bool receiveShadows)
{
    float3 d, ds, is;
    CozyShadeSplit(positionWS, N, V, screenUV, s, envMapIntensity, receiveShadows, d, ds, is);
    return d + ds + is;
}

CozySurface CozyMakeSurface(float3 albedo, float metalness, float roughness, float3 geometryNormal, float lambert)
{
    CozySurface s;
    s.diffuseColor = albedo * (1.0 - metalness);
    s.specularColor = lerp(float3(0.04, 0.04, 0.04), albedo, metalness);
    float3 dxy = max(abs(ddx(geometryNormal)), abs(ddy(geometryNormal)));
    float geometryRoughness = max(max(dxy.x, dxy.y), dxy.z);
    float r = max(roughness, 0.0525);
    r += geometryRoughness;
    s.roughness = min(r, 1.0);
    s.lambertOnly = lambert;
    return s;
}

#endif
