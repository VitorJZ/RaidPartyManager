Shader "RaidPartyManager/Moon Practical"
{
    Properties { _MainTex ("Printed gauze",2D)="white"{} _Color ("Tint",Color)=(1,1,1,1) _Lamp ("Emission",Float)=0 }
    SubShader { Tags {"Queue"="Transparent" "RenderType"="Opaque"}
        Pass { Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Color; float _Lamp;
            half4 frag(v2f_img i):SV_Target {
                if (_Lamp > 1.5)
                {
                    half r = length(i.uv - .5) * 4.8;
                    half falloff = exp2(-3.8 * max(0, r - .85)) * (1 - smoothstep(1.85, 2.4, r));
                    return half4(1, .74, .38, .38 * falloff);
                }
                half3 print=tex2D(_MainTex,i.uv).rgb;
                half lum=dot(print,half3(.25,.60,.15));
                return half4(_Color.rgb * lerp(print, half3(.64,.44,.22)+lum*half3(1.25,1.08,.76),_Lamp),1);
            }
            ENDCG
        }
    }
}
