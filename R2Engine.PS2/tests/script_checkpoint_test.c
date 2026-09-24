#include <assert.h>
#include <math.h>
#include <stdio.h>
#include "../include/r2_script_checkpoint.h"
int main(void)
{
    int value=0;unsigned flag=0;
    R2ScriptTimer toggle={1,0,12,{{0},{0}}};
    assert(r2_script_checkpoint_capture(&toggle,0,&value,1,&flag)&&flag==1);
    toggle.elapsed=0;assert(r2_script_checkpoint_restore(&toggle,0,0,1,flag)&&toggle.elapsed==1);
    R2ScriptTimer timer={2.345f,3,3,{{0},{0}}};
    assert(r2_script_checkpoint_capture(&timer,1,&value,0,&flag)&&value==2345);
    timer.elapsed=0;assert(r2_script_checkpoint_restore(&timer,1,value,0,0));assert(fabsf(timer.elapsed-2.345f)<.001f);
    R2ScriptTimer loop={7.25f,2,13,{{0},{0}}};
    assert(r2_script_checkpoint_capture(&loop,1,&value,0,&flag));loop.elapsed=0;
    assert(r2_script_checkpoint_restore(&loop,1,value,0,0)&&fabsf(loop.elapsed-3.25f)<.001f);
    R2ScriptTimer held={0,1,7,{{0},{0}}};assert(!r2_script_checkpoint_capture(&held,1,&value,0,&flag));
    assert(!r2_script_checkpoint_capture(&timer,0,&value,1,&flag));
    puts("PASS: toggle and timer script state capture/restore; loop normalization and incompatible-state rejection");
    return 0;
}
