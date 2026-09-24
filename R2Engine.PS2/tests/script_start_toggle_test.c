#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptMotion startup={{0},{0,0,45}},rate={{0},{0,30,0}};
    R2ScriptTimer seed={0,1,12,{{0},{0}}},state=seed,other=seed;
    float p[3]={0},r[3]={0};
    r2_script_motion_tick(&startup,p,r,1);assert(state.elapsed==0 && r[2]==45);
    r2_script_toggle_tick(&state,&rate,p,r,0.5f,0);assert(r[1]==0);
    r2_script_toggle_tick(&state,&rate,p,r,0.5f,1);assert(state.elapsed==1 && r[1]==15);
    r2_script_toggle_tick(&state,&rate,p,r,0.5f,0);assert(r[1]==30);
    r2_script_toggle_tick(&state,&rate,p,r,0.5f,1);assert(state.elapsed==0 && r[1]==30);
    assert(other.elapsed==0 && r[2]==45);
    r2_script_toggle_tick(&state,&rate,p,r,0.5f,0);assert(r[1]==30 && r[2]==45);
    memset(r,0,sizeof(r));state=seed;r2_script_motion_tick(&startup,p,r,1);
    assert(state.elapsed==0 && r[1]==0 && r[2]==45);
    state.elapsed=1;r2_script_toggle_tick(&state,&rate,p,r,0.5f,0);assert(r[1]==15 && r[2]==45);
    puts("PASS: startup independent of toggle, on/off persistence, one-time tilt, instance isolation, reload, initial true");
}
