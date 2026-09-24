#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptTimer seed={0,0,18,{{0,2,0},{0,0,45}}},state,other=seed;
    R2ScriptMotion rate={{0},{0,30,0}};
    float p[3]={0},r[3]={0};
    assert(r2_script_condition_read(&state,&seed,11));
    assert(!r2_script_condition_read(&state,&seed,10));
    state=seed; r2_script_start_apply(&state,p,r);
    assert(p[1]==2 && r[2]==45 && state.elapsed==1 && other.elapsed==0);
    r2_script_start_apply(&state,p,r); assert(p[1]==2 && r[2]==45);
    r2_script_timer_tick(&state,&rate,p,r,0.5f); assert(r[1]==15 && r[2]==45);
    r2_script_timer_tick(&state,&rate,p,r,0); assert(r[1]==15 && state.elapsed==1);
    for(int i=0;i<10;i++)r2_script_timer_tick(&state,&rate,p,r,0.5f);
    assert(r[1]==165 && r[2]==45 && p[1]==2);
    p[1]=r[1]=r[2]=0; state=seed; r2_script_start_apply(&state,p,r);
    assert(p[1]==2 && r[2]==45 && r[1]==0);
    seed.threshold=1; assert(!r2_script_condition_read(&state,&seed,11));
    seed.threshold=0;seed.elapsed=1;assert(!r2_script_condition_read(&state,&seed,11));
    puts("PASS: Start once, Update afterward, pause, reload, instance isolation, version validation");
}
