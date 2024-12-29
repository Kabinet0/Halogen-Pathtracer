#ifndef _HALOGEN_RANDOM_INCLUDED
#define _HALOGEN_RANDOM_INCLUDED

#include "HalogenDefines.hlsl"

/*
 * 2D [4][32] Array mapped to a 1D array 
 * Dimensions past four are padded from this table
*/
static const uint sobol_table[128] = {
    0x80000000, 0x40000000, 0x20000000, 0x10000000,
    0x08000000, 0x04000000, 0x02000000, 0x01000000,
    0x00800000, 0x00400000, 0x00200000, 0x00100000,
    0x00080000, 0x00040000, 0x00020000, 0x00010000,
    0x00008000, 0x00004000, 0x00002000, 0x00001000,
    0x00000800, 0x00000400, 0x00000200, 0x00000100,
    0x00000080, 0x00000040, 0x00000020, 0x00000010,
    0x00000008, 0x00000004, 0x00000002, 0x00000001,

    0x80000000, 0xc0000000, 0xa0000000, 0xf0000000,
    0x88000000, 0xcc000000, 0xaa000000, 0xff000000,
    0x80800000, 0xc0c00000, 0xa0a00000, 0xf0f00000,
    0x88880000, 0xcccc0000, 0xaaaa0000, 0xffff0000,
    0x80008000, 0xc000c000, 0xa000a000, 0xf000f000,
    0x88008800, 0xcc00cc00, 0xaa00aa00, 0xff00ff00,
    0x80808080, 0xc0c0c0c0, 0xa0a0a0a0, 0xf0f0f0f0,
    0x88888888, 0xcccccccc, 0xaaaaaaaa, 0xffffffff,

    0x80000000, 0xc0000000, 0x60000000, 0x90000000,
    0xe8000000, 0x5c000000, 0x8e000000, 0xc5000000,
    0x68800000, 0x9cc00000, 0xee600000, 0x55900000,
    0x80680000, 0xc09c0000, 0x60ee0000, 0x90550000,
    0xe8808000, 0x5cc0c000, 0x8e606000, 0xc5909000,
    0x6868e800, 0x9c9c5c00, 0xeeee8e00, 0x5555c500,
    0x8000e880, 0xc0005cc0, 0x60008e60, 0x9000c590,
    0xe8006868, 0x5c009c9c, 0x8e00eeee, 0xc5005555,
 
    0x80000000, 0xc0000000, 0x20000000, 0x50000000,
    0xf8000000, 0x74000000, 0xa2000000, 0x93000000,
    0xd8800000, 0x25400000, 0x59e00000, 0xe6d00000,
    0x78080000, 0xb40c0000, 0x82020000, 0xc3050000,
    0x208f8000, 0x51474000, 0xfbea2000, 0x75d93000,
    0xa0858800, 0x914e5400, 0xdbe79e00, 0x25db6d00,
    0x58800080, 0xe54000c0, 0x79e00020, 0xb6d00050,
    0x800800f8, 0xc00c0074, 0x200200a2, 0x50050093
};

/*
 * Helper function to index the 1D sobol direction table as 2D 
*/
uint sobol_table_get(uint dim, uint bit) {
    return sobol_table[(dim * 32) + bit];
}

/*
 * Dimension constants for the various random events in a frame
 * TODO: Make sure combining use of 1D and 2D samples can't repeat sequences
*/

/* Used for setting up the ray's starting conditions */
static const uint FOCAL_DISC_RANDOM_ID = 0;
static const uint RAY_JITTER_RANDOM_ID = 1;

/* Used for both rough reflection and refraction technically, depending on which event occurs */
static const uint ROUGH_REFLECTION_RANDOM_ID = 2;

/* Used for 2D sample that encompasses both doing refraction and specular scattering */
static const uint MATERIAL_BRDF_PROPERTY_RANDOM_ID = 3; 

/* What the random dimension offset is incremented by each bounce */
static const uint BOUNCE_RANDOM_INCREMENT = 4; 

/* RNG State */
static uint SobolDimensionOffset = 0;
static uint pixelID; 

/* State for simple PRNG */
static uint hashState;

/* 
 * https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/
 * PRNG variant of PCG hash
*/
uint u32_hash_stateful(uint value = hashState) {
    uint state = value * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    hashState = state;

    return (word >> 22u) ^ word;
}

/*
 * Wraps u32_hash_stateful, giving a very simple but decent PRNG sequence
 * Returns floats between 0-1
*/
float random_value() 
{
    return (float) u32_hash_stateful() / 4294967296.0f;
}

/*
 * Literal Sebastian Lague theft: https://www.youtube.com/watch?v=Qz0KTGYJtUk&t=679s
 * Also discovered (independantly I swear) from on https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/ 
 * 
 * The standard PCG hash
*/
uint u32_hash(uint value) {
    uint state = value * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;

    return (word >> 22u) ^ word;;
}

void setup_random_state(uint _pixelID, uint _frameCount) {
    // PRNG Hash state
    // Somewhat ugly but good enough
    hashState = _pixelID * _frameCount;

    // Hashed for unclear reasons, maybe remove
    pixelID = u32_hash(_pixelID);
}

/*  
 * From Practical Hash-based Owen Scrambling (https://jcgt.org/published/0009/04/01/)
 * Probably good enough to smash two numbers together
 * Swap for something else later maybe
*/
uint hash_combine(uint seed, uint v) {
    return seed ^ (v + (seed << 6) + (seed >> 2));
}

/*
 * Thanks to https://psychopath.io/post/2021_01_30_building_a_better_lk_hash
 * as well as PBRT
 * used for both scrambling and shuffling the sobol sample sequence
*/ 
uint owen_scramble(uint value, uint seed) {
    // Randomize inital seed value
    // replace with u32 hash
    // uint seed = u32_hash();

    uint x = reversebits(value);

    // // Original Laine-Karras hash.
    // x = x.wrapping_add(seed);
    // x ^= x.wrapping_mul(0x6c50b47c);
    // x ^= x.wrapping_mul(0xb82f1e52);
    // x ^= x.wrapping_mul(0xc7afe638);
    // x ^= x.wrapping_mul(0x8d22f6e6);

    x ^= x * 0x3d20adea;
    x += seed;
    x *= (seed >> 16) | 1;
    x ^= x * 0x05526c56;
    x ^= x * 0x53a22864;

    return reversebits(x);
}

// unknown source 1d sobol probably
// uint sobol(uint index) {
//     uint p = 0;
//     uint d = 0x80000000;

//     for (; index != 0; index >>= 1) {
//         if ((index & 1) != 0) {
//             p ^= d;
//         }
//         d >>= 1;
//     }

//     return p;
// }

uint sobol1d(uint index, uint dim) {
    uint X = 0;
    for (int bit = 0; bit < 32; bit++) {
      int mask = (index >> bit) & 1;
      X ^= mask * sobol_table_get(dim, bit);
    }
    return X;
}

uint2 sobol2d(uint index) {
    return uint2(sobol1d(index, 0), sobol1d(index, 1));
}

uint4 sobol4d(uint index) { 
    return uint4(sobol1d(index, 0), sobol1d(index, 1), sobol1d(index, 2), sobol1d(index, 3));
}


/*
 * Gets an element of a low discrepancy sample sequence
 * Seed shuffles and effectively pads dimensions
 * dimension is fed into seed to make function easier to use
 * seed should be pixel index
 * index is sample index
*/
uint u32_owen_scrambled_sobol(uint index, uint dimension, uint seed) {
    seed ^= u32_hash(dimension);

    uint output = owen_scramble(sobol1d(index, 0), u32_hash(seed)); // scrambling adds randomization 

    return output;
}

/*
 * Gets a 2D element of a low discrepancy sample sequence
 * See the 1D variant for more information
*/
uint2 u32_2d_owen_scrambled_sobol(uint index, uint dimension, uint seed) {
    seed ^= u32_hash(dimension);

    // confusingly enough, owen_scramble() is used for both shuffling and scrambling
    uint shuffledIndex = owen_scramble(index, seed); // shuffles index to decorrelate between dimensions
    uint2 sobolPoints = sobol2d(shuffledIndex);

    uint2 output;
    // scrambling adds randomization 
    output.x = owen_scramble(sobolPoints.x, hash_combine(seed, 0));
    output.y = owen_scramble(sobolPoints.y, hash_combine(seed, 1));

    return output;
}


/*
 * Gets a 4D element of a low discrepancy sample sequence
 * See the 1D variant for more information
*/
uint4 u32_4d_owen_scrambled_sobol(uint index, uint dimension, uint seed) {
    seed ^= u32_hash(dimension);

    // confusingly enough, owen_scramble() is used for both shuffling and scrambling
    uint shuffledIndex = owen_scramble(index, seed); // shuffles index to decorrelate between dimensions
    uint4 sobolPoints = sobol4d(shuffledIndex);

    uint4 output;
    // scrambling adds randomization 
    output.x = owen_scramble(sobolPoints.x, hash_combine(seed, 0));
    output.y = owen_scramble(sobolPoints.y, hash_combine(seed, 1));
    output.z = owen_scramble(sobolPoints.z, hash_combine(seed, 2));
    output.w = owen_scramble(sobolPoints.w, hash_combine(seed, 3));

    return output;
}

float float_owen_scrambled_sobol(uint index, uint dimensionID, uint seed) {
    // For debugging
    #if OVERRIDE_SAMPLING_TO_PRNG
        return random_value();
    #endif

    return ((float)u32_owen_scrambled_sobol(index, SobolDimensionOffset + dimensionID, seed)) / 4294967296.0f;
}

float2 float2_owen_scrambled_sobol(uint index, uint dimensionID, uint seed) {
    // For debugging
    #if OVERRIDE_SAMPLING_TO_PRNG
        return float2(random_value(), random_value());
    #endif
    
    return ((float2)u32_2d_owen_scrambled_sobol(index, SobolDimensionOffset + dimensionID, seed)) / 4294967296.0f;
}

float4 float4_owen_scrambled_sobol(uint index, uint dimensionID, uint seed) {
    // For debugging
    #if OVERRIDE_SAMPLING_TO_PRNG
        return float4(random_value(), random_value(), random_value(), random_value());
    #endif

    return ((float4)u32_4d_owen_scrambled_sobol(index, SobolDimensionOffset + dimensionID, seed)) / 4294967296.0f;
}

/*
 * Gets a random unit vector well distributed within a sphere
*/
float3 get_random_unit_vector(float2 randomData)
{
    float2 uv = randomData;
    float theta = uv.x * 2.0 * PI;
    float phi = acos(2.0 * uv.y - 1.0);
    float r = 1;
    float sinTheta = sin(theta);
    float cosTheta = cos(theta);
    float sinPhi = sin(phi);
    float cosPhi = cos(phi);
    float x = r * sinPhi * cosTheta;
    float y = r * sinPhi * sinTheta;
    float z = r * cosPhi;
    float3 randomVector = float3(x, y, z);
    
    return randomVector;
}

/*
 * Gets a random point vector well distributed within 2D circle
*/
float2 get_random_point_circle(float radius, float2 randomData)
{
    float theta = radians(randomData.x * 360);
    float distAlongRadius = randomData.y;
    return float2(cos(theta) * radius * distAlongRadius, sin(theta) * radius * distAlongRadius);
}

/*
 * Applies a Blackman-Harris filter with a specified width. 
 * Inspired by this post: https://computergraphics.stackexchange.com/questions/2130/anti-aliasing-filtering-in-ray-tracing
*/
float blackman_harris_filter(float x, float width) {
    float phi = FLOAT_2_PI * (x / width);
    return 0.35875f - 0.48829f * cos(phi) + 0.14128f * cos(2.0f * phi) - 0.01168f * cos(3.0f * phi);
}

float arctanh(float x) {
    return 0.5f * log((1+x)/(1-x));
}

/*
 * Generates a Blackman-Harris probabillity distribution 
 * Works through Inverse Transform Sampling with an inverted approximation of the CDF for the blackman harris distribution 
 * See more here: https://www.desmos.com/calculator/cn4gx0sdyb
*/
float inverted_blackman_harris_cdf_approximation(float x) {
    return ((x*1.99221575606) - 0.99610787803) / 6.24;
}

#endif