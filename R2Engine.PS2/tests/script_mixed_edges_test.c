#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
static void tick(R2ScriptInputEdges *e,const R2ScriptTimer *state,const R2ScriptMotion *step,
    float p[3],float r[3],uint16_t buttons,int connected,int paused)
{
    uint16_t released=r2_script_released(e,buttons,connected,paused);
    uint16_t pressed=r2_script_pressed(e,buttons,connected,paused);
    r2_script_dual_press_tick(state,step,p,r,
        r2_script_pair_edges(state,0,pressed,released)&1,
        r2_script_pair_edges(state,1,pressed,released)&1);
}
int main(void)
{
    R2ScriptTimer seed={0,512,16,{{0},{0,-45,0}}}, parsed;
    R2ScriptMotion step={{0},{0,45,0}};
    R2ScriptInputEdges e={0}; float p[3]={0},r[3]={0};
    assert(r2_script_condition_read(&parsed,&seed,9));
    assert(!r2_script_condition_read(&parsed,&seed,8));
    tick(&e,&seed,&step,p,r,0,1,0);
    for(int i=0;i<5;i++) {
        tick(&e,&seed,&step,p,r,1,1,0); assert(r[1]==45);
        tick(&e,&seed,&step,p,r,1,1,0); assert(r[1]==45);
        tick(&e,&seed,&step,p,r,0,1,0); assert(r[1]==0);
        tick(&e,&seed,&step,p,r,0,1,0); assert(r[1]==0);
    }
    seed.threshold=256;
    assert(r2_script_pair_edges(&seed,0,1,2)==2 && r2_script_pair_edges(&seed,1,1,2)==1);
    seed.threshold=768;
    assert(r2_script_pair_edges(&seed,0,1,2)==2 && r2_script_pair_edges(&seed,1,1,2)==2);
    seed.operation=14; assert(r2_script_pair_edges(&seed,0,1,2)==1);
    seed.operation=16;
    for(int i=256;i<1024;i++){seed.threshold=(float)i;assert(r2_script_condition_read(&parsed,&seed,9));}
    seed.threshold=255; assert(!r2_script_condition_read(&parsed,&seed,9));
    seed.threshold=1024; assert(!r2_script_condition_read(&parsed,&seed,9));
    seed.threshold=512.5f; assert(!r2_script_condition_read(&parsed,&seed,9));
    seed.threshold=512; seed.elapsed=1; assert(!r2_script_condition_read(&parsed,&seed,9));
    puts("PASS: paired press/release cycles, hold suppression, edge selection/order, legacy pair, validation");
}
