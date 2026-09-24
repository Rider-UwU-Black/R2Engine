#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptTimer seed={0,1,7,{{-2,0,0},{0,0,0}}}, condition;
    assert(!r2_script_timer_read(&condition,&seed)); /* v30 must reject new opcode */
    assert(r2_script_condition_read(&condition,&seed,1));
    R2ScriptMotion held={{0,0,0},{0,90,0}};
    float p[3]={0},r[3]={0};
    r2_script_input_tick(&condition,&held,p,r,0.5f,0); assert(p[0]==-1 && r[1]==0);
    r2_script_input_tick(&condition,&held,p,r,0.5f,1); assert(p[0]==-1 && r[1]==45);
    r2_script_input_tick(&condition,&held,p,r,0.5f,1); assert(r[1]==90); /* held, not edge */
    r2_script_input_tick(&condition,&held,p,r,0.5f,0); assert(p[0]==-2 && r[1]==90);
    r2_script_input_tick(&condition,&held,p,r,0,1); assert(r[1]==90);
    assert(condition.elapsed==0);
    for(int button=0;button<16;++button) { seed.threshold=(float)button; assert(r2_script_condition_read(&condition,&seed,1)); }
    const float invalid[]={-1,16,0.5f,NAN,INFINITY};
    for(unsigned i=0;i<sizeof(invalid)/sizeof(invalid[0]);++i)
    { seed.threshold=invalid[i]; assert(!r2_script_condition_read(&condition,&seed,1)); }
    seed.threshold=1; seed.elapsed=1; assert(!r2_script_condition_read(&condition,&seed,1));
    seed.elapsed=0; seed.operation=8; assert(!r2_script_condition_read(&condition,&seed,1));
    puts("PASS: held/released branches, continuous hold, pause dt, no timer mutation, all button indices, malformed records, v30 opcode rejection");
}
