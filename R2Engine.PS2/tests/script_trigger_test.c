#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>

int main(void)
{
    R2ScriptTimer seed={0,1,9,{{0},{0}}}, state;
    R2ScriptMotion step={{0,0,0},{0,45,0}};
    float position[3]={0},rotation[3]={0};
    assert(!r2_script_condition_read(&state,&seed,2));
    assert(r2_script_condition_read(&state,&seed,3));
    r2_script_trigger_tick(&state,&step,position,rotation,0); assert(rotation[1]==0);
    r2_script_trigger_tick(&state,&step,position,rotation,1); assert(rotation[1]==45);
    for(int i=0;i<120;++i) r2_script_trigger_tick(&state,&step,position,rotation,1);
    assert(rotation[1]==45); /* stay cannot retrigger */
    r2_script_trigger_tick(&state,&step,position,rotation,0);
    r2_script_trigger_tick(&state,&step,position,rotation,1); assert(rotation[1]==90);
    R2ScriptTimer other=seed;
    r2_script_trigger_tick(&other,&step,position,rotation,1); assert(rotation[1]==135);
    assert(r2_script_condition_read(&state,&seed,3)); /* reload starts outside */
    r2_script_trigger_tick(&state,&step,position,rotation,1); assert(rotation[1]==180);
    state.threshold=0;
    r2_script_trigger_tick(&state,&step,position,rotation,1); assert(rotation[1]==180 && state.elapsed==0);
    seed.threshold=2; assert(!r2_script_condition_read(&state,&seed,3));
    seed.threshold=1; seed.elapsed=1; assert(!r2_script_condition_read(&state,&seed,3));
    seed.elapsed=0; seed.otherwise.rotation[1]=1; assert(!r2_script_condition_read(&state,&seed,3));
    puts("PASS: enter once, stay suppression, exit/re-entry, instance isolation, reload/spawn-inside, mask disable, malformed records and old-version rejection");
}
