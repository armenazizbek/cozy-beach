// Three.js Points + PointsMaterial. Each point arrives as four vertices at the same spot (TEXCOORD0 = corner), spread here into
// a square of `size` pixels, shrinking with distance when size attenuation is on, as gl_PointSize does in Three.
Shader "Cozy/Points"
{
    Properties
    {
        _Color ("Color (linear) + opacity", Vector) = (1, 1, 1, 1)
        _MainTex ("Map", 2D) = "white" {}
        _AlphaMap ("Alpha Map", 2D) = "white" {}
        _Flags ("Vertex colours, map, alpha map, alpha test", Vector) = (0, 0, 0, 0)
        _Flags2 ("Fog, tone mapped", Vector) = (1, 1, 0, 0)
        _Params ("x, y, size, size attenuation", Vector) = (1, 0, 1, 1)
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _SrcBlendA ("SrcA", Float) = 1
        [HideInInspector] _DstBlendA ("DstA", Float) = 0
        [HideInInspector] _ColorMask ("ColorMask", Float) = 15
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "Points"
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
                float _ZWrite, _ZTest, _SrcBlend, _DstBlend, _SrcBlendA, _DstBlendA, _ColorMask;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : TEXCOORD1; float fogDepth : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 positionVS = TransformWorldToView(positionWS);
                float4 cs = TransformWViewToHClip(positionVS);
                float sizePx = _Params.w > 0.5 ? _Params.z * (_CozyScreen.y * 0.5) / max(1e-4, -positionVS.z) : _Params.z * _CozyScreen.z;
                float2 offset = (v.corner - 0.5) * sizePx / _CozyScreen.xy * 2.0;
                // the projection is upside down when drawing into a texture on some graphics APIs
                offset.y *= _ProjectionParams.x;
                cs.xy += offset * cs.w;
                o.positionCS = cs;
                o.uv = v.corner;
                o.color = _Flags.x > 0.5 ? v.color : float4(1, 1, 1, 1);
                o.fogDepth = -positionVS.z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                float4 col = _Color * i.color;
                if (_Flags.y > 0.5) col *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                if (_Flags.z > 0.5) col.a *= SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, uv).g;
                if (_Flags.w > 0) clip(col.a - _Flags.w);
                return half4(CozyFinish(col.rgb, _Flags2.y, _Flags2.x, i.fogDepth), col.a);
            }
            ENDHLSL
        }
    }
}
