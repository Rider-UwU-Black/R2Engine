#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>

int main(void)
{
    R2ScriptTimer seed={0,1,11,{{0,0,0},{0,-45,0}}},state;
    R2ScriptMotion enter={{0,0,0},{0,45,0}};
    float p[3]={0},r[3]={0};
    assert(!r2_script_condition_read(&state,&seed,3));
    assert(r2_script_condition_read(&state,&seed,4));
    for(int cycle=0;cycle<10;++cycle)
    {
        r2_script_trigger_tick(&state,&enter,p,r,0); assert(r[1]==0);
        r2_script_trigger_tick(&state,&enter,p,r,1); assert(r[1]==45);
        for(int frame=0;frame<60;++frame) r2_script_trigger_tick(&state,&enter,p,r,1);
        assert(r[1]==45);
        r2_script_trigger_tick(&state,&enter,p,r,0); assert(r[1]==0);
        for(int frame=0;frame<60;++frame) r2_script_trigger_tick(&state,&enter,p,r,0);
        assert(r[1]==0);
    }
    r2_script_trigger_tick(&state,&enter,p,r,1); assert(r[1]==45);
    /* Reload reinitializes transforms and latch: no synthetic OnTriggerExit. */
    r[1]=0; assert(r2_script_condition_read(&state,&seed,4));
    r2_script_trigger_tick(&state,&enter,p,r,0); assert(r[1]==0);
    r2_script_trigger_tick(&state,&enter,p,r,1); assert(r[1]==45);
    state.threshold=0; r2_script_trigger_tick(&state,&enter,p,r,0); assert(r[1]==45);
    /* Exit-only has no action at spawn or first entry, one step when leaving. */
    seed.operation=10; memset(&seed.otherwise,0,sizeof(seed.otherwise));
    assert(r2_script_condition_read(&state,&seed,4)); r[1]=0;
    r2_script_trigger_tick(&state,&enter,p,r,0); assert(r[1]==0);
    r2_script_trigger_tick(&state,&enter,p,r,1); assert(r[1]==0);
    r2_script_trigger_tick(&state,&enter,p,r,0); assert(r[1]==45);
    r2_script_trigger_tick(&state,&enter,p,r,0); assert(r[1]==45);
    seed.otherwise.rotation[1]=1; assert(!r2_script_condition_read(&state,&seed,4));
    seed.operation=11; seed.otherwise.position[0]=1; assert(!r2_script_condition_read(&state,&seed,4));
    seed.otherwise.position[0]=0; seed.otherwise.rotation[1]=NAN; assert(!r2_script_condition_read(&state,&seed,4));
    seed.otherwise.rotation[1]=0; seed.operation=12; assert(!r2_script_condition_read(&state,&seed,4));
    puts("PASS: paired enter/exit cycles, stay/outside suppression, reload, masks, exit-only, v33 rejection, malformed v34 records");
}
