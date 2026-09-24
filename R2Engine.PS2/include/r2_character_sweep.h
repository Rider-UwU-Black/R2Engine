#ifndef R2_CHARACTER_SWEEP_H
#define R2_CHARACTER_SWEEP_H
#include "r2_camera_sweep.h"

typedef struct { float distance; R2CamVec normal; } R2CharacterHit;
static inline R2CamVec cc_cross(R2CamVec a,R2CamVec b)
{ return (R2CamVec){a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x}; }
static inline int character_triangle_walkable(R2CamVec a,R2CamVec b,R2CamVec c,float slope_cosine)
{
    R2CamVec normal=cc_cross(cv_sub(b,a),cv_sub(c,a));float length=cv_len(normal);
    return length>1e-8f && fabsf(normal.y)/length>=slope_cosine;
}
static inline float cc_clamp(float v) { return fmaxf(0,fminf(1,v)); }
/* Exact closest separation between two finite segments, including parallel ones. */
static inline R2CamVec cc_segments(R2CamVec p,R2CamVec q,R2CamVec a,R2CamVec b)
{
    R2CamVec d=cv_sub(q,p),e=cv_sub(b,a),r=cv_sub(p,a);
    float dd=cv_dot(d,d),ee=cv_dot(e,e),de=cv_dot(d,e),dr=cv_dot(d,r),er=cv_dot(e,r),s=0,t=0;
    if(dd<1e-12f) t=ee>1e-12f?cc_clamp(er/ee):0;
    else if(ee<1e-12f) s=cc_clamp(-dr/dd);
    else {
        float denom=dd*ee-de*de;
        if(denom>1e-12f)s=cc_clamp((de*er-dr*ee)/denom);
        t=(de*s+er)/ee;
        if(t<0){t=0;s=cc_clamp(-dr/dd);}
        else if(t>1){t=1;s=cc_clamp((de-dr)/dd);}
    }
    return cv_sub(cv_add(p,cv_mul(d,s)),cv_add(a,cv_mul(e,t)));
}
static inline R2CamVec cc_triangle_distance(R2CamVec p,R2CamVec q,R2CamVec a,R2CamVec b,R2CamVec c)
{
    R2CamVec n=cc_cross(cv_sub(b,a),cv_sub(c,a)),d=cv_sub(q,p);
    float denom=cv_dot(n,d);
    if(fabsf(denom)>1e-12f){
        float t=cv_dot(n,cv_sub(a,p))/denom;
        if(t>=0 && t<=1){
            R2CamVec hit=cv_add(p,cv_mul(d,t));
            if(cv_len(cv_sub(hit,cv_closest(hit,a,b,c)))<1e-5f)return (R2CamVec){0,0,0};
        }
    }
    R2CamVec best=cv_sub(p,cv_closest(p,a,b,c));
    R2CamVec candidates[4]={cv_sub(q,cv_closest(q,a,b,c)),cc_segments(p,q,a,b),cc_segments(p,q,b,c),cc_segments(p,q,c,a)};
    for(int i=0;i<4;i++)if(cv_dot(candidates[i],candidates[i])<cv_dot(best,best))best=candidates[i];
    return best;
}
static inline void character_triangle_sweep(R2CamVec p,R2CamVec q,R2CamVec dir,float radius,
    R2CamVec a,R2CamVec b,R2CamVec c,R2CharacterHit *hit)
{
    R2CamVec end=cv_mul(dir,hit->distance);
    float pad=radius+0.002f;
    if(fmaxf(a.x,fmaxf(b.x,c.x))<fminf(p.x,p.x+end.x)-pad || fminf(a.x,fminf(b.x,c.x))>fmaxf(p.x,p.x+end.x)+pad ||
       fmaxf(a.z,fmaxf(b.z,c.z))<fminf(p.z,p.z+end.z)-pad || fminf(a.z,fminf(b.z,c.z))>fmaxf(p.z,p.z+end.z)+pad ||
       fmaxf(a.y,fmaxf(b.y,c.y))<p.y-pad || fminf(a.y,fminf(b.y,c.y))>q.y+pad)return;
    if(cv_len(cc_cross(cv_sub(b,a),cv_sub(c,a)))<1e-8f)return;
    float travel=0;
    for(int i=0;i<48 && travel<hit->distance;i++){
        R2CamVec offset=cv_mul(dir,travel);
        R2CamVec sep=cc_triangle_distance(cv_add(p,offset),cv_add(q,offset),a,b,c);
        float distance=cv_len(sep),gap=distance-radius;
        /* Convex distance cannot decrease later along a separating/tangent ray.
           This also allows escape from a shallow pre-existing overlap. */
        if(travel==0 && distance>1e-6f && cv_dot(sep,dir)>=-1e-7f)return;
        if(gap<=0.002f){
            hit->distance=travel;
            hit->normal=distance>1e-6f?cv_mul(sep,1/distance):cv_mul(dir,-1);
            return;
        }
        travel+=gap-0.001f;
    }
    if(travel<hit->distance){hit->distance=travel;hit->normal=cv_mul(dir,-1);}
}
typedef R2CharacterHit (*R2CharacterSweepFn)(void *,R2CamVec,R2CamVec);
static inline R2CamVec character_slide(void *context,R2CharacterSweepFn sweep,R2CamVec origin,R2CamVec movement)
{
    R2CamVec current=origin;
    for(int pass=0;pass<4;pass++){
        float length=cv_len(movement);if(length<1e-5f)break;
        R2CharacterHit hit=sweep(context,current,movement);
        float fraction=fminf(1,hit.distance/length);
        current=cv_add(current,cv_mul(movement,fraction));
        if(fraction>=1)break;
        movement=cv_mul(movement,1-fraction);
        hit.normal.y=0;float nl=cv_len(hit.normal);if(nl<1e-6f)break;
        hit.normal=cv_mul(hit.normal,1/nl);
        float into=cv_dot(movement,hit.normal);
        if(into>=-1e-7f)break;
        movement=cv_sub(movement,cv_mul(hit.normal,into));
    }
    return cv_sub(current,origin);
}
#endif
