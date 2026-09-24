#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct { uint32_t lane[4]; } Quad;

/* Mirrors the input reads of r2_vu1_transform.vsm, including the full
   STQ/RGBAQ/XYZ records copied to the exact-length GIF output. */
static unsigned emit(const Quad input[221], Quad output[217])
{
    unsigned cursor = 1;
    output[0] = input[1];
    const unsigned index_offset = input[0].lane[1];
    for (unsigned triangle = 0; triangle < input[0].lane[0]; ++triangle)
        for (unsigned corner = 0; corner < 3; ++corner)
        {
            const unsigned vertex = input[index_offset + triangle].lane[corner];
            for (unsigned record = 0; record < 3; ++record)
                output[cursor++] = input[2 + vertex * 3 + record];
        }
    return cursor;
}

static void fill(Quad input[221], unsigned vertices, unsigned triangles)
{
    input[0].lane[0] = triangles;
    input[0].lane[1] = 2 + vertices * 3;
    for (unsigned lane = 0; lane < 4; ++lane)
        input[1].lane[lane] = 0x10203040u + triangles + lane;
    for (unsigned vertex = 0; vertex < vertices; ++vertex)
        for (unsigned record = 0; record < 3; ++record)
            for (unsigned lane = 0; lane < 4; ++lane)
                input[2 + vertex * 3 + record].lane[lane] =
                    0x31415926u ^ (vertex * 100u + record * 4u + lane);
    for (unsigned triangle = 0; triangle < triangles; ++triangle)
        for (unsigned corner = 0; corner < 3; ++corner)
            input[input[0].lane[1] + triangle].lane[corner] =
                corner == 0 ? vertices - 1 : (triangle * 3 + corner) % vertices;
}

static int verify_compaction(void)
{
    Quad source[64][3], input[221], output[217];
    uint8_t remap[64], compact_source[64], compact_indices[24 * 3];
    memset(remap, 0xff, sizeof(remap));
    memset(input, 0xa5, sizeof(input));
    unsigned compact_count = 0;
    for (unsigned vertex = 0; vertex < 64; ++vertex)
        for (unsigned record = 0; record < 3; ++record)
            for (unsigned lane = 0; lane < 4; ++lane)
                source[vertex][record].lane[lane] =
                    0x600d0000u ^ (vertex * 100u + record * 4u + lane);
    for (unsigned triangle = 0; triangle < 24; ++triangle)
        for (unsigned corner = 0; corner < 3; ++corner)
        {
            const uint8_t original = (uint8_t)((triangle * 7u + corner * 19u) & 63u);
            if (remap[original] == 0xffu)
            {
                remap[original] = (uint8_t)compact_count;
                compact_source[compact_count++] = original;
            }
            compact_indices[triangle * 3 + corner] = remap[original];
        }
    input[0].lane[0] = 24;
    input[0].lane[1] = 2 + compact_count * 3;
    for (unsigned local = 0; local < compact_count; ++local)
        memcpy(&input[2 + local * 3], source[compact_source[local]], 3 * sizeof(Quad));
    for (unsigned triangle = 0; triangle < 24; ++triangle)
        for (unsigned corner = 0; corner < 3; ++corner)
            input[input[0].lane[1] + triangle].lane[corner] =
                compact_indices[triangle * 3 + corner];
    const unsigned output_count = emit(input, output);
    unsigned cursor = 1;
    for (unsigned triangle = 0; triangle < 24; ++triangle)
        for (unsigned corner = 0; corner < 3; ++corner)
        {
            const unsigned local = compact_indices[triangle * 3 + corner];
            if (memcmp(&output[cursor], source[compact_source[local]],
                    3 * sizeof(Quad)) != 0)
                return 0;
            cursor += 3;
        }
    return output_count == cursor;
}

int main(void)
{
    Quad reused[2][221], cleared[221], actual[217], expected[217];
    memset(reused, 0xa5, sizeof(reused));
    unsigned cases = 0;
    for (unsigned vertices = 64; vertices > 0; --vertices)
        for (unsigned triangles = 24; triangles > 0; --triangles)
        {
            memset(cleared, 0, sizeof(cleared));
            fill(cleared, vertices, triangles);
            Quad *bank = reused[cases & 1u];
            fill(bank, vertices, triangles);
            const unsigned count = emit(bank, actual);
            if (count != emit(cleared, expected) ||
                memcmp(actual, expected, count * sizeof(Quad)) != 0)
            {
                fprintf(stderr, "FAIL: %u vertices, %u triangles\n", vertices, triangles);
                return 1;
            }
            ++cases;
    }
    if (!verify_compaction())
    {
        fprintf(stderr, "FAIL: compact vertex remapping changed emitted records\n");
        return 1;
    }
    printf("PASS: %u poisoned/reused input layouts match cleared GIF output\n", cases);
    return 0;
}
