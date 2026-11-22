#ifndef RPM_SCENIC_PAINT
#define RPM_SCENIC_PAINT
float2 GaitOffset(float2 uv, float4 walk)
{
    float lane = walk.z > 0 ? uv.x : 1 - uv.x;
    float leg = smoothstep(.35,.62,lane) * 2 - 1;
    float knees = 1 - smoothstep(.09,.47,uv.y);
    return float2(walk.z * (leg * walk.x * walk.w * knees + (.045 - .025 * walk.x) * uv.y),
        max(0,leg * walk.y) * .06 * knees + .025 * abs(walk.y) * (1-knees));
}
// Keep the dry-brush marks registered to the prop when it moves under the lights.
half3 ScenicPigment(half3 rgb, float2 uv)
{
    float brush = sin((uv.y + .012 * sin(uv.x * 52)) * 183) * sin(uv.x * 27 + uv.y * 15);
    half luma = dot(rgb, half3(.25, .56, .19));
    return rgb * (.92 + .045 * brush) + half3(.029, .021, .011) * (1 - saturate(2 * luma));
}
half4 SawnFlat(half4 ink, sampler2D artwork, float2 uv)
{
    // Only the trailing side shows the thickness of the raw plywood.
    float2 stepUV = min(.014, max(fwidth(uv), float2(.0008, .0008))) * 1.55;
    half behind = tex2D(artwork, uv + float2(-stepUV.x, stepUV.y)).a;
    half edge = saturate(behind - ink.a);
    half3 kraft = half3(.42, .263, .123) * (.91 + .09 * sin(611 * uv.y));
    ink.rgb = lerp(ScenicPigment(ink.rgb, uv), kraft, edge);
    ink.a = max(ink.a, behind * .90);
    return ink;
}
#endif
