#ifndef R2_SCRIPT_CHECKPOINT_H
#define R2_SCRIPT_CHECKPOINT_H
#include <limits.h>
#include <math.h>
#include "r2_script_timer.h"
static inline int r2_script_checkpoint_capture(R2ScriptTimer *script,int save_integer,int *integer_value,
    int save_boolean,unsigned *boolean_value)
{
    if(!script)return 1;
    if(save_boolean){if(script->operation!=12u)return 0;*boolean_value=script->elapsed!=0;}
    if(save_integer){
        if(!((script->operation>=1u && script->operation<=6u)||script->operation==13u) ||
            script->elapsed<0 || script->elapsed>(float)INT_MAX/1000.0f)return 0;
        *integer_value=(int)lroundf(script->elapsed*1000.0f);
    }
    return 1;
}
static inline int r2_script_checkpoint_restore(R2ScriptTimer *script,int save_integer,int integer_value,
    int save_boolean,unsigned boolean_value)
{
    if(!script)return 1;
    if(save_boolean){if(script->operation!=12u || boolean_value>1u)return 0;script->elapsed=boolean_value?1.0f:0.0f;}
    if(save_integer){
        if(!((script->operation>=1u && script->operation<=6u)||script->operation==13u) || integer_value<0)return 0;
        float elapsed=(float)integer_value/1000.0f;
        if(script->operation==13u)elapsed=fmodf(elapsed,script->threshold*2.0f);
        script->elapsed=elapsed;
    }
    return 1;
}
#endif
