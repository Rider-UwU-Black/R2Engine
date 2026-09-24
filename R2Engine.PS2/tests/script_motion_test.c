#include "r2_script_motion.h"
#include <assert.h>
#include <float.h>
#include <stdio.h>
int main(void)
{
    _Static_assert(sizeof(R2ScriptMotion)==24, "R2SC v29 record size");
    float raw[6]={2,0,-1,0,90,0};
    R2ScriptMotion motion;
    assert(r2_script_motion_read(&motion,raw));
    float position[3]={0}, rotation[3]={0};
    for(int i=0;i<60;++i) r2_script_motion_tick(&motion,position,rotation,1.0f/60);
    assert(fabsf(position[0]-2)<0.00001f && fabsf(position[2]+1)<0.00001f);
    assert(fabsf(rotation[1]-90)<0.0001f);
    r2_script_motion_tick(&motion,position,rotation,0);
    r2_script_motion_tick(&motion,position,rotation,NAN);
    assert(fabsf(rotation[1]-90)<0.0001f);
    for(int i=0;i<6;++i) { float saved=raw[i]; raw[i]=NAN; assert(!r2_script_motion_read(&motion,raw)); raw[i]=saved; }
    raw[0]=FLT_MAX; assert(r2_script_motion_read(&motion,raw));
    float before=rotation[1]; r2_script_motion_tick(&motion,position,rotation,FLT_MAX);
    assert(rotation[1]==before);
    memset(&motion,0,sizeof(motion)); /* old scene/new scene reset */
    r2_script_motion_tick(&motion,position,rotation,1);
    assert(rotation[1]==before);
    puts("PASS: motion layout, rates, invalid values, overflow, zero/reset behavior");
}
