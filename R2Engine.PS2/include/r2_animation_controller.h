#ifndef R2_ANIMATION_CONTROLLER_H
#define R2_ANIMATION_CONTROLLER_H
#include <math.h>
#include <stdlib.h>
#include <string.h>

/* Match PlayerController: held sprint is animation intent only while moving.
   Use the same threshold as the native Moving parameter to avoid contradictory
   Idle->Run (Sprinting) and Run->Idle (!Moving) transitions. */
static inline int r2_anim_sprinting(int enabled, int button_down, float movement_length)
{ return enabled && button_down && movement_length > 0.0001f; }
typedef struct { unsigned int type, binding; float initial; char name[64]; } R2AnimParameter;
typedef struct { unsigned int from, to, condition, parameter; float threshold, blend; } R2AnimTransition;
typedef struct {
    unsigned int parameter_count, transition_count;
    R2AnimParameter parameters[32];
    R2AnimTransition transitions[128];
} R2AnimController;
static inline int r2_anim_set(const R2AnimController *c, float *values, const char *name, unsigned int type, float value)
{
    if (!c || !values || !name || !isfinite(value)) return 0;
    for (unsigned int i=0; i<c->parameter_count; i++)
        if (c->parameters[i].type == type && strcmp(c->parameters[i].name,name)==0)
        { values[i]=value; return 1; }
    return 0;
}
/* Authored order, one transition per update, consume only the winning trigger. */
static inline int r2_anim_transition(const R2AnimController *c, float *values, unsigned int state, int finished)
{
    if (!c || !values) return -1;
    for (unsigned int i=0; i<c->transition_count; i++)
    {
        const R2AnimTransition *t=&c->transitions[i];
        if (t->from!=state) continue;
        float v=t->parameter<c->parameter_count ? values[t->parameter] : 0;
        int pass=0;
        switch(t->condition) {
            case 0: pass=v!=0; break; case 1: pass=v==0; break;
            case 2: pass=v>t->threshold; break; case 3: pass=v<t->threshold; break;
            case 4: pass=fabsf(v-t->threshold)<0.0001f; break;
            case 5: pass=v!=0; break; case 6: pass=finished; break;
            case 7: pass=finished && ((float)rand()/(float)RAND_MAX)<t->threshold; break;
        }
        if (pass) { if(t->condition==5) values[t->parameter]=0; return (int)i; }
    }
    return -1;
}
#endif
