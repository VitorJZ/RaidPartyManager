// The suspended practical and its landing spot are in world space.
// The uniform belongs to one stage material. The HUD is drawn by its own panel.
float4 _MoonWorld, _MoonLanding;
float2 MoonShadowOnDeck(float3 surfacePosition)
{
    float travel = max(0, surfacePosition.y) / max(.1, _MoonWorld.y - surfacePosition.y);
    return surfacePosition.xz + (surfacePosition.xz - _MoonWorld.xz) * travel;
}

half MoonPool(float2 deck)
{
    float2 offset = deck - _MoonLanding.xz;
    float2 direction = _MoonWorld.xz - _MoonLanding.xz;
    direction *= rsqrt(max(.001, dot(direction, direction)));
    float2 across = float2(-direction.y, direction.x);
    float2 ellipse = float2(dot(offset, across) / max(.001, _MoonLanding.w), dot(offset, direction) / 7.5);
    return exp2(-1.35 * dot(ellipse, ellipse));
}

half3 MoonBoards(half3 ink, half pool)
{
    half grain = dot(ink, half3(.26, .58, .16));
    return ink * lerp(half3(.68, .78, .91), half3(2.65, 2.20, 1.60), pool)
        + half3(1, .68, .34) * pool * (.025 + .17 * smoothstep(.025, .27, grain));
}

half MoonNeighbourAlpha(float2 uv)
{
    float2 first = _MainTex_ST.zw;
    float2 last = first + _MainTex_ST.xy;
    if (any(uv < first) || any(uv > last)) return 0;
    return tex2D(_MainTex, uv).a;
}

half3 MoonPaper(half3 ink, float alpha, float2 uv, float2 toMoon, half pool)
{
    // Small figures still catch a line of light at gameplay resolution.
    float2 screenStep = max(abs(ddx(uv)), abs(ddy(uv)));
    float2 stepUV = toMoon * max(abs(_InkTexelSize.xy), screenStep);
    half rim = saturate(alpha - .64 * MoonNeighbourAlpha(uv + stepUV * 1.3)
                              - .36 * MoonNeighbourAlpha(uv + stepUV * 2.9));
    return ink * lerp(half3(.84, .91, .98), half3(1.19, 1.12, 1.02), pool)
        + half3(1, .76, .43) * rim * (.15 + .44 * pool);
}

half4 MoonHaze(float2 uv)
{
    float x = uv.x * 2 - 1;
    float envelope = (1 - smoothstep(.65, 1, abs(x))) * smoothstep(0, .24, uv.y)
        * (1 - smoothstep(.82, 1, uv.y));
    // Overlapping soft shafts read as light passing through the harbour mist.
    float streaks = .026 + .073 * exp2(-18 * (x + .25) * (x + .25))
        + .045 * exp2(-84 * (x - .33) * (x - .33));
    return half4(.99, .78, .49, envelope * streaks);
}
