#ifndef UNIVERSAL_RESTIR_GI_RANDOM_INCLUDED
#define UNIVERSAL_RESTIR_GI_RANDOM_INCLUDED

// Separate selection streams for each reservoir resampling pass.
// Reservoir selection needs more entropy than a small, repeating blue-noise table.
#define RESTIR_GI_RANDOM_INITIAL (0x68bc21ebu)
#define RESTIR_GI_RANDOM_TEMPORAL (0x02e5be93u)
#define RESTIR_GI_RANDOM_SPATIAL (0x967a889bu)

uint ReSTIRGIHash(uint value)
{
    value ^= value >> 16u;
    value *= 0x7feb352du;
    value ^= value >> 15u;
    value *= 0x846ca68bu;
    return value ^ (value >> 16u);
}

uint InitReSTIRGIRandom(uint2 pixel, uint frame, uint stream)
{
    return ReSTIRGIHash(pixel.x ^ ReSTIRGIHash(pixel.y + 0x9e3779b9u))
        ^ ReSTIRGIHash(frame ^ stream);
}

float NextReSTIRGIRandom(inout uint state)
{
    // PCG RXS-M-XS: permute each LCG state before using it for selection.
    state = state * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    word = (word >> 22u) ^ word;
    // Construct the mantissa directly: always [0, 1), never UNorm's endpoint 1.
    return asfloat(0x3f800000u | (word >> 9u)) - 1.0;
}

#endif
