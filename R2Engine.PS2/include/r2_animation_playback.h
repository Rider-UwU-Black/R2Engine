#ifndef R2_ANIMATION_PLAYBACK_H
#define R2_ANIMATION_PLAYBACK_H
#include <math.h>
static inline int r2_animation_finished(unsigned int flags, float time, float duration,
    unsigned int loop, unsigned int target, unsigned int state_count)
{
    return (flags & 8u) && !loop && duration > 0 && time >= duration && target < state_count;
}
/* flags: bit 0 animator present, bit 1 playing, bit 2 player. */
static inline void r2_animation_tick(unsigned int *flags, float *time, float *blend,
    float delta, float speed, float duration, unsigned int loop)
{
    if ((*flags & 3u) != 3u || duration <= 0) return;
    *time += delta * speed;
    if (loop) { *time = fmodf(*time, duration); if (*time < 0) *time += duration; }
    else if (*time > duration || *time < 0)
    { *time = fmaxf(0, fminf(duration, *time)); *flags &= ~2u; }
    *blend = fminf(1, *blend + delta / .15f);
}
#endif
