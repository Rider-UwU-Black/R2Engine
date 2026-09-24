#include "r2_animation_controller.h"
#include <assert.h>
#include <stdio.h>

int main(void)
{
    assert(!r2_anim_sprinting(1,1,0));
    assert(!r2_anim_sprinting(1,1,0.0001f));
    assert(r2_anim_sprinting(1,1,0.00011f));
    assert(!r2_anim_sprinting(0,1,1));
    assert(!r2_anim_sprinting(1,0,1));
    /* Mimi's relevant transitions: Idle->Run on Sprinting; Run->Idle on !Moving. */
    R2AnimController controller={0};
    controller.parameter_count=2; controller.transition_count=2;
    controller.transitions[0]=(R2AnimTransition){0,1,0,1,0,0.15f};
    controller.transitions[1]=(R2AnimTransition){1,0,1,0,0,0.15f};
    unsigned state=0;
    for(int frame=0;frame<600;++frame)
    {
        float movement=frame>=200 && frame<400 ? 1.0f : 0;
        float values[2]={movement>0.0001f,r2_anim_sprinting(1,1,movement)};
        int transition=r2_anim_transition(&controller,values,state,0);
        if(transition>=0) state=controller.transitions[transition].to;
        assert(state==(movement>0 ? 1u : 0u));
    }
    puts("PASS: held sprint stays idle without movement, enters Run while moving, returns to stable Idle while still held; threshold/disabled/released cases");
}
