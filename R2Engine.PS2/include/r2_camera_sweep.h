#ifndef R2_CAMERA_SWEEP_H
#define R2_CAMERA_SWEEP_H
#include <math.h>
typedef struct { float x,y,z; } R2CamVec;
static inline R2CamVec cv_add(R2CamVec a,R2CamVec b) { return (R2CamVec){a.x+b.x,a.y+b.y,a.z+b.z}; }
static inline R2CamVec cv_sub(R2CamVec a,R2CamVec b) { return (R2CamVec){a.x-b.x,a.y-b.y,a.z-b.z}; }
static inline R2CamVec cv_mul(R2CamVec a,float b) { return (R2CamVec){a.x*b,a.y*b,a.z*b}; }
static inline float cv_dot(R2CamVec a,R2CamVec b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
static inline float cv_len(R2CamVec a) { return sqrtf(cv_dot(a,a)); }
/* A radius overlap at the target is not an obstruction behind it when the
   ray separates from this convex feature and ends fully clear. True geometry
   penetration, inward/tangent rays and obstructed endpoints stay conservative. */
static inline int camera_separating_overlap(R2CamVec separation,float distance,R2CamVec dir,float radius,float end_distance)
{
    return distance>=0 && distance<=radius+0.001f && cv_dot(separation,dir)>0.0000001f && end_distance>radius+0.001f;
}
static inline R2CamVec cv_closest(R2CamVec p,R2CamVec a,R2CamVec b,R2CamVec c)
{
    R2CamVec ab=cv_sub(b,a),ac=cv_sub(c,a),ap=cv_sub(p,a);
    float d1=cv_dot(ab,ap),d2=cv_dot(ac,ap);
    if(d1<=0 && d2<=0) return a;
    R2CamVec bp=cv_sub(p,b);
    float d3=cv_dot(ab,bp),d4=cv_dot(ac,bp);
    if(d3>=0 && d4<=d3) return b;
    float vc=d1*d4-d3*d2;
    if(vc<=0 && d1>=0 && d3<=0) return cv_add(a,cv_mul(ab,d1/(d1-d3)));
    R2CamVec cp=cv_sub(p,c);
    float d5=cv_dot(ab,cp),d6=cv_dot(ac,cp);
    if(d6>=0 && d5<=d6) return c;
    float vb=d5*d2-d1*d6;
    if(vb<=0 && d2>=0 && d6<=0) return cv_add(a,cv_mul(ac,d2/(d2-d6)));
    float va=d3*d6-d5*d4;
    if(va<=0 && d4-d3>=0 && d5-d6>=0) return cv_add(b,cv_mul(cv_sub(c,b),(d4-d3)/((d4-d3)+(d5-d6))));
    float denom=va+vb+vc;
    if(fabsf(denom)<1e-20f) return a;
    return cv_add(a,cv_add(cv_mul(ab,vb/denom),cv_mul(ac,vc/denom)));
}
static inline float camera_triangle_sweep(R2CamVec origin,R2CamVec dir,float limit,float radius,R2CamVec a,R2CamVec b,R2CamVec c)
{
    R2CamVec end=cv_add(origin,cv_mul(dir,limit));
    if(fmaxf(a.x,fmaxf(b.x,c.x))<fminf(origin.x,end.x)-radius || fminf(a.x,fminf(b.x,c.x))>fmaxf(origin.x,end.x)+radius ||
       fmaxf(a.y,fmaxf(b.y,c.y))<fminf(origin.y,end.y)-radius || fminf(a.y,fminf(b.y,c.y))>fmaxf(origin.y,end.y)+radius ||
       fmaxf(a.z,fmaxf(b.z,c.z))<fminf(origin.z,end.z)-radius || fminf(a.z,fminf(b.z,c.z))>fmaxf(origin.z,end.z)+radius) return limit;
    R2CamVec separation=cv_sub(origin,cv_closest(origin,a,b,c));
    if(camera_separating_overlap(separation,cv_len(separation),dir,radius,cv_len(cv_sub(end,cv_closest(end,a,b,c)))))return limit;
    float travel=0;
    for(int i=0;i<48 && travel<limit;i++)
    {
        R2CamVec p=cv_add(origin,cv_mul(dir,travel));
        float gap=cv_len(cv_sub(p,cv_closest(p,a,b,c)))-radius;
        if(gap<=0.001f) return travel;
        travel+=gap;
    }
    return fminf(travel,limit);
}
static inline float camera_box_sweep(R2CamVec origin,R2CamVec dir,float limit,float radius,R2CamVec half)
{
    R2CamVec separation={origin.x-fmaxf(-half.x,fminf(half.x,origin.x)),origin.y-fmaxf(-half.y,fminf(half.y,origin.y)),origin.z-fmaxf(-half.z,fminf(half.z,origin.z))};
    R2CamVec end=cv_add(origin,cv_mul(dir,limit));
    R2CamVec end_gap={fmaxf(0,fabsf(end.x)-half.x),fmaxf(0,fabsf(end.y)-half.y),fmaxf(0,fabsf(end.z)-half.z)};
    if(camera_separating_overlap(separation,cv_len(separation),dir,radius,cv_len(end_gap)))return limit;
    float travel=0;
    for(int i=0;i<48 && travel<limit;i++)
    {
        R2CamVec p=cv_add(origin,cv_mul(dir,travel));
        R2CamVec gapv={fmaxf(0,fabsf(p.x)-half.x),fmaxf(0,fabsf(p.y)-half.y),fmaxf(0,fabsf(p.z)-half.z)};
        float gap=cv_len(gapv)-radius;
        if(gap<=0.001f) return travel;
        travel+=gap;
    }
    return fminf(travel,limit);
}
static inline float camera_capsule_sweep(R2CamVec origin,R2CamVec dir,float limit,float radius,float half,float capsule_radius)
{
    R2CamVec separation=origin;separation.y-=fmaxf(-half,fminf(half,separation.y));
    R2CamVec end=cv_add(origin,cv_mul(dir,limit));end.y-=fmaxf(-half,fminf(half,end.y));
    if(camera_separating_overlap(separation,cv_len(separation)-capsule_radius,dir,radius,cv_len(end)-capsule_radius))return limit;
    float travel=0;
    for(int i=0;i<48 && travel<limit;i++)
    {
        R2CamVec p=cv_add(origin,cv_mul(dir,travel));p.y-=fmaxf(-half,fminf(half,p.y));
        float gap=cv_len(p)-capsule_radius-radius;
        if(gap<=0.001f)return travel;
        travel+=gap;
    }
    return fminf(travel,limit);
}
#endif
