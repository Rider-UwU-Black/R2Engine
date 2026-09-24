#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
int main(void)
{
    R2ScriptTimer seed={0,1,17,{{-2,0,0},{0}}},parsed;
    R2ScriptMotion first={{2,0,0},{0}};
    float p[3]={0},r[3]={0};
    assert(r2_script_condition_read(&parsed,&seed,10));
    assert(!r2_script_condition_read(&parsed,&seed,9));
    r2_script_dual_held_tick(&seed,&first,p,r,0.5f,1,0); assert(p[0]==1);
    r2_script_dual_held_tick(&seed,&first,p,r,0.5f,1,0); assert(p[0]==2);
    r2_script_dual_held_tick(&seed,&first,p,r,0.5f,0,1); assert(p[0]==1);
    r2_script_dual_held_tick(&seed,&first,p,r,0.5f,1,1); assert(p[0]==1);
    r2_script_dual_held_tick(&seed,&first,p,r,0.5f,0,0); assert(p[0]==1);
    r2_script_dual_held_tick(&seed,&first,p,r,0,1,1); assert(p[0]==1);
    r2_script_dual_held_tick(&seed,&first,p,r,NAN,1,1); assert(p[0]==1);
    seed.otherwise.position[0]=4;
    r2_script_dual_held_tick(&seed,&first,p,r,0.5f,1,1); assert(p[0]==4);
    assert(seed.elapsed==0);
    for(int i=0;i<256;i++){seed.threshold=(float)i;assert(r2_script_condition_read(&parsed,&seed,10));}
    seed.threshold=256;assert(!r2_script_condition_read(&parsed,&seed,10));
    seed.threshold=1.5f;assert(!r2_script_condition_read(&parsed,&seed,10));
    seed.threshold=1;seed.elapsed=1;assert(!r2_script_condition_read(&parsed,&seed,10));
    puts("PASS: dual held continuity, cancellation, independent rates, release stop, dt guards, record validation");
}
