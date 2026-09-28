#ifndef UNIVERSAL_RESTIR_GI_COORDINATES_INCLUDED
#define UNIVERSAL_RESTIR_GI_COORDINATES_INCLUDED

// The temporal denoiser keeps history on a fixed receiver grid. In Half mode
// each trace texel always represents the first source pixel of its 2x2 cell.
// Alternating checkerboard receivers require a checkerboard-aware denoiser:
// reinterpolating their packed history every frame causes horizontal diffusion.
uint2 GetReSTIRGISourceStep()
{
    // Full/Half are the only supported scales. Use comparisons rather than
    // integer divisions in the per-neighbor addressing path.
    return uint2(_SSGITraceScreenSize.x < _SSGISourceSize.x ? 2u : 1u,
        _SSGITraceScreenSize.y < _SSGISourceSize.y ? 2u : 1u);
}

uint2 GetReSTIRGISourceCoord(uint2 traceCoord)
{
    uint2 sourceSize = max((uint2)_SSGISourceSize.xy, 1u);
    return min(traceCoord * GetReSTIRGISourceStep(), sourceSize - 1u);
}

uint2 GetReSTIRGITraceCoordFromSource(int2 sourceCoord)
{
    uint2 step = GetReSTIRGISourceStep();
    sourceCoord = clamp(sourceCoord, 0, (int2)_SSGISourceSize.xy - 1);
    uint2 traceCoord = (uint2)sourceCoord + (step >> 1u);
    traceCoord.x >>= step.x - 1u;
    traceCoord.y >>= step.y - 1u;
    return min(traceCoord, (uint2)_SSGITraceScreenSize.xy - 1u);
}

float2 GetReSTIRGITracePosition(float2 sourceUV)
{
    // Work in source pixel centers, including odd viewport sizes. In a static
    // view a receiver reprojects to an integer history texel in both modes.
    float2 sourcePixel = sourceUV * _SSGISourceSize.xy - 0.5;
    return sourcePixel / (float2)GetReSTIRGISourceStep();
}

uint2 GetReSTIRGITraceCoord(float2 sourceUV)
{
    int2 traceCoord = (int2)floor(GetReSTIRGITracePosition(sourceUV) + 0.5);
    return (uint2)clamp(traceCoord, 0, (int2)_SSGITraceScreenSize.xy - 1);
}

#endif
