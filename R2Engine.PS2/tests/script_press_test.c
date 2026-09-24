#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptInputEdges edges={0};
    const uint16_t button=0x800;
    R2ScriptMotion step={{0,0,0},{0,90,0}};
    float p[3]={0},r[3]={0},other[3]={0};
    assert(!r2_script_pressed(&edges,button,1,0)); /* first sample held */
    assert(!r2_script_pressed(&edges,button,1,0));
    assert(!r2_script_pressed(&edges,0,1,0));
    uint16_t press=r2_script_pressed(&edges,button,1,0); assert(press==button);
    r2_script_press_tick(&step,p,r,press & button);
    r2_script_press_tick(&step,p,other,press & button); /* non-consuming */
    assert(r[1]==90 && other[1]==90);
    for(int i=0;i<120;++i) r2_script_press_tick(&step,p,r,r2_script_pressed(&edges,button,1,0)&button);
    assert(r[1]==90);
    r2_script_pressed(&edges,0,1,0);
    r2_script_press_tick(&step,p,r,r2_script_pressed(&edges,button,1,0)&button); assert(r[1]==180);
    assert(!r2_script_pressed(&edges,0,1,1)); /* pause, then press on resume */
    assert(!r2_script_pressed(&edges,button,1,0));
    assert(!r2_script_pressed(&edges,0,0,0)); /* disconnect */
    assert(!r2_script_pressed(&edges,button,1,0)); /* reconnect held */
    memset(&edges,0,sizeof(edges)); assert(!r2_script_pressed(&edges,button,1,0)); /* load */
    R2ScriptTimer seed={0,1,8,{{0},{0}}}, parsed;
    assert(!r2_script_condition_read(&parsed,&seed,1));
    assert(r2_script_condition_read(&parsed,&seed,2));
    seed.otherwise.position[0]=1; assert(!r2_script_condition_read(&parsed,&seed,2));
    seed.otherwise.position[0]=0; seed.operation=9; assert(!r2_script_condition_read(&parsed,&seed,2));
    puts("PASS: fixed steps, held suppression, release/repress, shared edges, scene/pause/reconnect baselines, v32 validation");
}
