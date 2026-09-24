#include "r2_vertex_transform.h"
#include <math.h>
#include <stdint.h>
#include <stdio.h>

static uint32_t seed = 0x12345678u;
static float random_value(float range)
{
    seed = seed * 1664525u + 1013904223u;
    return ((float)(seed >> 8) / 16777215.0f * 2.0f - 1.0f) * range;
}

/* Independent copy of the previous per-vertex rotation sequence. */
static void reference(float v[3], const float s[3], const float r[6],
    const float c[6], const float position[3])
{
    float x = v[0] * s[0], y = v[1] * s[1], z = v[2] * s[2];
    float t = r[1]*y-r[0]*z; z = r[0]*y+r[1]*z; y = t;
    t = r[3]*x+r[2]*z; z = -r[2]*x+r[3]*z; x = t;
    t = r[5]*x-r[4]*y; y = r[4]*x+r[5]*y; x = t;
    x += position[0]; y += position[1]; z += position[2];
    t = c[1]*x+c[0]*z; z = -c[0]*x+c[1]*z; x = t;
    t = c[3]*y-c[2]*z; z = c[2]*y+c[3]*z; y = t;
    t = c[5]*x+c[4]*y; y = -c[4]*x+c[5]*y; x = t;
    v[0] = x; v[1] = y; v[2] = z;
}

int main(void)
{
    float maximum_error = 0.0f;
    for (unsigned int test = 0; test < 20000u; ++test)
    {
        float r[6], c[6], s[3], p[3], v[3], expected[3], actual[3], m[12];
        for (unsigned int axis = 0; axis < 3; ++axis)
        {
            const float object_angle = random_value(3.14159265f);
            const float camera_angle = random_value(3.14159265f);
            r[axis*2] = sinf(object_angle); r[axis*2+1] = cosf(object_angle);
            c[axis*2] = sinf(camera_angle); c[axis*2+1] = cosf(camera_angle);
            s[axis] = test % 7u == 0u ? 0.0f : random_value(4.0f);
            p[axis] = random_value(1000.0f);
            expected[axis] = v[axis] = random_value(100.0f);
        }
        reference(expected, s, r, c, p);
        r2_build_camera_transform(m, s, r, c, p);
        r2_transform_camera(m, v[0], v[1], v[2], actual);
        for (unsigned int axis = 0; axis < 3; ++axis)
        {
            const float error = fabsf(actual[axis] - expected[axis]);
            if (!isfinite(actual[axis]) || error > 0.002f)
            {
                fprintf(stderr, "Transform mismatch case %u axis %u: %.9g vs %.9g\n",
                    test, axis, actual[axis], expected[axis]);
                return 1;
            }
            if (error > maximum_error) maximum_error = error;
        }
        /* Reused reciprocal must retain screen/depth behavior for in-front
           and behind-camera vertices; clipping still handles their signs. */
        float depth = random_value(1000.0f);
        if (fabsf(depth) < 0.1f) depth = 0.1f;
        const float q = 1.0f / depth;
        const float divided = v[0] * 300.0f / depth;
        const float multiplied = v[0] * 300.0f * q;
        if (fabsf(divided - multiplied) > 0.000001f * (1.0f + fabsf(divided)))
            return 2;
    }
    printf("PASS: 20000 transform/projection cases; maximum camera-space error %.9g\n",
        maximum_error);
    return 0;
}
