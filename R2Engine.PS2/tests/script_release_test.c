#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
static uint16_t frame(R2ScriptInputEdges *e,uint16_t b,int connected,int paused)
{
    uint16_t released=r2_script_released(e,b,connected,paused);
    r2_script_pressed(e,b,connected,paused);
    return released;
}
int main(void)
{
    R2ScriptInputEdges e={0}; const uint16_t button=0x800;
    R2ScriptMotion step={{0},{0,45,0}}; float p[3]={0},r[3]={0},other[3]={0};
    assert(!frame(&e,0,1,0)); assert(!frame(&e,button,1,0));
    assert(!frame(&e,button,1,0));
    uint16_t released=frame(&e,0,1,0); assert(released==button);
    r2_script_press_tick(&step,p,r,released&button);
    r2_script_press_tick(&step,p,other,released&button); assert(r[1]==45 && other[1]==45);
    assert(!frame(&e,0,1,0)); assert(!frame(&e,button,1,0));
    assert(frame(&e,0,1,0)==button);
    frame(&e,button,1,0); assert(!frame(&e,0,0,0)); /* disconnect */
    assert(!frame(&e,0,1,0));
    frame(&e,button,1,0); assert(!frame(&e,0,1,1)); /* released while paused */
    assert(!frame(&e,0,1,0));
    memset(&e,0,sizeof(e)); assert(!frame(&e,0,1,0));
    assert(!frame(&e,button,1,0)); assert(frame(&e,0,1,0)==button);
    R2ScriptTimer seed={0,1,15,{{0},{0}}},parsed;
    assert(r2_script_condition_read(&parsed,&seed,8));
    assert(!r2_script_condition_read(&parsed,&seed,7));
    seed.threshold=16; assert(!r2_script_condition_read(&parsed,&seed,8));
    seed.threshold=1.5f; assert(!r2_script_condition_read(&parsed,&seed,8));
    seed.threshold=1; seed.otherwise.rotation[1]=1; assert(!r2_script_condition_read(&parsed,&seed,8));
    puts("PASS: release once, shared edges, re-release, disconnect/pause/load suppression, validation");
}
