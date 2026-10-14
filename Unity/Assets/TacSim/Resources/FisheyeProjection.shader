// Re-projects five 90° camera faces through an equidistant fisheye lens (r = f·θ),
// then applies lens shading and sensor noise. Drawn as a procedural full-screen triangle.
Shader "Hidden/TacSim/FisheyeProjection"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_FaceFront); TEXTURE2D(_FaceRight); TEXTURE2D(_FaceLeft);
            TEXTURE2D(_FaceUp); TEXTURE2D(_FaceDown);
            SAMPLER(sampler_LinearClamp);
            float4 _Projection; // focal length px, max field angle rad, width px, height px
            float4 _Sensor;     // noise, vignetting, frame seed, gain

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.positionCS = float4(uv * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1 - uv.y;
                #endif
                o.uv = uv;
                return o;
            }

            float3 Face(TEXTURE2D_PARAM(face, faceSampler), float2 p)
            {
                return SAMPLE_TEXTURE2D_LOD(face, faceSampler, p * 0.5 + 0.5, 0).rgb;
            }

            // Direction in camera space: X right, Y up, Z forward.
            float3 SampleFaces(float3 d)
            {
                float3 a = abs(d);
                if (a.z >= a.x && a.z >= a.y) return Face(TEXTURE2D_ARGS(_FaceFront, sampler_LinearClamp), d.xy / d.z);
                if (a.x >= a.y)
                {
                    if (d.x > 0) return Face(TEXTURE2D_ARGS(_FaceRight, sampler_LinearClamp), float2(-d.z, d.y) / d.x);
                    return Face(TEXTURE2D_ARGS(_FaceLeft, sampler_LinearClamp), float2(d.z, d.y) / -d.x);
                }
                if (d.y > 0) return Face(TEXTURE2D_ARGS(_FaceUp, sampler_LinearClamp), float2(d.x, -d.z) / d.y);
                return Face(TEXTURE2D_ARGS(_FaceDown, sampler_LinearClamp), float2(d.x, d.z) / -d.y);
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 pixel = (i.uv - 0.5) * _Projection.zw;
                float radius = length(pixel);
                float theta = radius / _Projection.x;
                // One-pixel soft edge on the image circle.
                float inside = saturate((_Projection.y - theta) * _Projection.x + 0.5);
                if (inside <= 0) return float4(0, 0, 0, 1);
                float2 heading = radius > 1e-4 ? pixel / radius : float2(0, 0);
                float3 direction = float3(heading * sin(theta), cos(theta));
                float3 color = SampleFaces(direction) * _Sensor.w;
                float edge = saturate(theta / max(_Projection.y, 1e-3));
                color *= (1 - _Sensor.y * edge * edge) * inside;
                float2 cell = floor(i.uv * _Projection.zw);
                float grain = Hash(cell + _Sensor.z) + Hash(cell * 1.37 + _Sensor.z * 2.1) - 1;
                // Shot noise grows with signal; a read-noise floor keeps dark water grainy.
                color += grain * _Sensor.x * (0.4 + sqrt(saturate(color)));
                return float4(saturate(color), 1);
            }
            ENDHLSL
        }
    }
}
