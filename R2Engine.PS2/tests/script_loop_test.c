#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptTimer seed={0,2,13,{{-2,0,0},{0}}}, state, other=seed;
    R2ScriptMotion on={{2,0,0},{0}};
    float p[3]={0},r[3]={0};
    assert(!r2_script_condition_read(&state,&seed,5));
    assert(r2_script_condition_read(&state,&seed,6));
    r2_script_timer_tick(&state,&on,p,r,1); assert(state.elapsed==1 && p[0]==2);
    r2_script_timer_tick(&state,&on,p,r,1); assert(state.elapsed==2 && p[0]==0);
    r2_script_timer_tick(&state,&on,p,r,1); assert(state.elapsed==3 && p[0]==-2);
    r2_script_timer_tick(&state,&on,p,r,1); assert(state.elapsed==0 && p[0]==0);
    for(int i=0;i<400;++i) r2_script_timer_tick(&state,&on,p,r,1);
    assert(state.elapsed==0 && p[0]==0 && other.elapsed==0);
    r2_script_timer_tick(&state,&on,p,r,9); assert(state.elapsed==1 && p[0]==18);
    r2_script_timer_tick(&state,&on,p,r,0); assert(state.elapsed==1 && p[0]==18);
    r2_script_timer_tick(&state,&on,p,r,NAN); assert(state.elapsed==1);
    assert(r2_script_condition_read(&state,&seed,6) && state.elapsed==0);
    seed.threshold=0; assert(!r2_script_condition_read(&state,&seed,6));
    seed.threshold=-1; assert(!r2_script_condition_read(&state,&seed,6));
    seed.threshold=3e38f; assert(!r2_script_condition_read(&state,&seed,6));
    seed.threshold=2; seed.elapsed=-1; assert(!r2_script_condition_read(&state,&seed,6));
    puts("PASS: loop boundaries, repeated cycles, large dt, pause, reset, isolation, validation");
}
