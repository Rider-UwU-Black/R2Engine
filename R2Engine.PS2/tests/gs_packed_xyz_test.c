#include "r2_gs_packed_xyz.h"
#include <assert.h>
#include <stdio.h>

/* Independently decode using the GS field positions, then compare the
   result with the existing CPU A+D XYZF2 representation. */
static void check(uint32_t depth, uint8_t fog)
{
    uint32_t words[4] = {~0u, ~0u, ~0u, ~0u};
    r2_pack_gs_xyz(words, 0x8123, 0x7fed, depth, fog, 1);
    const uint32_t z = (words[2] >> 4) & 0xffffffu;
    const uint32_t f = (words[3] >> 4) & 0xffu;
    const uint64_t decoded = (uint64_t)(words[0] & 0xffffu) |
        ((uint64_t)(words[1] & 0xffffu) << 16) |
        ((uint64_t)z << 32) | ((uint64_t)f << 56);
    const uint64_t cpu = UINT64_C(0x7fed8123) |
        ((uint64_t)(depth & 0xffffffu) << 32) | ((uint64_t)fog << 56);
    assert(decoded == cpu);
    assert((words[2] & 0xf000000fu) == 0);
    assert((words[3] & ~0xff0u) == 0); /* padding and ADC never set */
    /* Toggle fog off in the same storage: no stale fog or shifted depth. */
    r2_pack_gs_xyz(words, 0xffff, 0, depth, fog, 0);
    assert(words[0] == 0xffff && words[1] == 0);
    assert(words[2] == depth && words[3] == 0);
}

int main(void)
{
    const uint32_t edge_depths[] = {
        0, 1, 15, 16, 255, 256, 0xffff, 0x10000, 0x7fffff,
        0xfffffe, 0xffffff, 0x1000000, 0xffffffffu};
    for (unsigned i = 0; i < sizeof(edge_depths)/sizeof(edge_depths[0]); ++i)
        for (unsigned f = 0; f < 256; ++f)
            check(edge_depths[i], (uint8_t)f);
    for (uint32_t z = 0; z <= 0xffffffu; ++z)
        check(z, (uint8_t)(z ^ (z >> 8)));

    /* Near character must win GEQUAL against more distant terrain, with
       exactly the same depth comparison as the CPU-rendered character. */
    uint32_t body[4];
    const uint32_t terrain_z = 0x200000u, body_z = 0xc00000u;
    for (unsigned f = 0; f < 256; ++f)
    {
        r2_pack_gs_xyz(body, 0, 0, body_z, (uint8_t)f, 1);
        assert(((body[2] >> 4) & 0xffffffu) == body_z);
        assert(((body[2] >> 4) & 0xffffffu) > terrain_z);
    }
    /* Demonstrate that the previous unshifted encoding fails this case. */
    assert((body_z >> 4) < terrain_z);
    puts("PASS: all 16777216 Z24 values, fog endpoints/toggles, CPU packing parity, and depth ordering");
    return 0;
}
