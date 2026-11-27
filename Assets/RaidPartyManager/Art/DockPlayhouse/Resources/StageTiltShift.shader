Shader "Hidden/Raid Party Manager/Stage Tilt Shift"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X(_StageLensMask);
        float4 _FocusAxis;
        float4 _SharpBand;
        float _BlurStrength;

        half4 ReadStage(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
        }

        half SharpCoverage(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_StageLensMask, sampler_LinearClamp, uv, 0).r;
        }

        void Accumulate(float2 uv, half weight, inout half4 sum, inout half total)
        {
            // Keep sharp ink out of the blur at neighbouring pixels, too.
            weight *= 1 - step(.025, SharpCoverage(uv));
            sum += ReadStage(uv) * weight;
            total += weight;
        }

        half4 FocusStage(Varyings input, float2 axis)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 screen = GetNormalizedScreenSpaceUV(input.positionCS);
            float distanceFromFeet = dot(float3(screen, 1), _FocusAxis.xyz);
            float outsideCast = max(_SharpBand.x - distanceFromFeet, distanceFromFeet - _SharpBand.y);
            float defocus = smoothstep(0, .26, outsideCast);
            half4 centre = ReadStage(uv);
            if (defocus < .001 || SharpCoverage(uv) >= .025) return centre;

            // Isotropic in display pixels, including portrait layouts and mobile render scale.
            float2 stepUV = axis * abs(_BlitTexture_TexelSize.xy) * (_ScaledScreenParams.y * .0025 * defocus * _BlurStrength);
            half total = .1256;
            half4 sum = centre * total;
            Accumulate(uv + 1.46 * stepUV, .2241, sum, total);
            Accumulate(uv - 1.46 * stepUV, .2241, sum, total);
            Accumulate(uv + 3.42 * stepUV, .1386, sum, total);
            Accumulate(uv - 3.42 * stepUV, .1386, sum, total);
            Accumulate(uv + 5.37 * stepUV, .0578, sum, total);
            Accumulate(uv - 5.37 * stepUV, .0578, sum, total);
            Accumulate(uv + 7.32 * stepUV, .0167, sum, total);
            Accumulate(uv - 7.32 * stepUV, .0167, sum, total);
            // Retain fine print and item silhouettes in facility views.
            return lerp(centre, sum / total, _BlurStrength);
        }

        half4 Horizontal(Varyings input) : SV_Target { return FocusStage(input, float2(1, 0)); }
        half4 Vertical(Varyings input) : SV_Target { return FocusStage(input, float2(0, 1)); }
        ENDHLSL

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Horizontal
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Vertical
            ENDHLSL
        }
    }
}
