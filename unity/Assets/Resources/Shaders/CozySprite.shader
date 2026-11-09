// Three.js Sprite + SpriteMaterial: a quad that always faces the camera.
Shader "Cozy/Sprite"
{
    Properties
    {
        _Color ("Color (linear) + opacity", Vector) = (1, 1, 1, 1)
        _MainTex ("Map", 2D) = "white" {}
        _AlphaMap ("Alpha Map", 2D) = "white" {}
        _Flags ("Vertex colours, map, alpha map, alpha test", Vector) = (0, 0, 0, 0)
        _Flags2 ("Fog, tone mapped", Vector) = (1, 1, 0, 0)
        _Params ("x, y, rotation, size attenuation", Vector) = (1, 0, 0, 1)
        [HideInInspector] _Cull ("Cull", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src", Float) = 5
        [HideInInspector] _DstBlend ("Dst", Float) = 10
        [HideInInspector] _SrcBlendA ("SrcA", Float) = 1
        [HideInInspector] _DstBlendA ("DstA", Float) = 10
        [HideInInspector] _ColorMask ("ColorMask", Float) = 15
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "Sprite"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "CozyCore.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaMap); SAMPLER(sampler_AlphaMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _Flags;
                float4 _Flags2;
                float4 _Params;
                float _Cull, _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask;
            CBUFFER_END

            float4 _SpriteCenter;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fogDepth : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float4 mv = float4(TransformWorldToView(centerWS), 1);
                float2 scale = float2(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21));
                if (_Params.w < 0.5 && unity_OrthoParams.w < 0.5) scale *= -mv.z;
                float2 center = _SpriteCenter.xy;
                if (center.x == 0 && center.y == 0) center = float2(0.5, 0.5);
                float2 aligned = (v.positionOS.xy - (center - 0.5)) * scale;
                float r = _Params.z, c = cos(r), s = sin(r);
                mv.xy += float2(c * aligned.x - s * aligned.y, s * aligned.x + c * aligned.y);
                o.positionCS = TransformWViewToHClip(mv.xyz);
                o.uv = v.uv;
                o.fogDepth = -mv.z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                float4 col = _Color;
                if (_Flags.y > 0.5) col *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                if (_Flags.z > 0.5) col.a *= SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, uv).g;
                if (_Flags.w > 0) clip(col.a - _Flags.w);
                return half4(CozyFinish(col.rgb, _Flags2.y, _Flags2.x, i.fogDepth), col.a);
            }
            ENDHLSL
        }
    }
}
