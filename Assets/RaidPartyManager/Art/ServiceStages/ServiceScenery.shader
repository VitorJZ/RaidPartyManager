Shader "Raid Party Manager/Service Scenery"
{
    Properties
    {
        _MainTex ("Paint", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Surface ("Surface", Float) = 0
        _PlayingZ ("Playing area depth", Float) = 0
        _DepthWrite ("Depth write", Float) = 1
        _Desaturation ("Desaturation", Range(0,1)) = 0
        _CutoutThreshold ("Cutout threshold", Range(0,1)) = .025
        _Walk ("Walk pose", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Cull Off ZWrite [_DepthWrite] Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../Stagecraft/ScenicPaint.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color, _FireLight, _Walk;
            float _Surface, _FlameTime, _FireRange, _BackEdge, _PlayingZ, _Desaturation, _CutoutThreshold;
            struct vertex { float4 pos:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct fragment { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; float3 normal:TEXCOORD2; };
            fragment vert(vertex v)
            {
                fragment o;
                if (_Surface == 4) // Only the top of the flame moves; the logs stay planted.
                    v.pos.x += pow(saturate(v.uv.y),2) * (.019*sin(_FlameTime*6+v.uv.y*8)+.012*sin(_FlameTime*9-v.uv.y*5));
                if (_Walk.z != 0)
                    v.pos.xy += GaitOffset(v.uv, _Walk);
                o.pos = UnityObjectToClipPos(v.pos);
                o.uv = TRANSFORM_TEX(v.uv,_MainTex);
                o.world = mul(unity_ObjectToWorld,v.pos).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            half4 frag(fragment i):SV_Target
            {
                if (_Surface == 3) // soft contact shadow
                    return half4(.021,.014,.018, _Color.a * (1-smoothstep(.08,1,length(i.uv*2-1))));
                if (_Surface == 5) // translucent amber atmosphere and rising sparks
                {
                    float2 p = i.uv;
                    float bloom = exp(-dot((p-float2(.5,.38))*float2(5,6),(p-float2(.5,.38))*float2(5,6)));
                    float sparks=0;
                    [unroll] for (int j=0;j<8;j++)
                    {
                        float t=frac(_FlameTime*(.32+.021*j)+j*.618);
                        float x=.5+.11*sin(j*13.7)+.08*t*sin(t*7+j);
                        float2 d=(p-float2(x,.28+t*.66))*float2(1,.85);
                        sparks+= (1-smoothstep(.001,.006,length(d)))*sin(t*3.14159);
                    }
                    return half4(1,.26+.34*saturate(sparks),.055,saturate(.20*bloom*_FireLight.w+sparks));
                }
                half4 ink=tex2D(_MainTex,i.uv)*_Color;
                half luminance = dot(ink.rgb, half3(.299,.587,.114));
                ink.rgb = lerp(ink.rgb, half3(luminance,luminance,luminance), _Desaturation);
                if (_Surface == 8) ink = SawnFlat(ink, _MainTex, i.uv);
                else if (_Surface == 1) ink.rgb = ScenicPigment(ink.rgb, i.uv);
                clip(ink.a-_CutoutThreshold);
                if (_Surface == 0) return half4(ink.rgb * half3(.86,.91,.97), ink.a);
                if (_Surface == 4) return ink;
                float3 n=normalize(i.normal);
                float diffuse=.70+.39*saturate(dot(n, normalize(float3(-.45,.84,-.48))));
                if (_Surface == 2 || _Surface == 8) diffuse=1; // illustrated people and flats already contain brush shading
                float3 delta=_FireLight.xyz-i.world;
                float dist=length(delta);
                float pool=pow(saturate(1-dist/max(.01,_FireRange)),2)*_FireLight.w;
                float incidence=(_Surface==2) ? .72 : .35+.65*abs(dot(n,delta/max(.01,dist)));
                half stagePool = StageWash(i.world, 6.8, _PlayingZ);
                // Leave the campfire as the strongest local source when it is burning.
                stagePool *= lerp(1, .65, saturate(_FireLight.w));
                ink.rgb = LitStagePaint(ink.rgb, stagePool) * diffuse + pool*incidence*(ink.rgb*float3(2.8,1.14,.30)+float3(.14,.045,.007));
                if (_Surface == 6)
                {
                    float edge = smoothstep(0,6.5,_BackEdge-i.world.z);
                    ink.rgb *= lerp(float3(.46,.62,.67),float3(1,1,1),edge);
                }
                return ink;
            }
            ENDCG
        }
    }
}
