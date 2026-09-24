#include <assert.h>
#include <stdio.h>
#include "../../R2Engine.PS2/include/r2_camera_sweep.h"
int main(void)
{
    R2CamVec origin={0,1,0},down={0,-1,0};
    R2CamVec a={-20,0,-20},b={20,0,-20},c={0,0,20};
    float hit=camera_triangle_sweep(origin,down,10,0.25f,a,b,c);
    assert(fabsf(hit-0.75f)<0.002f);
    assert(fabsf(camera_triangle_sweep(origin,down,10,0.25f,c,b,a)-hit)<0.002f);
    R2CamVec away={0,1,0};
    assert(camera_triangle_sweep(origin,away,10,0.25f,a,b,c)==10);
    R2CamVec box_origin={0,0,-2},forward={0,0,1},half={2,2,0.001f};
    assert(fabsf(camera_box_sweep(box_origin,forward,8,0.25f,half)-1.749f)<0.002f);
    R2CamVec inside={0,0,0};
    assert(camera_box_sweep(inside,forward,8,0.25f,half)==0);
    R2CamVec touching={0,0,-0.1f},back={0,0,-1};
    assert(camera_box_sweep(touching,back,4,0.25f,half)==4);
    assert(camera_box_sweep(touching,forward,4,0.25f,half)==0);
    assert(camera_box_sweep(touching,back,.05f,0.25f,half)==0); /* endpoint still overlaps */
    assert(camera_box_sweep(touching,(R2CamVec){1,0,0},4,0.25f,half)==0); /* tangent */
    R2CamVec near_ground={0,.1f,0};
    assert(camera_triangle_sweep(near_ground,away,4,.25f,a,b,c)==4);
    assert(camera_triangle_sweep(near_ground,away,4,.25f,c,b,a)==4);
    assert(camera_triangle_sweep(near_ground,down,4,.25f,a,b,c)==0);
    assert(camera_capsule_sweep((R2CamVec){0,0,-.6f},back,4,.25f,1,.5f)==4);
    assert(camera_capsule_sweep((R2CamVec){0,0,-.6f},forward,4,.25f,1,.5f)==0);
    assert(camera_capsule_sweep((R2CamVec){0,0,-.1f},back,4,.25f,1,.5f)==0);
    float second=camera_box_sweep((R2CamVec){0,0,2},back,4,.25f,half);
    assert(fabsf(second-1.749f)<.002f); /* different wall behind remains blocking */
    assert(camera_box_sweep(touching,back,second,.25f,half)==second);
    puts("PASS: native separating overlap, inward/tangent/embedded guards, endpoints, second wall, thin walls, terrain, reverse winding, capsules");
}
