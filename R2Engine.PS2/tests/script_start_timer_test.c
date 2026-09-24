#include "r2_script_timer.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
int main(int argc,char **argv)
{
    R2ScriptMotion startup={{0},{0,0,45}},rate={{0},{0,30,0}};
    R2ScriptTimer seed={0,2,4,{{0},{0}}},timer=seed;
    float p[3]={0},r[3]={0};
    if(argc==2) {
        FILE *f=fopen(argv[1],"rb");assert(f);fseek(f,0,SEEK_END);long n=ftell(f);rewind(f);assert(n>=184);
        unsigned char *b=malloc((size_t)n);assert(b);assert(fread(b,1,(size_t)n,f)==(size_t)n);fclose(f);
        uint32_t version,count;memcpy(&version,b+4,4);memcpy(&count,b+16,4);assert(version==43 && count==2);
        assert(r2_script_motion_read(&startup,b+n-48));
        assert(r2_script_motion_read(&rate,b+n-168));
        assert(r2_script_condition_read(&seed,b+n-120,12));
        assert(seed.operation==4 && seed.elapsed==0 && seed.threshold==2 && startup.rotation[2]==90 && rate.rotation[1]==90);
        free(b);timer=seed;
    }
    r2_script_motion_tick(&startup,p,r,1);assert(timer.elapsed==0 && r[2]==startup.rotation[2]);
    r2_script_timer_tick(&timer,&rate,p,r,1);assert(timer.elapsed==1 && r[1]==0);
    r2_script_timer_tick(&timer,&rate,p,r,0);assert(timer.elapsed==1 && r[1]==0);
    r2_script_timer_tick(&timer,&rate,p,r,1);assert(timer.elapsed==2 && r[1]==rate.rotation[1]);
    r2_script_timer_tick(&timer,&rate,p,r,1);assert(r[1]==2*rate.rotation[1] && r[2]==startup.rotation[2]);
    memset(r,0,sizeof(r));timer=seed;r2_script_motion_tick(&startup,p,r,1);
    assert(timer.elapsed==0 && r[1]==0 && r[2]==startup.rotation[2]);
    R2ScriptMotion invalid=startup,parsed;invalid.rotation[2]=NAN;assert(!r2_script_motion_read(&parsed,&invalid));
    puts("PASS: independent startup/timer, delay, pause, ongoing motion, reload, startup validation, optional v43 fixture");
}
