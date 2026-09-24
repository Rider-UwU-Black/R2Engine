#include "../../R2Engine.PS2/include/r2_animation_playback.h"
#include <assert.h>
#include <stdio.h>
int main(void) {
    unsigned int a=3, b=1; float ta=0, tb=0, blend=1, other=1;
    r2_animation_tick(&a,&ta,&blend,1,.5f,2,1);
    r2_animation_tick(&b,&tb,&other,1,1,2,1);
    assert(ta==.5f && tb==0); /* same mesh, independent PlayOnStart */
    b=3; r2_animation_tick(&b,&tb,&other,1,2,3,1);
    assert(tb==2 && ta==.5f);
    r2_animation_tick(&b,&tb,&other,1,2,3,1); assert(tb==1);
    r2_animation_tick(&a,&ta,&blend,10,1,2,0); assert(ta==2 && !(a&2));
    r2_animation_tick(&a,&ta,&blend,10,1,2,0); assert(ta==2);
    b=3;tb=0; r2_animation_tick(&b,&tb,&other,.5f,-1,2,1); assert(tb==1.5f);
    b=3;tb=0; r2_animation_tick(&b,&tb,&other,.5f,-1,2,0); assert(tb==0 && !(b&2));
    b=3;tb=1; r2_animation_tick(&b,&tb,&other,1,0,2,1); assert(tb==1);
    puts("PASS: independent clocks, PlayOnStart, speed, looping, stopped final pose, reverse and zero speed.");
    assert(r2_animation_finished(9,2,2,0,1,2)); /* stopped at end still transitions */
    assert(r2_animation_finished(11,2,2,0,0,2)); /* self-transition valid */
    assert(!r2_animation_finished(1,2,2,0,1,2)); /* never started */
    assert(!r2_animation_finished(11,1.9f,2,0,1,2));
    assert(!r2_animation_finished(11,2,2,1,1,2)); /* looping */
    assert(!r2_animation_finished(11,2,2,0,~0u,2)); /* no edge */
    assert(!r2_animation_finished(11,0,2,0,1,2)); /* reverse stops at start, not Finished */
    puts("PASS: Finished gating, stopped endpoint, self-edge, looping, unstarted, absent-edge and reverse semantics.");
}
