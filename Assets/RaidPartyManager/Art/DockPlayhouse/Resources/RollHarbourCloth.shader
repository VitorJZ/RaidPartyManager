Shader "RaidPartyManager/Roll Harbour Cloth"
{
    Properties
    {
        _MainTex ("Departing cloth", 2D) = "white" {}
        _NextTex ("Arriving cloth", 2D) = "white" {}
        _Roll ("Travel", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry-30" }
        Pass
        {
            Cull Off ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _NextTex;
            float4 _MainTex_ST, _NextTex_ST;
            float _Roll;
            struct Print { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            Print vert(appdata_base v) { Print o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord; return o; }
            half4 frag(Print i):SV_Target
            {
                float u=i.uv.x+_Roll;
                float2 uv=float2(u,i.uv.y);
                if (u < 1)
                    return half4(tex2D(_MainTex,uv*_MainTex_ST.xy+_MainTex_ST.zw).rgb*half3(.87,.915,.975),1);
                uv.x-=1;
                return half4(tex2D(_NextTex,uv*_NextTex_ST.xy+_NextTex_ST.zw).rgb*half3(.87,.915,.975),1);
            }
            ENDCG
        }
    }
}
