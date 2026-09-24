#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptTimer seed={0,1,12,{{0},{0}}}, state=seed, other=seed, parsed;
    R2ScriptMotion on={{0,0,0},{0,90,0}};
    float p[3]={0},r[3]={0};
    R2ScriptInputEdges edges={0};
    const uint16_t button=0x800;
    assert(r2_script_condition_read(&parsed,&seed,5));
    assert(!r2_script_condition_read(&parsed,&seed,4));
    assert(!r2_script_is_trigger(12));
    r2_script_pressed(&edges,0,1,0);
    r2_script_toggle_tick(&state,&on,p,r,0.5f,r2_script_pressed(&edges,button,1,0)&button);
    assert(state.elapsed==1 && r[1]==45 && other.elapsed==0);
    r2_script_toggle_tick(&state,&on,p,r,0.5f,r2_script_pressed(&edges,button,1,0)&button);
    assert(state.elapsed==1 && r[1]==90);
    r2_script_toggle_tick(&state,&on,p,r,0.5f,r2_script_pressed(&edges,0,1,0)&button);
    assert(state.elapsed==1 && r[1]==135);
    r2_script_toggle_tick(&state,&on,p,r,0.5f,r2_script_pressed(&edges,button,1,0)&button);
    assert(state.elapsed==0 && r[1]==135);
    state.otherwise.position[0]=4;
    r2_script_toggle_tick(&state,&on,p,r,0.5f,0); assert(p[0]==2);
    assert(!r2_script_pressed(&edges,0,1,1));
    assert(!r2_script_pressed(&edges,button,1,0));
    assert(!r2_script_pressed(&edges,0,0,0));
    assert(!r2_script_pressed(&edges,button,1,0));
    seed.elapsed=1; assert(r2_script_condition_read(&parsed,&seed,5));
    r2_script_toggle_tick(&parsed,&on,p,r,0.5f,0); assert(r[1]==180);
    seed.elapsed=0.5f; assert(!r2_script_condition_read(&parsed,&seed,5));
    seed.elapsed=0; seed.threshold=16; assert(!r2_script_condition_read(&parsed,&seed,5));
    seed.threshold=1.5f; assert(!r2_script_condition_read(&parsed,&seed,5));
    seed.threshold=1; assert(r2_script_condition_read(&state,&seed,5) && state.elapsed==0);
    puts("PASS: toggle edges, persistence, branches, instance isolation, initial/reset state, validation");
}
