Shader "TacSim/WorldLabel"
{
    Properties { _MainTex("Font atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float fog:TEXCOORD1; float3 normal:TEXCOORD2; };
            V Vert(A i)
            {
                V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color;
                o.fog=ComputeFogFactor(o.positionCS.z); o.normal=TransformObjectToWorldNormal(i.normalOS); return o;
            }
            half4 Frag(V i):SV_Target
            {
                half alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a*i.color.a;
                Light light=GetMainLight();
                half3 illumination=SampleSH(normalize(i.normal))+light.color*saturate(dot(normalize(i.normal),light.direction));
                return half4(MixFog(i.color.rgb*illumination,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
