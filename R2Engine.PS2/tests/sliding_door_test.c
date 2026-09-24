#include "r2_sliding_door.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
int main(int argc,char **argv)
{
    R2SlidingDoor seed={0,0,3.3f,2,3,{0,1.5f,0},0,0},d;
    assert(r2_door_read(&d,&seed,1));
    if(argc==2){FILE *f=fopen(argv[1],"rb");assert(f);fseek(f,0,SEEK_END);long n=ftell(f);rewind(f);unsigned char *b=malloc(n);assert(b);assert(fread(b,1,n,f)==(size_t)n);fclose(f);uint32_t version,count,num;memcpy(&version,b+4,4);memcpy(&count,b+16,4);assert(version==44&&count==5);long off=n-count*92-36;memcpy(&num,b+off,4);assert(num==1);assert(r2_door_read(&d,b+off+4,count));free(b);}
    float p[3]={0,1.5f,0},player[3]={0,0,2};assert(r2_door_distance(&d,player)<d.range*d.range);
    r2_door_tick(&d,p,.1f);assert(p[1]==1.5f);d.opening=1;r2_door_tick(&d,p,.5f);assert(p[1]==2.5f);
    r2_door_tick(&d,p,0);assert(p[1]==2.5f);r2_door_tick(&d,p,NAN);assert(p[1]==2.5f);
    for(int i=0;i<100;i++)r2_door_tick(&d,p,.1f);
    assert(fabsf(p[1]-4.8f)<.001f&&d.progress==d.lift);
    assert(r2_door_read(&d,&seed,1)&&!d.opening&&d.progress==0);
    seed.speed=0;assert(!r2_door_read(&d,&seed,1));seed.speed=2;seed.source=1;assert(!r2_door_read(&d,&seed,1));
    puts("PASS: range, open-only clamped motion, pause, reload, validation, optional v44 exported fixture");
}
