Shader "Raid Party Manager/Printed Scenery"
{
    Properties
    {
        [HideInInspector] _StageKey ("Front bar colour and position", Vector) = (0,0,0,-4.4)
        [HideInInspector] _StageBack ("Back bar colour and focus", Vector) = (1,1,1,-.6)
        [HideInInspector] _StageMist ("Mist colour and density", Vector) = (0,0,0,0)
        [HideInInspector] _MistHeight ("Low mist height", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _PortalTest ("Proscenium mask", Float) = 8
        // These stage-wide values travel with material copies after the battle scene unloads.
        [HideInInspector] _MoonWorld ("Moon position", Vector) = (0,1,0,0)
        [HideInInspector] _MoonLanding ("Light pool centre and width", Vector) = (0,0,0,1)
        _MainTex ("Artwork", 2D) = "white" {}
        _Color ("Ink tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Stencil { Ref 16 ReadMask 16 Comp [_PortalTest] Pass Keep }
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local __ RPM_DOCK_MOON
            #include "UnityCG.cginc"
            #include "../Stagecraft/ScenicPaint.cginc"
            #include "../Stagecraft/RidingFoam.cginc"
            #include "../Stagecraft/StageLighting.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color, _Walk;
            float _ContactShadow, _ScenicFlat;
            float _PaintedSurface;
            #define SURFACE_SKY 1
            #define SURFACE_BOARDS 2
            #define SURFACE_PAPER 3
            #define SURFACE_HAZE 4
            #define SURFACE_SHADOW 5
            #ifdef RPM_DOCK_MOON
            float4 _InkTexelSize;
            #include "DockMoonlight.cginc"
            #endif
            struct Vertex { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float2 lightUV : TEXCOORD2;
                #ifdef RPM_DOCK_MOON
                float2 moonUV : TEXCOORD3;
                #endif
            };
            Varyings vert(Vertex v)
            {
                // Six-step paper deformation for units with no authored walk sheet.
                if (_Walk.z != 0)
                    v.position.xy += GaitOffset(v.uv, _Walk);
                Varyings o;
                o.position = UnityObjectToClipPos(v.position);
                o.world = mul(unity_ObjectToWorld, v.position).xyz;
                #ifdef RPM_DOCK_MOON
                float2 moonDir = mul((float3x3)unity_WorldToObject, _MoonWorld.xyz - o.world).xy;
                o.moonUV = moonDir / max(.001, length(moonDir));
                #endif
                float2 localLight = mul((float3x3)unity_WorldToObject, BackLamp() - o.world).xy;
                o.lightUV = localLight / max(.001, length(localLight));
                if (_PaintedSurface == SURFACE_SHADOW) // projected cutout shadow
                {
                    #ifdef RPM_DOCK_MOON
                    float3 lamp = _MoonWorld.xyz;
                    #else
                    float3 lamp = KeyLamp();
                    #endif
                    float travel = max(0, o.world.y) / max(.1, lamp.y - o.world.y);
                    float2 floorXZ = o.world.xz + (o.world.xz - lamp.xz) * travel;
                    o.position = UnityWorldToClipPos(float3(floorXZ.x, .007, floorXZ.y));
                }
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half4 color = WithRidingFoam(tex2D(_MainTex,i.uv), i.uv, _MainTex_ST) * _Color;
                if (_ScenicFlat > .5) color = SawnFlat(color, _MainTex, i.uv);
                if (_PaintedSurface == SURFACE_SKY)
                    color.rgb = CycloramaWash(color.rgb, i.world);
                else if (_StageKey.x > 0 && (_PaintedSurface == SURFACE_BOARDS || _PaintedSurface == SURFACE_PAPER))
                {
                    bool paper = _PaintedSurface == SURFACE_PAPER;
                    half rim = paper ? CutEdge(_MainTex, i.uv, _MainTex_ST, i.lightUV, color.a) : 0;
                    color.rgb = StageExposure(color.rgb, i.world, float3(0,1,0), paper, rim);
                    #ifdef RPM_DOCK_MOON
                    color.rgb = paper
                        ? MoonPaper(color.rgb, color.a, i.uv, i.moonUV, MoonPool(MoonShadowOnDeck(i.world)))
                        : MoonBoards(color.rgb, MoonPool(i.world.xz));
                    #endif
                }
                #ifdef RPM_DOCK_MOON
                if (_PaintedSurface == SURFACE_HAZE) return MoonHaze(i.uv);
                if (_PaintedSurface == SURFACE_SHADOW)
                    return half4(.019,.025,.039, color.a * (.13 + .48 * MoonPool(MoonShadowOnDeck(i.world))));
                #else
                if (_PaintedSurface == SURFACE_SHADOW) return half4(.032,.041,.076, color.a * (.14 + .31 * KeyAt(i.world)));
                #endif
                if (_PaintedSurface >= SURFACE_SKY && _PaintedSurface <= SURFACE_PAPER) color.rgb = StageAir(color.rgb, i.world);
                if (_ContactShadow > 0)
                    color.a *= 1-smoothstep(.18,1,length(i.uv*2-1));
                return color;
            }
            ENDCG
        }
    }
}
