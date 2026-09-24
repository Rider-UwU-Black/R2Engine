#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptTimer seed={0,1,19,{{0},{0,0,45}}}, state;
    R2ScriptMotion rate={{0},{0,30,0}};
    float p[3]={0},r[3]={0};
    assert(r2_script_condition_read(&state,&seed,12));
    assert(!r2_script_condition_read(&state,&seed,11));
    state=seed;r2_script_start_apply(&state,p,r);assert(r[2]==45 && state.elapsed==1);
    for(int i=0;i<10;i++)r2_script_start_held_tick(&rate,p,r,0.5f,0);
    assert(r[1]==0 && r[2]==45);
    r2_script_start_held_tick(&rate,p,r,0.5f,1);assert(r[1]==15 && r[2]==45);
    r2_script_start_held_tick(&rate,p,r,0.5f,1);assert(r[1]==30);
    r2_script_start_held_tick(&rate,p,r,0.5f,0);assert(r[1]==30);
    r2_script_start_held_tick(&rate,p,r,0,1);assert(r[1]==30);
    r2_script_start_apply(&state,p,r);assert(r[2]==45);
    r[1]=r[2]=0;state=seed;r2_script_start_apply(&state,p,r);assert(r[1]==0 && r[2]==45);
    seed.threshold=16;assert(!r2_script_condition_read(&state,&seed,12));
    seed.threshold=1.5f;assert(!r2_script_condition_read(&state,&seed,12));
    seed.threshold=1;seed.elapsed=1;assert(!r2_script_condition_read(&state,&seed,12));
    puts("PASS: one-time startup, idle/held/released motion, pause dt, reload, malformed record rejection");
}
