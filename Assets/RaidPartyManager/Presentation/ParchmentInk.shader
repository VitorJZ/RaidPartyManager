Shader "Hidden/RaidPartyManager/ParchmentInk"
{
    Properties { _MainTex ("Drawing", 2D) = "white" {} }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 drawing = tex2D(_MainTex, input.uv);
                float3 rgb = drawing.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                rgb = LinearToGammaSpace(rgb);
                #endif
                float grayscale = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                // Near-white highlights are clean paper. Darker strokes retain their original alpha.
                float ink = saturate((0.97 - grayscale) / 0.97) * drawing.a;
                return fixed4(0, 0, 0, ink);
            }
            ENDCG
        }
    }
}
