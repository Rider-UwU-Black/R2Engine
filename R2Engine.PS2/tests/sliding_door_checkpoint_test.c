#include <assert.h>
#include <math.h>
#include <stdio.h>
#include "../include/r2_sliding_door.h"
int main(void)
{
    R2SlidingDoor original={3,0,3.3f,2,3,{2,1,-4},1.25f,1};
    R2DoorCheckpoint saved;r2_door_capture(&original,&saved);
    R2SlidingDoor restored={3,0,3.3f,2,3,{2,1,-4},0,0};float position[3]={2,1,-4};
    assert(r2_door_restore(&restored,position,&saved));
    assert(fabsf(position[1]-2.25f)<.0001f && restored.opening);
    r2_door_tick(&restored,position,.5f);
    assert(fabsf(restored.progress-2.25f)<.0001f && fabsf(position[1]-3.25f)<.0001f);
    saved.source=4;assert(!r2_door_restore(&restored,position,&saved));saved.source=3;
    saved.closed[0]+=1;assert(!r2_door_restore(&restored,position,&saved));saved.closed[0]-=1;
    saved.progress=NAN;assert(!r2_door_restore(&restored,position,&saved));
    puts("PASS: door checkpoint restores partial position and resumes; rejects stale/corrupt identity");
    return 0;
}
