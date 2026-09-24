#include <math.h>
#include <stdio.h>
#include "../include/r2_camera_sweep.h"

int main(void)
{
    const R2CamVec origin = {0.0f, 0.0f, 3.0f};
    const R2CamVec direction = {0.0f, 0.0f, -1.0f};
    const R2CamVec wall_half_size = {2.0f, 2.0f, 0.025f};
    const float radius = 0.2f;
    const float distance = camera_box_sweep(
        origin, direction, 6.0f, radius, wall_half_size);
    const float expected = 3.0f - wall_half_size.z - radius;
    if (fabsf(distance - expected) > 0.002f)
    {
        fprintf(stderr, "FAIL: thin plane wall hit %.5f, expected %.5f\n",
            distance, expected);
        return 1;
    }
    printf("PASS: camera stops %.3f units before thin plane wall\n", radius);
    return 0;
}
