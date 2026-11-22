Shader "RaidPartyManager/Roll Harbour Cloth"
{
    Properties
    {
        [HideInInspector] _StageKey ("Front bar colour and position", Vector) = (1,1,1,-4.4)
        [HideInInspector] _StageBack ("Back bar colour and focus", Vector) = (1,1,1,-.6)
        [HideInInspector] _StageMist ("Mist colour and density", Vector) = (0,0,0,0)
        [HideInInspector] _MistHeight ("Low mist height", Float) = 1
        _MainTex ("Departing cloth", 2D) = "white" {}
        _NextTex ("Arriving cloth", 2D) = "white" {}
        _Roll ("Travel", Range(-1,1)) = 0
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
            #include "../../Stagecraft/StageLighting.cginc"
            sampler2D _MainTex, _NextTex;
            float4 _MainTex_ST, _NextTex_ST;
            float _Roll;
            struct Print { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; };
            Print vert(appdata_base v) { Print o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord; o.world=mul(unity_ObjectToWorld,v.vertex).xyz; return o; }
            half4 frag(Print i):SV_Target
            {
                float u=i.uv.x+_Roll;
                float2 uv=float2(u,i.uv.y);
                half3 ink = u >= 0 && u < 1 ? tex2D(_MainTex,uv*_MainTex_ST.xy+_MainTex_ST.zw).rgb
                    : tex2D(_NextTex,float2(frac(u),uv.y)*_NextTex_ST.xy+_NextTex_ST.zw).rgb;
                return half4(StageAir(CycloramaWash(ink, i.world), i.world), 1);
            }
            ENDCG
        }
    }
}
