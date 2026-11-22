#ifndef RPM_STAGE_LIGHTING
#define RPM_STAGE_LIGHTING
// Colours and focus come from the cue for this set. Positions are metres in the fixed stage frame.
float4 _StageKey, _StageBack, _StageMist;
float _MistHeight, _PlayingZ;
float3 KeyLamp() { return float3(_StageKey.w, 7.4, -5.3); }
float3 BackLamp() { return float3(-_StageKey.w, 7.8, 3.3); }

half SpotAt(float3 world, float3 lamp, float3 mark, float outer, float inner)
{
    float3 delta = world - lamp;
    float d2 = max(.01, dot(delta, delta));
    float cosine = dot(delta * rsqrt(d2), normalize(mark - lamp));
    return smoothstep(outer, inner, cosine) * (1.45 / (1 + .006 * d2));
}

half KeyAt(float3 p)
{
    return SpotAt(p, KeyLamp(), float3(-.14 * _StageKey.w, .75, _StageBack.w + _PlayingZ), .71, .94);
}

half BackAt(float3 p)
{
    return SpotAt(p, BackLamp(), float3(.16 * _StageKey.w, .60, _StageBack.w + _PlayingZ), .82, .975);
}

// A flat catches front light on the painted face; the back light mostly skims its cut edge.
half3 StageExposure(half3 ink, float3 p, float3 normal, bool paper, half rim)
{
    half face = saturate(dot(normal, normalize(KeyLamp() - p)));
    half rear = saturate(dot(normal, normalize(BackLamp() - p)));
    half key = KeyAt(p) * (paper ? .86 : .28 + .72 * face);
    half backSpot = BackAt(p);
    half back = backSpot * (paper ? .19 : .13 + .87 * rear);
    half3 gain = half3(.56,.61,.70) + key * _StageKey.rgb + back * .52 * _StageBack.rgb;
    return ink * min(gain, 1) + ink * (1 - saturate(ink)) * max(gain - 1, 0)
        + (1 - saturate(ink)) * (.013 * key * _StageKey.rgb)
        + _StageBack.rgb * rim * backSpot * .44;
}

half CutEdge(sampler2D artwork, float2 uv, float4 bounds, float2 toLight, half alpha)
{
    float2 pixel = max(abs(ddx(uv)), abs(ddy(uv)));
    float2 probe = uv + toLight * pixel * 1.6;
    float2 lo = min(bounds.zw, bounds.zw + bounds.xy);
    float2 hi = max(bounds.zw, bounds.zw + bounds.xy);
    half neighbour = tex2D(artwork, clamp(probe, lo, hi)).a;
    if (any(probe < lo) || any(probe > hi)) neighbour = 0;
    return saturate(alpha - neighbour);
}

half3 CycloramaWash(half3 ink, float3 world)
{
    // A separate, dimmer wash keeps the painted distance behind the actors.
    half level = .50 + .15 * BackAt(world);
    return ink * half3(.86,.91,.97) * level;
}

// Integrate only the air between this surface and the front of the set. Transparent cutouts
// therefore receive the haze at their own depth, even when the camera moves into a close-up.
half3 StageAir(half3 ink, float3 world)
{
    float3 eye = _WorldSpaceCameraPos.xyz;
    float3 ray = world - eye;
    float entry = saturate((-6.0 - eye.z) / max(.01, ray.z));
    float stepLength = length(ray) * (1 - entry) * .25;
    if (stepLength <= .001 || _StageMist.w <= 0) return ink;
    float3 stepRay = ray * ((1 - entry) * .25);
    float3 p = world - .5 * stepRay;
    [unroll] for (int i=0; i<4; i++, p -= stepRay)
    {
        float h = max(0, p.y);
        float drift = p.x * .73 + p.z * .52 - _Time.y * .25;
        float curl = sin(drift + .9 * sin(p.z * .87 + p.x * .29 + _Time.y * .12));
        float ceiling = _MistHeight * (.93 + .32 * curl);
        half low = exp2(-1.6 * h / max(.1, ceiling)) * (.76 + .24 * curl);
        // The hottest centre of the rear special reads as a shaft in suspended haze.
        half shaft = SpotAt(p, BackLamp(), float3(.16*_StageKey.w,.60,_StageBack.w + _PlayingZ), .926, .992);
        half key = KeyAt(p);
        half haze = .10 + .035 * curl;
        half column = stepLength * _StageMist.w;
        half opacity = 1 - exp2(-column * (low + haze));
        half3 scatter = _StageMist.rgb * (.73 + .21 * key)
            + _StageBack.rgb * (.22 * shaft);
        ink = lerp(ink, scatter, opacity)
            + column * haze * (1.50 * shaft * _StageBack.rgb + .21 * key * _StageKey.rgb);
    }
    return ink;
}
#endif
