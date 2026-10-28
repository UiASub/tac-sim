Shader "TacSim/Seagrass"
{
    // Double-sided ribbons (eelgrass, kelp) that sway with the water. uv.y runs base → tip,
    // vertex colour rgb is a per-blade tint and alpha a per-blade phase.
    Properties
    {
        _BaseColor("Base", Color) = (0.16,0.24,0.08,1)
        _TipColor("Tip", Color) = (0.30,0.28,0.12,1)
        _Sway("Sway (m at tip)", Float) = 0.12
        _Speed("Sway speed", Float) = 0.7
    }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _TipColor;
        float _Sway;
        float _Speed;
    CBUFFER_END
    struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; };
    float3 Sway(float3 world, float height, float phase)
    {
        float bend = height * height;
        float p = dot(world.xz, float2(0.35, 0.27)) + phase * 6.2832;
        float t = _Time.y * _Speed;
        float2 offset = float2(sin(t + p) + 0.4 * sin(2.3 * t + 1.7 * p), 0.6 * cos(0.8 * t + 1.3 * p)) * _Sway * bend;
        world.xz += offset;
        world.y -= length(offset) * 0.35;
        return world;
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half4 color:COLOR; float fog:TEXCOORD3; };
            V Vert(A i)
            {
                V o;
                o.world = Sway(TransformObjectToWorld(i.positionOS.xyz), i.uv.y, i.color.a);
                o.positionCS = TransformWorldToHClip(o.world);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.color = i.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(V i, bool front:SV_IsFrontFace):SV_Target
            {
                float3 n = normalize(front ? i.normalWS : -i.normalWS);
                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, saturate(i.uv.y * 1.2)) * i.color.rgb;
                // Thin leaves transmit light, so use wrapped diffuse on both sides.
                Light sun = GetMainLight();
                half3 light = SampleSH(n) + sun.color * saturate(dot(n, sun.direction) * 0.5 + 0.5) * 0.8;
                InputData inputData = (InputData)0;
                inputData.positionWS = i.world;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light lamp = GetAdditionalLight(lightIndex, i.world);
                    light += lamp.color * lamp.distanceAttenuation * saturate(dot(n, lamp.direction) * 0.5 + 0.5);
                LIGHT_LOOP_END
                return half4(MixFog(albedo * light, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct V { float4 positionCS:SV_POSITION; };
            V Vert(A i) { V o; o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(i.positionOS.xyz), i.uv.y, i.color.a)); return o; }
            half Frag(V i):SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct V { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; };
            V Vert(A i)
            {
                V o;
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(i.positionOS.xyz), i.uv.y, i.color.a));
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half4 Frag(V i, bool front:SV_IsFrontFace):SV_Target
            {
                float3 n = normalize(front ? i.normalWS : -i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(NormalizeNormalPerPixel(n), 0);
                #endif
            }
            ENDHLSL
        }
    }
}
