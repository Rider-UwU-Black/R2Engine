#ifndef R2_SCRIPT_MOTION_H
#define R2_SCRIPT_MOTION_H
#include <math.h>
#include <string.h>

typedef struct R2ScriptMotion { float position[3], rotation[3]; } R2ScriptMotion;

static inline int r2_script_motion_read(R2ScriptMotion *out, const void *bytes)
{
    memcpy(out, bytes, sizeof(*out));
    for (int axis=0; axis<3; ++axis)
        if (!isfinite(out->position[axis]) || !isfinite(out->rotation[axis])) return 0;
    return 1;
}

static inline void r2_script_motion_tick(const R2ScriptMotion *motion,
    float position[3], float rotation[3], float dt)
{
    if (!isfinite(dt) || dt <= 0) return;
    if (!(motion->position[0] || motion->position[1] || motion->position[2] ||
        motion->rotation[0] || motion->rotation[1] || motion->rotation[2])) return;
    float next_position[3], next_rotation[3];
    for (int axis=0; axis<3; ++axis)
    {
        next_position[axis] = position[axis] + motion->position[axis] * dt;
        next_rotation[axis] = rotation[axis] + motion->rotation[axis] * dt;
        if (!isfinite(next_position[axis]) || !isfinite(next_rotation[axis])) return;
    }
    memcpy(position, next_position, sizeof(next_position));
    memcpy(rotation, next_rotation, sizeof(next_rotation));
}
#endif
