#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptTimer seed={0,1,14,{{0},{0,-45,0}}}, state;
    R2ScriptMotion first={{0},{0,45,0}};
    float p[3]={0},r[3]={0};
    assert(r2_script_condition_read(&state,&seed,7));
    assert(!r2_script_condition_read(&state,&seed,6));
    assert(!r2_script_is_trigger(14));
    r2_script_dual_press_tick(&seed,&first,p,r,1,0); assert(r[1]==45);
    r2_script_dual_press_tick(&seed,&first,p,r,0,1); assert(r[1]==0);
    r2_script_dual_press_tick(&seed,&first,p,r,1,1); assert(r[1]==0);
    R2ScriptInputEdges edges={0};
    r2_script_pressed(&edges,0,1,0);
    uint16_t pressed=r2_script_pressed(&edges,3,1,0); assert(pressed==3);
    r2_script_dual_press_tick(&seed,&first,p,r,pressed&1,pressed&2); assert(r[1]==0);
    assert(!r2_script_pressed(&edges,3,1,0));
    assert(!r2_script_pressed(&edges,1,1,0));
    pressed=r2_script_pressed(&edges,3,1,0); assert(pressed==2);
    r2_script_dual_press_tick(&seed,&first,p,r,pressed&1,pressed&2); assert(r[1]==-45);
    /* Both statements must execute rather than selecting a single branch. */
    seed.otherwise.position[0]=2;
    r2_script_dual_press_tick(&seed,&first,p,r,1,1); assert(r[1]==-45 && p[0]==2);
    for(int i=0;i<256;i++) { seed.threshold=(float)i; assert(r2_script_condition_read(&state,&seed,7)); }
    seed.threshold=256; assert(!r2_script_condition_read(&state,&seed,7));
    seed.threshold=-1; assert(!r2_script_condition_read(&state,&seed,7));
    seed.threshold=1.5f; assert(!r2_script_condition_read(&state,&seed,7));
    seed.threshold=1; seed.elapsed=1; assert(!r2_script_condition_read(&state,&seed,7));
    puts("PASS: independent/simultaneous presses, held suppression, per-button re-press, paired steps, packed binding validation");
}
