#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include "../../R2Engine.PS2/include/r2_character_sweep.h"

typedef struct { R2CamVec a,b,c,d; float radius; } Wall;
static R2CharacterHit sweep(void *opaque,R2CamVec root,R2CamVec movement)
{
    Wall *w=(Wall *)opaque;float length=cv_len(movement);
    R2CharacterHit hit={length,{0,0,0}};if(length<1e-6f)return hit;
    R2CamVec p={root.x,root.y-.5f,root.z},q={root.x,root.y+.5f,root.z};
    R2CamVec dir=cv_mul(movement,1/length);
    character_triangle_sweep(p,q,dir,w->radius,w->a,w->b,w->c,&hit);
    character_triangle_sweep(p,q,dir,w->radius,w->a,w->c,w->d,&hit);
    return hit;
}
static void near(float actual,float expected,const char *message)
{if(fabsf(actual-expected)>.006f){fprintf(stderr,"%s: %.4f expected %.4f\n",message,actual,expected);exit(1);}}
int main(void)
{
    if(!character_triangle_walkable((R2CamVec){-5,0,-5},(R2CamVec){5,0,-5},(R2CamVec){5,0,5},.707f))
        {fputs("flat ground was not classified as walkable\n",stderr);return 1;}
    if(character_triangle_walkable((R2CamVec){1,-2,-5},(R2CamVec){1,2,-5},(R2CamVec){1,2,5},.707f))
        {fputs("wall was classified as walkable\n",stderr);return 1;}
    Wall wall={{1,-2,-5},{1,2,-5},{1,2,5},{1,-2,5},.5f};
    R2CamVec hit=character_slide(&wall,sweep,(R2CamVec){0,0,0},(R2CamVec){2,0,0});
    near(hit.x,.499f,"straight wall contact");near(hit.z,0,"straight wall drift");
    hit=character_slide(&wall,sweep,(R2CamVec){0,0,0},(R2CamVec){2,0,2});
    near(hit.x,.499f,"diagonal wall contact");near(hit.z,2,"diagonal wall slide");
    hit=character_slide(&wall,sweep,(R2CamVec){.55f,0,0},(R2CamVec){-1,0,0});
    near(hit.x,-1,"separating overlap escape");
    hit=character_slide(&wall,sweep,(R2CamVec){0,0,0},(R2CamVec){0,0,2});
    near(hit.z,2,"parallel travel");
    puts("PASS: native capsule blocks walls, slides along them, and escapes existing overlap");
    return 0;
}
