Shader "TacSim/OceanWater"
{
 Properties { _BaseColor("Water", Color) = (0.06,0.24,0.26,1) }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
 Pass {
 Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A { float4 positionOS:POSITION; };
 struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float fog:TEXCOORD1; };
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor;
 CBUFFER_END
 V vert(A i) { V o; float3 w=TransformObjectToWorld(i.positionOS.xyz);
 w.y += .45*sin(w.x*.045+_Time.y*.8)+.25*sin(w.z*.067-_Time.y*.6);
 o.world=w; o.positionCS=TransformWorldToHClip(w); o.fog=ComputeFogFactor(o.positionCS.z); return o; }
 half4 frag(V i):SV_Target {
 float2 slope=float2(.02025*cos(i.world.x*.045+_Time.y*.8),.01675*cos(i.world.z*.067-_Time.y*.6));
 float3 n=normalize(float3(-slope.x,1,-slope.y));
 float3 v=normalize(_WorldSpaceCameraPos-i.world);
 float f=pow(1-abs(dot(n,v)),4);
 float ripple=sin(i.world.x*.7+sin(i.world.z*.5)+_Time.y)*.025;
 float3 col=lerp(_BaseColor.rgb,float3(.48,.63,.65),f*.75)+ripple;
 return half4(MixFog(col,i.fog),1); }
 ENDHLSL
 }
 }
}
