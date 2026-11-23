// Reuse the actual moving breaker's pixels where it crosses the rider's lower tail.
// Keep the silhouette alpha, so this pass cannot paint over a neighbouring performer.
sampler2D _RideWaveTex;
float4 _RideWavePlacement;
float _RideFoam;
half4 WithRidingFoam(half4 pigment, float2 uv, float4 cel)
{
    if (_RideFoam > 0)
    {
        float2 body = (uv - cel.zw) / cel.xy;
        float2 water = body * _RideWavePlacement.xy + _RideWavePlacement.zw;
        if (all(water >= 0) && all(water <= 1))
        {
            half4 crest = tex2D(_RideWaveTex, water * cel.xy + cel.zw);
            half contact = 1 - smoothstep(.09, .18, body.y);
            pigment.rgb = lerp(pigment.rgb, crest.rgb, crest.a * contact * _RideFoam);
        }
    }
    return pigment;
}
