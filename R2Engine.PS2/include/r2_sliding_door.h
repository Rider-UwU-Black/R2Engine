#ifndef R2_SLIDING_DOOR_H
#define R2_SLIDING_DOOR_H
#include <stdint.h>
#include <string.h>
#include <math.h>
typedef struct R2SlidingDoor {
    uint32_t source,button;
    float lift,speed,range,closed[3];
    float progress;
    int opening;
} R2SlidingDoor;
typedef struct R2DoorCheckpoint {
    uint32_t source;
    float closed[3],progress;
    uint32_t opening;
} R2DoorCheckpoint;
static inline int r2_door_read(R2SlidingDoor *d,const void *bytes,uint32_t count)
{
    memset(d,0,sizeof(*d));memcpy(d,bytes,32);
    if(d->source>=count||d->button>15||!isfinite(d->lift)||d->lift<=0||!isfinite(d->speed)||d->speed<=0||!isfinite(d->range)||d->range<=0)return 0;
    for(int i=0;i<3;i++)if(!isfinite(d->closed[i]))return 0;
    if(!isfinite(d->closed[1]+d->lift)||!isfinite(d->range*d->range))return 0;
    return 1;
}
static inline float r2_door_distance(const R2SlidingDoor *d,const float player[3])
{float v=0;for(int i=0;i<3;i++){float x=player[i]-d->closed[i];v+=x*x;}return v;}
static inline void r2_door_tick(R2SlidingDoor *d,float position[3],float dt)
{
    if(!d->opening||!isfinite(dt)||dt<=0)return;
    d->progress=fminf(d->lift,d->progress+d->speed*dt);
    memcpy(position,d->closed,12);position[1]+=d->progress;
}
static inline void r2_door_capture(const R2SlidingDoor *d,R2DoorCheckpoint *saved)
{
    saved->source=d->source;memcpy(saved->closed,d->closed,12);
    saved->progress=d->progress;saved->opening=d->opening!=0;
}
static inline int r2_door_restore(R2SlidingDoor *d,float position[3],const R2DoorCheckpoint *saved)
{
    if(saved->source!=d->source || saved->opening>1 || !isfinite(saved->progress) ||
       saved->progress<0 || saved->progress>d->lift+0.001f)return 0;
    for(int i=0;i<3;i++)if(!isfinite(saved->closed[i]) || fabsf(saved->closed[i]-d->closed[i])>.001f)return 0;
    d->progress=fminf(d->lift,saved->progress);d->opening=saved->opening!=0;
    memcpy(position,d->closed,12);position[1]+=d->progress;
    return 1;
}
#endif
