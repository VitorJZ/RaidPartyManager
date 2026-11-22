Shader "Raid Party Manager/Printed Scenery"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _PortalTest ("Proscenium mask", Float) = 8
        // These stage-wide values travel with material copies after the battle scene unloads.
        [HideInInspector] _MoonWorld ("Moon position", Vector) = (0,1,0,0)
        [HideInInspector] _MoonLanding ("Light pool centre and width", Vector) = (0,0,0,1)
        [HideInInspector] _PlayingHalfWidth ("Playing area half width", Float) = 1
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
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color, _Walk;
            float _ContactShadow, _ScenicFlat;
            #ifdef RPM_DOCK_MOON
            float4 _InkTexelSize;
            float _PlayingHalfWidth;
            #include "DockMoonlight.cginc"
            #endif
            struct Vertex { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                #ifdef RPM_DOCK_MOON
                float3 world : TEXCOORD1;
                float2 lightUV : TEXCOORD2;
                #endif
            };
            Varyings vert(Vertex v)
            {
                // Six-step paper deformation for units with no authored walk sheet.
                if (_Walk.z != 0)
                    v.position.xy += GaitOffset(v.uv, _Walk);
                Varyings o;
                o.position = UnityObjectToClipPos(v.position);
                #ifdef RPM_DOCK_MOON
                o.world = mul(unity_ObjectToWorld, v.position).xyz;
                float2 localLight = mul((float3x3)unity_WorldToObject, _MoonWorld.xyz - o.world).xy;
                o.lightUV = localLight / max(.001, length(localLight));
                if (_MoonSurface == MOON_SHADOW)
                {
                    float2 floorXZ = MoonShadowOnDeck(o.world);
                    o.position = UnityWorldToClipPos(float3(floorXZ.x, .007, floorXZ.y));
                }
                #endif
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half4 color = tex2D(_MainTex,i.uv) * _Color;
                if (_ScenicFlat > .5) color = SawnFlat(color, _MainTex, i.uv);
                #ifdef RPM_DOCK_MOON
                if (_MoonSurface == MOON_SKY)
                    color.rgb *= half3(.88,.92,.98);
                else if (_MoonSurface == MOON_BOARDS || _MoonSurface == MOON_PAPER)
                {
                    half3 frontLit = LitStagePaint(color.rgb, StageWash(i.world, _PlayingHalfWidth, 0));
                    color.rgb = _MoonSurface == MOON_BOARDS
                        ? MoonBoards(frontLit, MoonPool(i.world.xz))
                        : MoonPaper(frontLit, color.a, i.uv, i.lightUV, MoonPool(MoonShadowOnDeck(i.world)));
                }
                else if (_MoonSurface == MOON_HAZE)
                    return MoonHaze(i.uv);
                else if (_MoonSurface == MOON_SHADOW)
                    return half4(.019, .025, .039, color.a * (.13 + .48 * MoonPool(MoonShadowOnDeck(i.world))));
                #endif
                if (_ContactShadow > 0)
                    color.a *= 1-smoothstep(.18,1,length(i.uv*2-1));
                return color;
            }
            ENDCG
        }
    }
}
