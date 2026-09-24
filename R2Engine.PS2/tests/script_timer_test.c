#include "r2_script_timer.h"
#include <assert.h>
#include <float.h>
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv)
{
    _Static_assert(sizeof(R2ScriptTimer)==36, "R2SC v30 timer record size");
    R2ScriptMotion yes={{2,0,0},{0,90,0}};
    R2ScriptTimer seed={0,0.5f,4,{{-2,0,0},{0,-90,0}}}, timer;
    /* All operators below, at, above boundary AFTER timer increment. */
    const int expected[6][3]={{1,0,0},{1,1,0},{0,0,1},{0,1,1},{0,1,0},{1,0,1}};
    for(unsigned op=1;op<=6;++op)
        for(int sample=0;sample<3;++sample)
        {
            seed.operation=op; seed.elapsed=sample*0.25f;
            assert(r2_script_timer_read(&timer,&seed));
            float p[3]={0},r[3]={0};
            r2_script_timer_tick(&timer,&yes,p,r,0.25f);
            assert(timer.elapsed==(sample+1)*0.25f);
            assert(p[0]==(expected[op-1][sample]?0.5f:-0.5f));
            assert(r[1]==(expected[op-1][sample]?22.5f:-22.5f));
        }
    /* Pause/invalid dt never advances state; separate instances do not share time. */
    seed.elapsed=0; seed.operation=4; memset(&seed.otherwise,0,sizeof(seed.otherwise));
    assert(r2_script_timer_read(&timer,&seed));
    R2ScriptTimer other=timer;
    float p[3]={0},r[3]={0};
    r2_script_timer_tick(&timer,&yes,p,r,0);
    r2_script_timer_tick(&timer,&yes,p,r,NAN);
    r2_script_timer_tick(&timer,&yes,p,r,-1);
    assert(timer.elapsed==0 && p[0]==0);
    r2_script_timer_tick(&timer,&yes,p,r,0.25f); assert(p[0]==0);
    r2_script_timer_tick(&timer,&yes,p,r,0.25f); assert(p[0]==0.5f);
    assert(other.elapsed==0);
    assert(r2_script_timer_read(&timer,&seed)); assert(timer.elapsed==0); /* reload */
    r2_script_timer_tick(&timer,&yes,p,r,0.25f); assert(p[0]==0.5f);
    r2_script_timer_tick(NULL,&yes,p,r,0.25f); assert(p[0]==1); /* v29 */
    timer.operation=0; r2_script_timer_tick(&timer,&yes,p,r,0.25f); assert(p[0]==1.5f);
    seed.operation=7; assert(!r2_script_timer_read(&timer,&seed)); seed.operation=4;
    seed.threshold=NAN; assert(!r2_script_timer_read(&timer,&seed)); seed.threshold=0.5f;
    seed.elapsed=INFINITY; assert(!r2_script_timer_read(&timer,&seed)); seed.elapsed=0;
    for(int axis=0;axis<3;++axis)
    {
        seed.otherwise.position[axis]=NAN; assert(!r2_script_timer_read(&timer,&seed)); seed.otherwise.position[axis]=0;
        seed.otherwise.rotation[axis]=INFINITY; assert(!r2_script_timer_read(&timer,&seed)); seed.otherwise.rotation[axis]=0;
    }
    seed.elapsed=FLT_MAX; assert(r2_script_timer_read(&timer,&seed));
    float before=p[0]; r2_script_timer_tick(&timer,&yes,p,r,FLT_MAX);
    assert(timer.elapsed==FLT_MAX && p[0]==before);
    if (argc == 2)
    {
        /* Consume the actual two-instance v30 fixture made by Tests/Ps2Scripts.
           Decode the same block offsets used in the native scene loader. */
        FILE *file=fopen(argv[1],"rb"); assert(file);
        assert(fseek(file,0,SEEK_END)==0); long length=ftell(file);
        assert(length>=136 && length<1024*1024); rewind(file);
        unsigned char *bytes=malloc((size_t)length); assert(bytes);
        assert(fread(bytes,1,(size_t)length,file)==(size_t)length); fclose(file);
        uint32_t version,count; memcpy(&version,bytes+4,4); memcpy(&count,bytes+16,4);
        assert(version==30 && count==2);
        R2ScriptMotion fixture_motion[2]; R2ScriptTimer fixture_timer[2];
        for (unsigned i=0;i<2;++i)
        {
            assert(r2_script_motion_read(&fixture_motion[i],bytes+length-count*60u+i*24u));
            assert(r2_script_timer_read(&fixture_timer[i],bytes+length-count*36u+i*36u));
        }
        assert(fixture_motion[0].rotation[1]==90 && fixture_timer[0].threshold==3.5f && fixture_timer[0].operation==4);
        float fp[3]={0},fr[3]={0},sp[3]={0},sr[3]={0};
        for(int frame=0;frame<6;++frame) r2_script_timer_tick(&fixture_timer[0],&fixture_motion[0],fp,fr,0.5f);
        assert(fr[1]==0 && fixture_timer[1].elapsed==0);
        r2_script_timer_tick(&fixture_timer[0],&fixture_motion[0],fp,fr,0.5f); assert(fr[1]==45);
        r2_script_timer_tick(&fixture_timer[1],&fixture_motion[1],sp,sr,0.5f); assert(sr[1]==0);
        free(bytes);
        puts("PASS: actual C# exporter v30 package consumed and executed by native timer helpers");
    }
    puts("PASS: timer layout, all six comparison boundaries, branch motion, pause, isolation, reload, v29 compatibility, malformed records, overflow");
}
