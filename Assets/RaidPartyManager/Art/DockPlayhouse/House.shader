Shader "RaidPartyManager/Dock Playhouse"
{
    Properties
    {
        [HideInInspector] _StageKey ("Front bar colour and position", Vector) = (0,0,0,-4.4)
        [HideInInspector] _StageBack ("Back bar colour and focus", Vector) = (1,1,1,-.6)
        [HideInInspector] _StageMist ("Mist colour and density", Vector) = (0,0,0,0)
        [HideInInspector] _MistHeight ("Low mist height", Float) = 1
        _Grain ("Timber grain", 2D) = "gray" {}
        _HouseLight ("House lights", Range(0,1)) = 1
        _Cloth ("Curtain", Float) = 0
        _PortalMask ("Proscenium stencil write mask", Float) = 0
        _Travel ("Curtain opening", Range(0,1)) = 1
        [HideInInspector] _Hem ("Hem opening", Float) = 1
        [HideInInspector] _Flutter ("Cloth flutter", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _DepthTest ("Depth test", Float) = 4
        [Toggle] _DepthWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGINCLUDE
            #include "UnityCG.cginc"
            #include "../Stagecraft/StageLighting.cginc"
            sampler2D _Grain;
            float _Cloth, _Travel, _Hem, _Flutter, _HouseLight;
            struct In { float4 p:POSITION; float3 n:NORMAL; float4 c:COLOR; float2 uv:TEXCOORD0; };
            struct Out { float4 p:SV_POSITION; float3 n:TEXCOORD0; float2 uv:TEXCOORD1; float4 c:COLOR; float3 local:TEXCOORD2; float3 world:TEXCOORD3; float gather:TEXCOORD4; };
            Out vert(In v)
            {
                Out o;
                o.gather = 0;
                if (_Cloth > .5)
                {
                    float d = max(v.uv.x, 1e-4);
                    float drop = 1 - v.uv.y;
                    // The track pulls the heading. The hem trails it and swings on past when the track stops.
                    float t = clamp(lerp(_Travel, _Hem, pow(drop, 1.6)), 0, 1.06);
                    float lead = lerp(1, .112, t);
                    // Mid-travel the folds pile up against the anchored edge while the leading panel still hangs loose.
                    float k = min(1 / lead, 1 + .85 * sin(3.14159 * saturate(t)));
                    float reach = pow(d, k - 1);
                    v.p.x = 4.21 + (v.p.x - 4.21) * lead * reach;
                    // Compression beyond an even gather deepens the pleats. Loose cloth flattens them a little.
                    float gather = clamp(1 - k * reach, -.4, .7);
                    o.gather = gather;
                    float billow = .045 * _Flutter * sin(d * 9 - _Time.y * 7) * drop;
                    v.p.z = 7.86 + (v.p.z - 7.86) * (1 + .35 * gather) + billow;
                    v.p.y += .12 * saturate(t) * drop * v.uv.x;
                }
                o.p=UnityObjectToClipPos(v.p);
                o.local=v.p.xyz;
                o.world=mul(unity_ObjectToWorld,v.p).xyz;
                o.n=UnityObjectToWorldNormal(v.n); o.uv=v.uv; o.c=v.c;
                return o;
            }
            half4 frag(Out i):SV_Target
            {
                float3 ink=i.c.rgb;
                float2 uv=i.uv;
                if (_Cloth > .5)
                {
                    float pleat=uv.x*12+.14*sin(uv.x*14+uv.y*3)*(1-uv.y)+.16*sin(uv.x*18+.45);
                    float folds=cos(6.28318*pleat);
                    float depth=1+.9*i.gather;
                    float light=.68+.225*folds*depth+.07*sin(6.28318*pleat+.8)+.12*uv.y;
                    light*=1-.08*max(0,i.gather);
                    // A shiver runs through the sailcloth while it travels.
                    light*=1+.05*_Flutter*sin(uv.x*26-_Time.y*10+uv.y*5)*(1-.6*uv.y);
                    float hem=min(abs(uv.y-.036),abs(uv.y-.048));
                    float embroidery=1-smoothstep(.001,.0015+fwidth(uv.y),hem);
                    ink=lerp(ink,float3(.66,.49,.28),embroidery*.82)*light;
                    // A faded, split compass rose is printed on the two halves of sailcloth.
                    float2 map=float2((1-uv.x)*1.17,uv.y-.53);
                    float radius=length(map), angle=atan2(map.y,max(.0001,map.x));
                    float aa=max(.001,fwidth(radius));
                    float ring=1-smoothstep(.001,.001+aa,min(abs(radius-.171),abs(radius-.187)));
                    float spike=pow(abs(cos(angle*4)),13);
                    float needle=(1-smoothstep(.003,.003+aa,abs(radius-(.05+.104*spike))));
                    ink=lerp(ink,float3(.66,.52,.34)*light,.44*max(ring,needle));
                    float weave=.5+.5*sin(uv.y*1440)*sin(uv.x*1000);
                    ink*=.88+.085*cos(uv.y*10+uv.x*8)+.03*weave*saturate(1-fwidth(uv.y)*700);
                    ink*=lerp(1,.68,smoothstep(.988,1,uv.x));
                }
                else
                {
                    float3 n=normalize(i.n);
                    float3 p=i.local;
                    float2 grainUV=abs(n.y)>.55 ? p.xz*float2(.35,.8) : float2(p.x*.30+p.z*.39,p.y*.31);
                    float grain=dot(tex2D(_Grain,grainUV).rgb,float3(.25,.57,.18));
                    float leather=step(ink.g*1.74,ink.r);
                    ink*=lerp(.67+1.12*sqrt(max(.001,grain)),.55+.19*grain,leather);
                    ink*=.65+.33*saturate(dot(n,normalize(float3(-.35,.78,-.54))))+.10*saturate(n.y);
                    // With the house lamps down only upward edges catch spill from the stage.
                    float lens = smoothstep(.85, .95, i.c.r) * smoothstep(.60, .69, i.c.g);
                    float level = lerp(_HouseLight, 1, lens);
                    float edge = pow(saturate(dot(n, normalize(float3(-.14,.87,.47)))), 5);
                    ink *= level * lerp(float3(.77,.85,1.0), 1, level);
                    ink += (1-level) * edge * float3(.038,.029,.024);
                    ink += lens * float3(.15,.095,.045);
                }
                #ifndef UNITY_COLORSPACE_GAMMA
                    ink=GammaToLinearSpace(ink);
                #endif
                if (_StageKey.x > 0)
                    ink = StageAir(StageExposure(ink,i.world,normalize(i.n),false,0), i.world);
                return half4(ink,1);
            }
            half4 LensMask(Out i):SV_Target { return 1; }
        ENDCG

        Pass
        {
            Cull Off
            Stencil { Ref 16 WriteMask [_PortalMask] Comp Always Pass Replace }
            ZTest [_DepthTest]
            ZWrite [_DepthWrite]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="StageLensMask" }
            Cull Off
            ZTest [_DepthTest]
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment LensMask
            ENDCG
        }
    }
}
