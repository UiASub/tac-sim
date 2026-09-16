Shader "TacSim/Caustics"
{
    Properties { _Color("Light tint", Color) = (0.3,0.7,0.65,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float fog : TEXCOORD2; };
            V Vert(A input)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS; o.world = p.positionWS;
                o.normal = TransformObjectToWorldNormal(input.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            float2 Hash(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3)))) * 43758.5453); }
            float Cells(float2 uv)
            {
                float2 cell = floor(uv), f = frac(uv);
                float nearest = 8, next = 8;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 offset = float2(x,y);
                    float2 cellPosition = 0.5 + 0.35 * sin(_Time.y * 0.45 + 6.2831 * Hash(cell + offset));
                    float d = length(offset + cellPosition - f);
                    if(d < nearest) { next = nearest; nearest = d; } else next = min(next,d);
                }
                return 1 - smoothstep(0.02,0.11,next-nearest);
            }
            half4 Frag(V i) : SV_Target
            {
                float3 n = abs(i.normal);
                float2 uv = n.y > 0.5 ? i.world.xz : (n.x > 0.5 ? i.world.zy : i.world.xy);
                float pattern = Cells(uv * 1.1 + float2(_Time.y * 0.07,0));
                float visibility = ComputeFogIntensity(i.fog);
                return half4(_Color.rgb * pattern * visibility * 0.32, 1);
            }
            ENDHLSL
        }
    }
}
