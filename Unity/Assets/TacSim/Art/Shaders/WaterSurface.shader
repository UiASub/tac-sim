Shader "TacSim/WaterSurface"
{
    Properties { _Color("Water", Color) = (0.06,0.28,0.34,0.65) _LightLevel("Surface illumination", Float) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _LightLevel;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; };
            V Vert(A i) { V o; o.world=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.world); return o; }
            half4 Frag(V i):SV_Target
            {
                float t = _Time.y;
                float2 p = i.world.xz;
                float2 wave = float2(sin(p.x*2.2+p.y*1.3+t*0.8),cos(p.y*2.7-p.x*0.7+t*0.6));
                float3 normal = normalize(float3(wave.x*0.14,1,wave.y*0.14));
                float3 view = normalize(GetWorldSpaceViewDir(i.world));
                float fresnel = pow(1-abs(dot(normal,view)),3);
                float sparkle = pow(saturate(dot(reflect(-view,normal), normalize(float3(0.3,1,0.2)))),48);
                float ripple = pow(saturate(sin(p.x*6 + wave.y*2 + t) * cos(p.y*5+wave.x*2-t)),8);
                half3 color = lerp(_Color.rgb,half3(0.38,0.64,0.67),fresnel) + sparkle*0.5 + ripple*0.07;
                return half4(color * _LightLevel,lerp(_Color.a,0.92,fresnel));
            }
            ENDHLSL
        }
    }
}
