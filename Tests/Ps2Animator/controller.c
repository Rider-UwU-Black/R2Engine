#include <assert.h>
#include <stdio.h>
#include "../../R2Engine.PS2/include/r2_animation_controller.h"
int main(void)
{
    R2AnimController c={0}; c.parameter_count=3; c.transition_count=1;
    c.parameters[0].type=0; strcpy(c.parameters[0].name,"Speed");
    c.parameters[1].type=2; strcpy(c.parameters[1].name,"Grounded");
    c.parameters[2].type=3; strcpy(c.parameters[2].name,"Jump");
    float a[32]={0}, b[32]={0};
    R2AnimTransition *t=&c.transitions[0]; t->from=0;t->to=1;t->parameter=0;t->threshold=.5f;
    assert(r2_anim_set(&c,a,"Speed",0,1)); assert(b[0]==0);
    assert(!r2_anim_set(&c,a,"Speed",2,1)); assert(!r2_anim_set(&c,a,"missing",0,1));
    assert(!r2_anim_set(&c,a,"Speed",0,NAN));
    t->condition=2;assert(r2_anim_transition(&c,a,0,0)==0);assert(r2_anim_transition(&c,b,0,0)==-1);
    t->condition=3;assert(r2_anim_transition(&c,b,0,0)==0);assert(r2_anim_transition(&c,a,0,0)==-1);
    t->condition=4;a[0]=.5f;assert(r2_anim_transition(&c,a,0,0)==0);
    a[0]=.5002f;assert(r2_anim_transition(&c,a,0,0)==-1);
    t->parameter=1;t->condition=1;assert(r2_anim_transition(&c,a,0,0)==0);
    assert(r2_anim_set(&c,a,"Grounded",2,1));t->condition=0;assert(r2_anim_transition(&c,a,0,0)==0);
    t->condition=6;assert(r2_anim_transition(&c,a,0,0)==-1);assert(r2_anim_transition(&c,a,0,1)==0);
    t->parameter=2;t->condition=5;assert(r2_anim_set(&c,a,"Jump",3,1));
    assert(r2_anim_transition(&c,a,1,0)==-1 && a[2]==1);
    assert(r2_anim_transition(&c,a,0,0)==0 && a[2]==0);
    assert(r2_anim_transition(&c,a,0,0)==-1);
    c.transition_count=2;c.transitions[1]=*t;t->condition=6;a[2]=1;
    assert(r2_anim_transition(&c,a,0,1)==0 && a[2]==1); /* losing trigger untouched */
    assert(r2_anim_transition(&c,a,0,0)==1 && a[2]==0);
    puts("PASS: all conditions, authored priority, trigger consumption, setters and independent instances.");
}
