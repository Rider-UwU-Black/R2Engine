#ifndef R2_VERTEX_TRANSFORM_H
#define R2_VERTEX_TRANSFORM_H

/* Rotation arrays contain sin/cos pairs. Preserve the renderer's camera
   yaw, pitch, roll convention (which differs from the object XYZ order). */
static inline void r2_rotate_camera(float v[3], const float r[6])
{
    float t = r[1] * v[0] + r[0] * v[2];
    v[2] = -r[0] * v[0] + r[1] * v[2]; v[0] = t;
    t = r[3] * v[1] - r[2] * v[2];
    v[2] = r[2] * v[1] + r[3] * v[2]; v[1] = t;
    t = r[5] * v[0] + r[4] * v[1];
    v[1] = -r[4] * v[0] + r[5] * v[1]; v[0] = t;
}

/* Three linear columns followed by translation; includes nonuniform scale.
   Construct columns independently, not by subtracting transformed positions,
   to avoid losing precision when objects are far from the origin. */
static inline void r2_build_camera_transform(float m[12], const float scale[3],
    const float object_rotation[6], const float camera_rotation[6],
    const float relative_position[3])
{
    const float *r = object_rotation;
    for (unsigned int column = 0; column < 3; ++column)
    {
        float v[3] = {0.0f, 0.0f, 0.0f};
        v[column] = scale[column];
        float t = r[1] * v[1] - r[0] * v[2];
        v[2] = r[0] * v[1] + r[1] * v[2]; v[1] = t;
        t = r[3] * v[0] + r[2] * v[2];
        v[2] = -r[2] * v[0] + r[3] * v[2]; v[0] = t;
        t = r[5] * v[0] - r[4] * v[1];
        v[1] = r[4] * v[0] + r[5] * v[1]; v[0] = t;
        r2_rotate_camera(v, camera_rotation);
        for (unsigned int row = 0; row < 3; ++row)
            m[column * 3 + row] = v[row];
    }
    float translation[3] = {
        relative_position[0], relative_position[1], relative_position[2]};
    r2_rotate_camera(translation, camera_rotation);
    for (unsigned int row = 0; row < 3; ++row)
        m[9 + row] = translation[row];
}

static inline void r2_transform_camera(const float m[12],
    float x, float y, float z, float result[3])
{
    result[0] = x * m[0] + y * m[3] + z * m[6] + m[9];
    result[1] = x * m[1] + y * m[4] + z * m[7] + m[10];
    result[2] = x * m[2] + y * m[5] + z * m[8] + m[11];
}

#endif
