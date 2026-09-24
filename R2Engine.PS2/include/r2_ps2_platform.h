#ifndef R2_PS2_PLATFORM_H
#define R2_PS2_PLATFORM_H

#include <tamtypes.h>

typedef struct R2Ps2Platform R2Ps2Platform;

/* Per-instance Animator parameter update. Type: 0 float, 1 integer, 2 bool,
   3 trigger (1=set, 0=reset). Returns 0 for missing name/type/instance. */
int r2_ps2_animator_set_parameter(R2Ps2Platform *platform, unsigned int instance,
    const char *name, unsigned int type, float value);

typedef struct R2Ps2MeshVertex
{
    float x, y, z;
    float nx, ny, nz;
    float u, v;
} R2Ps2MeshVertex;

typedef struct R2Ps2MeshPackageHeader
{
    char magic[4];
    u32 version;
    u32 vertex_count;
    u32 index_count;
    u32 vertex_stride;
    u32 index_size;
    float bounds_min[3];
    float bounds_max[3];
} R2Ps2MeshPackageHeader;

typedef struct R2Ps2SceneInstance
{
    u32 mesh_slot;
    u32 texture_slot;
    float position[3];
    float rotation[3];
    float scale[3];
    float color[4];
} R2Ps2SceneInstance;

typedef struct R2Ps2SceneCamera
{
    float position[3];
    float rotation[3];
    float field_of_view;
    float near_clip;
    float far_clip;
    float aspect;
} R2Ps2SceneCamera;

typedef struct R2Ps2SceneLighting
{
    float background[3];
    float ambient[3];
    float directional_direction[3];
    float directional_color[3];
    float directional_intensity;
    u32 directional_enabled;
} R2Ps2SceneLighting;

typedef struct R2Ps2SceneFog
{
    float color[3];
    float start;
    float end;
    u32 enabled;
} R2Ps2SceneFog;

typedef struct R2Ps2SceneMaterial
{
    u32 lit;
    u32 surface_mode;
    u32 double_sided;
    u32 receive_fog;
    float alpha_cutoff;
} R2Ps2SceneMaterial;

typedef struct R2Ps2SceneCollider
{
    u32 type;
    float center[3];
    float radius;
    float height;
    float slope_limit;
    float step_height;
    u32 layer;
    u32 collision_mask;
} R2Ps2SceneCollider;

typedef struct R2Ps2SceneTransition
{
    u32 enabled;
    float center[3];
    float size[3];
    char scene_name[128];
} R2Ps2SceneTransition;

typedef struct R2Ps2SceneControls
{
    u32 enabled;
    s32 move_x_axis;
    s32 move_y_axis;
    s32 turn_axis;
    s32 sprint_button;
    s32 jump_button;
    float move_dead_zone;
    float turn_dead_zone;
    float move_speed;
    float turn_speed;
    float sprint_multiplier;
    float jump_strength;
    u32 invert_move_x;
    u32 invert_move_y;
    u32 invert_turn;
    u32 enable_sprint;
    u32 enable_jump;
    float camera_pitch_speed;
    float camera_min_pitch;
    float camera_max_pitch;
    u32 invert_camera_pitch;
    u32 camera_collision;
    float camera_collision_radius;
    float camera_clearance;
    float camera_return_speed;
    float camera_target_height;
    u32 kill_floor_enabled;
    float kill_floor_height;
    u32 camera_collision_mask;
    u32 launch_jump_from_animation_event;
    float jump_event_timeout;
} R2Ps2SceneControls;

typedef struct R2Ps2SceneAudio
{
    u32 enabled;
    u32 loop;
    u32 spatial;
    float volume;
    float pitch;
    float min_distance;
    float max_distance;
    float position[3];
    s32 listener_object_index;
    float listener_offset[3];
    char clip_path[256];
    char ui_sound_paths[5][256];
    u32 ui_open_on_scene_start;
} R2Ps2SceneAudio;

typedef struct R2Ps2SceneSavePoint
{
    u32 enabled;
    float center[3];
    float size[3];
    char slot_name[32];
} R2Ps2SceneSavePoint;

typedef struct R2Ps2ScenePersistentObject
{
    u32 enabled;
    char save_id[32];
    u32 save_transform;
    u32 save_active_state;
    u32 save_integer;
    u32 save_boolean;
    u32 active;
    s32 integer_value;
    u32 boolean_value;
} R2Ps2ScenePersistentObject;

typedef struct R2Ps2SceneUiElement
{
    u32 type;
    u32 interactable;
    float anchor[2];
    float offset[2];
    float size[2];
    float normal_color[4];
    float hover_color[4];
    float pressed_color[4];
    char text[64];
    u32 action;
    char save_slot[32];
    u32 save_load_feedback;
    char result_messages[4][64];
    float sprite_borders[4];
    float sprite_uv_borders[3][4];
    float sprite_region[4]; /* normalized left, top, right, bottom */
    char target_scene[128];
} R2Ps2SceneUiElement;

typedef struct R2Ps2Input
{
    int connected;
    int analog;
    u16 buttons;
    u8 left_x;
    u8 left_y;
    u8 right_x;
    u8 right_y;
} R2Ps2Input;

R2Ps2Platform *r2_ps2_create(void);
void r2_ps2_destroy(R2Ps2Platform *platform);
void r2_ps2_begin_frame(R2Ps2Platform *platform);
void r2_ps2_poll_input(R2Ps2Platform *platform, R2Ps2Input *input);
void r2_ps2_update_debug_camera(
    R2Ps2Platform *platform,
    const R2Ps2Input *input,
    float delta_seconds);
void r2_ps2_update_gameplay(
    R2Ps2Platform *platform,
    const R2Ps2Input *input,
    float delta_seconds);
void r2_ps2_update_audio(R2Ps2Platform *platform);
void r2_ps2_update_ui(R2Ps2Platform *platform, const R2Ps2Input *input);
void r2_ps2_draw_ui(R2Ps2Platform *platform);
int r2_ps2_consume_ui_button(R2Ps2Platform *platform, const char *text);
int r2_ps2_save_blob(R2Ps2Platform *platform, const char *slot,
    const void *data, unsigned int size);
int r2_ps2_load_blob(R2Ps2Platform *platform, const char *slot,
    void *data, unsigned int capacity, unsigned int *size);
int r2_ps2_save_checkpoint(R2Ps2Platform *platform, const char *slot);
int r2_ps2_load_checkpoint(R2Ps2Platform *platform, const char *slot);
int r2_ps2_set_persistent_active(R2Ps2Platform *platform, const char *save_id, int active);
int r2_ps2_get_persistent_active(R2Ps2Platform *platform, const char *save_id, int *active);
int r2_ps2_set_persistent_integer(R2Ps2Platform *platform, const char *save_id, int value);
int r2_ps2_get_persistent_integer(R2Ps2Platform *platform, const char *save_id, int *value);
int r2_ps2_set_persistent_boolean(R2Ps2Platform *platform, const char *save_id, int value);
int r2_ps2_get_persistent_boolean(R2Ps2Platform *platform, const char *save_id, int *value);
int r2_ps2_load_texture_package(
    R2Ps2Platform *platform,
    const void *package_data,
    unsigned int package_size);
int r2_ps2_load_texture_file(R2Ps2Platform *platform, const char *path);
int r2_ps2_load_mesh_package(
    R2Ps2Platform *platform,
    const void *package_data,
    unsigned int package_size);
int r2_ps2_load_mesh_file(R2Ps2Platform *platform, const char *path);
int r2_ps2_load_scene_package(
    R2Ps2Platform *platform,
    const void *package_data,
    unsigned int package_size);
int r2_ps2_load_scene_file(R2Ps2Platform *platform, const char *path);
void r2_ps2_draw_mesh_probe(R2Ps2Platform *platform, float phase);
void r2_ps2_profile_frame(float frame_ms, float input_ui_ms,
    float gameplay_ms, float audio_ms);
void r2_ps2_end_frame(R2Ps2Platform *platform);

#endif
