Shader "AccumulationShader"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off Cull Off
        Pass
        {
            Name "AccumulationPass"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output strucutre (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            TEXTURE2D_X(_AccumulationBuffer);
            SAMPLER(sampler_AccumulationBuffer);

            float _FrameCount;

            half4 frag (Varyings input) : SV_Target
            {
                float4 NewFrame = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
                float4 AccumulatedFrames = SAMPLE_TEXTURE2D_X(_AccumulationBuffer, sampler_AccumulationBuffer, input.texcoord);

                float weight = 1 / _FrameCount;
                return AccumulatedFrames * (1 - weight) + NewFrame * weight;
            }
            ENDHLSL
        }
    }
}