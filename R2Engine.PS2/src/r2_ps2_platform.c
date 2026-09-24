#include "r2_ps2_platform.h"
#include "r2_camera_sweep.h"
#include "r2_character_sweep.h"
#include "r2_save_identity.h"
#include "r2_script_checkpoint.h"
#include <errno.h>
#include "r2_vertex_transform.h"
#include "r2_gs_packed_xyz.h"
#include "r2_script_motion.h"
#include "r2_script_timer.h"
#include "r2_sliding_door.h"

#include <dmaKit.h>
#include <packet2.h>
#include <packet2_utils.h>
#include <draw3d.h>
#include <audsrv.h>
#include <gsKit.h>
#include <gsInline.h>
#include <kernel.h>
#include <libpad.h>
#include <libmc.h>
#include <fcntl.h>
#include <loadfile.h>
#include <sifrpc.h>
#include <malloc.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#ifdef R2_DISC_BUILD
#include <libcdvd.h>
#endif

extern int dma_channel_initialize(int channel, void *handler, int flags);
extern int dma_channel_wait(int channel, int timeout);
extern void dma_channel_send_packet2(packet2_t *packet, int channel, u8 flush_cache);

extern u32 R2VU1Transform_CodeStart __attribute__((section(".vudata")));
extern u32 R2VU1Transform_CodeEnd __attribute__((section(".vudata")));

#define R2_VU1_INPUT_QWORDS 221u
#define R2_VU1_DMA_BATCH_JOBS 16u
#ifndef R2_VU1_LIVE_DISPATCH
#define R2_VU1_LIVE_DISPATCH 1
#endif
#define R2_PS2_PROFILE_OVERLAY 0
#define R2_PS2_PROFILE_BARS 1
#define R2_DRAW_STQ_FOG_REGLIST \
    (((u64)GIF_REG_ST) | ((u64)GIF_REG_RGBAQ << 4) | ((u64)GIF_REG_XYZF2 << 8))
static packet2_t *r2_vu1_packets[2];
static u128 r2_vu1_inputs[2][R2_VU1_DMA_BATCH_JOBS][R2_VU1_INPUT_QWORDS]
    __attribute__((aligned(128)));
/* Each VIF command packet owns a matching REF payload bank. Stage one keeps
   the conservative waits but validates alternating ownership before overlap
   is enabled. */
static u32 r2_vu1_context;
static int r2_vu1_ready;

static inline void r2_vu1_wait_submission(void)
{
    if (r2_vu1_ready)
        dma_channel_wait(DMA_CHANNEL_VIF1, 0);
}

static int r2_vu1_initialize(void)
{
    if (dma_channel_initialize(DMA_CHANNEL_VIF1, NULL, 0) < 0)
        return 0;
    packet2_t *upload = packet2_create(
        packet2_utils_get_packet_size_for_program(
            &R2VU1Transform_CodeStart, &R2VU1Transform_CodeEnd) + 1u,
        P2_TYPE_NORMAL, P2_MODE_CHAIN, 1);
    packet2_t *settings = packet2_create(2, P2_TYPE_NORMAL, P2_MODE_CHAIN, 1);
    r2_vu1_packets[0] = packet2_create(96, P2_TYPE_NORMAL, P2_MODE_CHAIN, 1);
    r2_vu1_packets[1] = packet2_create(96, P2_TYPE_NORMAL, P2_MODE_CHAIN, 1);
    if (upload == NULL || settings == NULL ||
        r2_vu1_packets[0] == NULL || r2_vu1_packets[1] == NULL)
    {
        packet2_free(upload); packet2_free(settings);
        packet2_free(r2_vu1_packets[0]); packet2_free(r2_vu1_packets[1]);
        r2_vu1_packets[0] = r2_vu1_packets[1] = NULL;
        return 0;
    }
    packet2_vif_add_micro_program(upload, 0,
        &R2VU1Transform_CodeStart, &R2VU1Transform_CodeEnd);
    packet2_utils_vu_add_end_tag(upload);
    dma_channel_send_packet2(upload, DMA_CHANNEL_VIF1, 1);
    dma_channel_wait(DMA_CHANNEL_VIF1, 0);
    packet2_utils_vu_add_double_buffer(settings, 0, 438);
    packet2_utils_vu_add_end_tag(settings);
    dma_channel_send_packet2(settings, DMA_CHANNEL_VIF1, 1);
    dma_channel_wait(DMA_CHANNEL_VIF1, 0);
    packet2_free(upload);
    packet2_free(settings);
    r2_vu1_context = 0u;
    return 1;
}

static void r2_vu1_shutdown(void)
{
    if (r2_vu1_ready) dma_channel_wait(DMA_CHANNEL_VIF1, 0);
    if (r2_vu1_packets[0] != NULL) packet2_free(r2_vu1_packets[0]);
    if (r2_vu1_packets[1] != NULL) packet2_free(r2_vu1_packets[1]);
    r2_vu1_packets[0] = r2_vu1_packets[1] = NULL;
    r2_vu1_ready = 0;
}

/* Logical HostFS names stay in cooked packages and save files. Disc builds
   map them to flat ISO9660 8.3 names, avoiding spaces/case/path-length issues. */
static FILE *open_asset(const char *path)
{
#ifdef R2_DISC_BUILD
    static struct { char logical[256]; char disc[16]; } entries[2048];
    static unsigned int count;
    static int loaded;
    if (!loaded)
    {
        FILE *map = fopen("cdrom0:\\PATHS.BIN;1", "rb");
        char magic[4];
        if (!map) return NULL;
        if (fread(magic,1,4,map)!=4 || memcmp(magic,"R2DM",4)!=0 ||
            fread(&count,4,1,map)!=1 || count>2048 ||
            fread(entries,sizeof(entries[0]),count,map)!=count)
        { fclose(map); count=0; return NULL; }
        fclose(map);
        for(unsigned int i=0;i<count;i++) { entries[i].logical[255]=0; entries[i].disc[15]=0; }
        loaded=1;
    }
    const char *logical = strncmp(path,"host:assets/",12)==0 ? path+12 : path;
    char key[256];
    if(strlen(logical)>=sizeof(key)) return NULL;
    snprintf(key,sizeof(key),"%s",logical);
    for(char *p=key;*p;p++) { if(*p=='\\') *p='/'; if(*p>='A' && *p<='Z') *p+=32; }
    for(unsigned int i=0;i<count;i++) if(strcmp(entries[i].logical,key)==0)
    {
        char disc_path[48];
        snprintf(disc_path,sizeof(disc_path),"cdrom0:\\%s;1",entries[i].disc);
        return fopen(disc_path,"rb");
    }
    printf("Disc asset not mapped: %s\n",path);
    return NULL;
#else
    return fopen(path,"rb");
#endif
}

typedef struct R2ProjectedVertex
{
    float x;
    float y;
    float q;
    float camera_x;
    float camera_y;
    float depth;
    float red;
    float green;
    float blue;
    float alpha;
    float fog;
    u32 z;
    u32 outcode;
    u16 gs_x;
    u16 gs_y;
    int visible;
} R2ProjectedVertex;

/* Temporary native renderer profiler. Kept on-screen because PCSX2 does not
   forward EE stdout for every ELF/ISO boot path. Values are 120-frame means. */
static float r2_profile_skin_ms;
static float r2_profile_vertex_ms;
static float r2_profile_triangle_ms;
static float r2_profile_render_ms;
static float r2_profile_vu_prep_ms, r2_profile_vu_send_ms, r2_profile_vu_setup_ms;
static float r2_profile_sync_ms;
static float r2_profile_frame_bar_ms, r2_profile_input_bar_ms;
static float r2_profile_gameplay_bar_ms, r2_profile_audio_bar_ms;
static clock_t r2_profile_vu_prep_ticks, r2_profile_vu_send_ticks, r2_profile_vu_setup_ticks;
static int r2_profile_ready;
static u32 r2_vu1_eligible_objects;
static u32 r2_vu1_clipped_objects;
static u32 r2_vu1_dispatched_meshlets;
static GSTEXTURE *r2_vu1_active_texture;
static int r2_vu1_active_fog = -1;
#define R2_VU1_MAX_CLIPPED_TRIANGLES 4096u
static u32 r2_vu1_clipped_indices[R2_VU1_MAX_CLIPPED_TRIANGLES];
static u32 r2_vu1_clipped_index_count;

#define R2_PS2_MAX_SCENE_MESHES 16
#define R2_PS2_MAX_SCENE_TEXTURES 16
#include "r2_animation_playback.h"
#include "r2_animation_controller.h"
#define R2_PS2_MAX_ANIMATION_STATES 32
#define R2_PS2_MAX_PERSISTENT_OBJECTS 64
#define R2_PS2_MAX_UI_ELEMENTS 64

typedef struct R2Ps2AnimationState
{
    u32 kind;
    u32 frame_count;
    float fps;
    float duration;
    u32 float_offset;
    u32 loop;
    float speed;
    u32 finished_target;
    float finished_blend;
    float jump_launch_time;
    u32 lock_movement;
} R2Ps2AnimationState;

typedef struct R2Ps2Playback
{
    u32 flags, state, previous;
    float time, blend, previous_time, blend_duration;
    float *parameters;
    int parameters_ready;
    R2ScriptMotion script_motion;
} R2Ps2Playback;

typedef struct R2Ps2Meshlet
{
    u16 vertex_count;
    u16 triangle_count;
    const u16 *vertices;
    const u8 *indices;
    float bounds_min[3];
    float bounds_max[3];
} R2Ps2Meshlet;

typedef struct { u32 target; float center[3], size[3]; char trigger[64]; u32 enabled; int inside; } R2AnimatorZone;
typedef struct { float center[3], size[3]; u32 canvas, button; char prompt[64]; u32 source, page_count, page; char pages[8][64]; s32 font_slot; float font_size; } R2Interactable;

typedef struct R2Ps2MeshResource
{
    R2Ps2MeshVertex *vertices;
    u16 *indices;
    R2ProjectedVertex *projected;
    float *animation_frames;
    u8 *animation_skin;
    R2AnimController *controller;
    R2Ps2MeshVertex *animated_vertices;
    void *meshlet_blob;
    R2Ps2Meshlet *meshlets;
    u32 meshlet_count;
    u32 vertex_count;
    u32 index_count;
    u32 animation_bone_count;
    R2Ps2AnimationState animation_states[R2_PS2_MAX_ANIMATION_STATES];
    u32 animation_state_count;
    u32 animation_state;
    u32 animation_previous_state;
    float animation_time;
    float animation_blend;
    int ready;
} R2Ps2MeshResource;

typedef struct R2Ps2SceneDraw
{
    u32 source_instance;
    u32 mesh_slot;
    u32 texture_slot;
    u32 index_start;
    u32 index_count;
    R2Ps2SceneMaterial material;
    float color[4];
    float texture_tiling[2];
    float texture_offset[2];
} R2Ps2SceneDraw;

typedef struct R2Ps2LocalLight
{
    u32 type; /* 0 point, 1 spot */
    float position[3];
    float direction[3];
    float color[3];
    float intensity;
    float range;
    float spot_cos;
    u32 layer_mask;
} R2Ps2LocalLight;

typedef struct R2Ps2TextureResource
{
    GSTEXTURE texture;
    int ready;
} R2Ps2TextureResource;

struct R2Ps2Platform
{
    GSGLOBAL *gs;
    u64 background;
    int pad_open;
    int pad_mode_request;
    int pad_mode_retry_frames;
    GSTEXTURE texture;
    int texture_ready;
    R2Ps2MeshVertex *mesh_vertices;
    u16 *mesh_indices;
    R2ProjectedVertex *mesh_projected;
    void *meshlet_blob;
    R2Ps2Meshlet *meshlets;
    u32 meshlet_count;
    u32 mesh_vertex_count;
    u32 mesh_index_count;
    R2Ps2SceneInstance *scene_instances;
    R2Ps2SceneMaterial *scene_materials;
    R2Ps2SceneDraw *scene_draws;
    u32 scene_draw_count;
    u8 **scene_baked_colors;
    u32 *scene_baked_color_counts;
    R2Ps2LocalLight scene_local_lights[3];
    u32 scene_local_light_count;
    u32 scene_directional_layer_mask;
    u8 scene_instance_layers[4096];
    R2Ps2SceneCollider *scene_colliders;
    R2Ps2SceneTransition *scene_transitions;
    R2Ps2Playback playback[4096];
    u32 script_motion_count;
    R2ScriptTimer *script_timers;
    R2ScriptInputEdges script_input_edges;
    R2AnimatorZone animator_zones[64];
    u32 animator_zone_count;
    R2Interactable interactables[64];
    u32 interactable_count;
    R2SlidingDoor doors[64];
    u32 door_count;
    int door_focus;
    int interaction_focus;
    int interaction_canvas;
    R2Ps2SceneSavePoint *scene_save_points;
    R2Ps2ScenePersistentObject *scene_persistent_objects;
    R2Ps2SceneUiElement *scene_ui_elements;
    u32 scene_ui_count;
    float scene_ui_reference[2];
    s32 scene_ui_focus;
    int scene_ui_axis_latched;
    int scene_ui_activation_frames;
    char activated_ui_button[64];
    u32 scene_ui_visible;
    u32 scene_ui_pause_gameplay;
    u32 scene_ui_loading;
    u32 scene_ui_toggle_input;
    u32 scene_ui_cancel_input;
    u32 menu_depth;
    u32 menu_parent[32], menu_child[32];
    s32 menu_return_focus[32];
    u32 scene_instance_count;
    R2Ps2MeshResource scene_meshes[R2_PS2_MAX_SCENE_MESHES];
    R2Ps2TextureResource scene_textures[R2_PS2_MAX_SCENE_TEXTURES];
    u32 scene_mesh_count;
    u32 scene_texture_count;
    u32 queued_world_triangles;
    R2Ps2SceneCamera scene_camera;
    R2Ps2SceneLighting scene_lighting;
    R2Ps2SceneFog scene_fog;
    R2Ps2SceneControls scene_controls;
    float camera_correction[3];
    float camera_retraction;
    u32 startup_control_frames;
    R2Ps2SceneAudio scene_audio;
    FILE *audio_file;
    u32 audio_data_offset;
    u32 audio_data_length;
    u32 audio_remaining;
    u16 audio_source_bits;
    int audio_ready;
    int audio_volume;
    audsrv_adpcm_t ui_sounds[5][16];
    u32 ui_sound_counts[5];
    s32 ui_sound_last[5];
    u32 ui_sound_random;
    int memory_card_ready;
    int save_status;
    int load_error;
    int external_scene_loaded;
    u16 previous_buttons;
    float jump_time;
    float jump_previous_height;
    float vertical_velocity;
    int character_grounded;
    float player_idle_time;
    float scene_time;
    int transition_latched;
    u32 transition_prompt_mask;
    int autosave_latched;
    char current_scene_path[256];
    char active_checkpoint_slot[17];
    char scene_mesh_paths[R2_PS2_MAX_SCENE_MESHES][256];
    char scene_texture_paths[R2_PS2_MAX_SCENE_TEXTURES][256];
};

static unsigned char pad_buffer[256] __attribute__((aligned(64)));

static void free_baked_colors(u8 **colors, u32 *counts, u32 count)
{
    if (colors != NULL)
        for (u32 index = 0; index < count; ++index) free(colors[index]);
    free(colors);
    free(counts);
}

static u16 glyph_bits(char character)
{
    switch (character)
    {
        case '0': return 0x7B6F;
        case '1': return 0x2492;
        case '2': return 0x73E7;
        case '3': return 0x73CF;
        case '4': return 0x5BC9;
        case '5': return 0x79CF;
        case '6': return 0x79EF;
        case '7': return 0x7249;
        case '8': return 0x7BEF;
        case '9': return 0x7BCF;
        case 'F': return 0x79E4;
        case 'P': return 0x7BE4;
        case 'S': return 0x79CF;
        case 'A': return 0x2BED;
        case 'D': return 0x6B6E;
        case 'E': return 0x79E7;
        case 'O': return 0x7B6F;
        case 'K': return 0x5AAD;
        case 'L': return 0x4927;
        case 'I': return 0x7497;
        case 'H': return 0x5BED;
        case 'B': return 0x7AED;
        case 'C': return 0x792F;
        case 'G': return 0x79AF;
        case 'M': return 0x5BEA;
        case 'N': return 0x5B6D;
        case 'R': return 0x7BEC;
        case 'T': return 0x7492;
        case 'U': return 0x5B6F;
        case 'V': return 0x5B6A;
        case 'W': return 0x5B7D; /* 101 / 101 / 101 / 111 / 101 */
        case 'X': return 0x5AAD;
        case 'Y': return 0x5A92;
        case ':': return 0x0410;
        case '.': return 0x0002;
        default: return 0;
    }
}

static void draw_text(R2Ps2Platform *platform, float x, float y, const char *text, u64 color)
{
    const float pixel = 3.0f;
    const float aspect = platform->scene_camera.aspect > 0.0f
        ? platform->scene_camera.aspect : 4.0f / 3.0f;
    const float pixel_x = pixel * platform->gs->Width / (platform->gs->Height * aspect);
    x += (float)strlen(text) * (pixel - pixel_x) * 2.0f;
    while (*text != '\0')
    {
        const u16 bits = glyph_bits(*text++);
        for (int row = 0; row < 5; ++row)
        {
            for (int column = 0; column < 3; ++column)
            {
                const int bit = 14 - (row * 3 + column);
                if ((bits & (1u << bit)) == 0)
                    continue;
                const float left = x + (float)column * pixel_x;
                const float top = y + (float)row * pixel;
                gsKit_prim_sprite(platform->gs, left, top, left + pixel_x, top + pixel, 4, color);
            }
        }
        x += pixel_x * 4.0f;
    }
}

static u64 ui_color(const float color[4])
{
    int r = (int)(color[0] * 128.0f), g = (int)(color[1] * 128.0f);
    int b = (int)(color[2] * 128.0f), a = (int)(color[3] * 128.0f);
    if (r < 0) r = 0;
    if (r > 128) r = 128;
    if (g < 0) g = 0;
    if (g > 128) g = 128;
    if (b < 0) b = 0;
    if (b > 128) b = 128;
    if (a < 0) a = 0;
    if (a > 128) a = 128;
    return GS_SETREG_RGBAQ(r, g, b, a, 0x00);
}

static void ui_texture_slots(const R2Ps2SceneUiElement *element, int *normal, int *hover, int *pressed)
{
    *normal = *hover = *pressed = -1;
    if (element->type == 4u && element->save_slot[0] == '@')
    {
        sscanf(element->save_slot + 1, "%d", normal);
        return;
    }
    const char *metadata = strchr(element->save_slot, '|');
    if (metadata != NULL) sscanf(metadata + 1, "%d,%d,%d", normal, hover, pressed);
}

static int ui_font_slot(const R2Ps2SceneUiElement *element)
{
    int slot=-1;
    if(element && element->save_slot[0]=='#' && element->save_slot[1]=='F')
        sscanf(element->save_slot+2,"%d",&slot);
    else if(element && element->type==2u && element->result_messages[0][0]=='#' && element->result_messages[0][1]=='F')
        sscanf(element->result_messages[0]+2,"%d",&slot);
    return slot;
}

static float ui_font_size(const R2Ps2SceneUiElement *element, float fallback)
{
    float size=fallback;
    if(element && element->type==2u && element->result_messages[0][0]=='#' && element->result_messages[0][1]=='F')
    {
        int ignored=-1;
        sscanf(element->result_messages[0]+2,"%d,%f",&ignored,&size);
    }
    return size;
}

static void draw_atlas_text(R2Ps2Platform *platform, GSTEXTURE *texture, float cx, float cy,
    float height, const char *text, u64 color)
{
    if(!text || !texture || height<=0) return;
    u32 length=0; while(length<63u && text[length]) length++;
    /* The atlas cells include generous side padding. Keep the complete cell
       visible, but advance by the actual approximate glyph width so small
       labels do not look as though spaces were inserted between every letter. */
    const float aspect = platform->scene_camera.aspect > 0.0f
        ? platform->scene_camera.aspect : 4.0f / 3.0f;
    const float horizontal_scale = platform->gs->Width / (platform->gs->Height * aspect);
    const float width=height*0.8f*horizontal_scale, advance=height*0.58f*horizontal_scale;
    const float start=cx-((float)(length ? length-1u : 0u)*advance+width)*.5f;
    for(u32 i=0;i<length;i++)
    {
        int code=(unsigned char)text[i]; if(code<32 || code>126) code='?';
        int index=code-32, column=index%16, row=index/16;
        gsKit_prim_sprite_texture(platform->gs,texture,
            start+i*advance,cy-height*.5f,column*32.0f,row*40.0f,
            start+i*advance+width,cy+height*.5f,(column+1)*32.0f,(row+1)*40.0f,1,color);
    }
}

static const char *ui_checkpoint_slot(const R2Ps2SceneUiElement *element, char slot[32])
{
    u32 length = 0u;
    while (length < 31u && element->save_slot[length] != '\0' && element->save_slot[length] != '|')
    { slot[length] = element->save_slot[length]; ++length; }
    slot[length] = '\0';
    return length ? slot : "CHECKPOINT";
}

static void play_ui_sound(R2Ps2Platform *platform, u32 kind);

static int ui_go_back(R2Ps2Platform *platform)
{
    if (!platform->menu_depth) return 0;
    const u32 index = --platform->menu_depth;
    platform->scene_ui_visible &= ~(1u << platform->menu_child[index]);
    platform->scene_ui_visible |= 1u << platform->menu_parent[index];
    platform->scene_ui_focus = platform->menu_return_focus[index];
    platform->scene_ui_activation_frames = 0;
    play_ui_sound(platform, 4u);
    return 1;
}

static void play_ui_sound(R2Ps2Platform *platform, u32 kind)
{
    if (platform == NULL || !platform->audio_ready || kind >= 5u ||
        platform->ui_sound_counts[kind] == 0u) return;
    const u32 count = platform->ui_sound_counts[kind];
    platform->ui_sound_random = platform->ui_sound_random * 1664525u + 1013904223u;
    u32 choice = count == 1u ? 0u : platform->ui_sound_random % (count - 1u);
    if (count > 1u && (s32)choice >= platform->ui_sound_last[kind]) ++choice;
    platform->ui_sound_last[kind] = (s32)choice;
    int channel = audsrv_play_adpcm(&platform->ui_sounds[kind][choice]);
    if (channel >= 0) audsrv_adpcm_set_volume_and_pan(channel, MAX_VOLUME, 0);
}

void r2_ps2_update_ui(R2Ps2Platform *platform, const R2Ps2Input *input)
{
    if (platform == NULL || input == NULL || platform->scene_ui_count == 0u) return;
    if ((input->buttons & PAD_START) != 0 && (platform->previous_buttons & PAD_START) == 0)
    {
        if (platform->menu_depth)
        {
            if (platform->scene_ui_cancel_input & (1u << platform->menu_child[platform->menu_depth - 1]))
                ui_go_back(platform);
        }
        else
        {
        u32 candidates = platform->scene_ui_toggle_input & ~platform->scene_ui_loading;
        u32 preferred = candidates & platform->scene_ui_pause_gameplay;
        u32 toggle = preferred & (~preferred + 1u);
        if (!toggle)
            for (u32 i = 0; i < platform->scene_ui_count; ++i)
            {
                u32 candidate = 1u << (platform->scene_ui_elements[i].interactable >> 16);
                if (candidate & candidates) { toggle = candidate; break; }
            }
        platform->scene_ui_visible ^= toggle;
        if (toggle && (platform->scene_ui_visible & toggle)) play_ui_sound(platform, 4u);
        if (toggle) platform->scene_ui_focus = -1;
        }
    }
    if (!platform->scene_ui_visible) return;
    // A hidden/disabled button cannot retain navigation focus after a canvas change.
    if (platform->scene_ui_focus >= 0)
    {
        const u32 focus = (u32)platform->scene_ui_focus;
        if (focus >= platform->scene_ui_count || platform->scene_ui_elements[focus].type != 2u ||
            !(platform->scene_ui_elements[focus].interactable & 1u) ||
            !(platform->scene_ui_visible & (1u << (platform->scene_ui_elements[focus].interactable >> 16))))
            platform->scene_ui_focus = -1;
    }
    if (platform->scene_ui_focus < 0)
        for (u32 i = 0; i < platform->scene_ui_count; ++i)
            if (platform->scene_ui_elements[i].type == 2u && (platform->scene_ui_elements[i].interactable & 1u) &&
                (platform->scene_ui_visible & (1u << (platform->scene_ui_elements[i].interactable >> 16))))
            { platform->scene_ui_focus = (s32)i; break; }
    const int previous_up = (platform->previous_buttons & PAD_UP) != 0;
    const int previous_down = (platform->previous_buttons & PAD_DOWN) != 0;
    const int axis_direction = input->left_y > 200u ? 1 : input->left_y < 55u ? -1 : 0;
    const int navigate = ((input->buttons & PAD_DOWN) != 0 && !previous_down) ? 1 :
        ((input->buttons & PAD_UP) != 0 && !previous_up) ? -1 :
        (axis_direction != 0 && !platform->scene_ui_axis_latched ? axis_direction : 0);
    platform->scene_ui_axis_latched = axis_direction != 0;
    if (navigate != 0 && platform->scene_ui_focus >= 0)
    {
        s32 candidate = platform->scene_ui_focus;
        for (u32 attempt = 0; attempt < platform->scene_ui_count; ++attempt)
        {
            candidate += navigate;
            if (candidate < 0) candidate = (s32)platform->scene_ui_count - 1;
            if (candidate >= (s32)platform->scene_ui_count) candidate = 0;
            if (platform->scene_ui_elements[candidate].type == 2u &&
                (platform->scene_ui_elements[candidate].interactable & 1u) &&
                (platform->scene_ui_visible & (1u << (platform->scene_ui_elements[candidate].interactable >> 16))))
            { platform->scene_ui_focus = candidate; break; }
        }
        play_ui_sound(platform, 0u);
    }
    if ((input->buttons & PAD_CROSS) != 0 && (platform->previous_buttons & PAD_CROSS) == 0 &&
        platform->scene_ui_focus >= 0)
    {
        play_ui_sound(platform, 1u);
        platform->scene_ui_activation_frames = 10;
        const u32 visible_before_activation = platform->scene_ui_visible;
        snprintf(platform->activated_ui_button, sizeof(platform->activated_ui_button), "%s",
            platform->scene_ui_elements[platform->scene_ui_focus].text);
        R2Ps2SceneUiElement *activated = &platform->scene_ui_elements[platform->scene_ui_focus];
        const u32 action = activated->action & 0xffffu, owner = activated->interactable >> 16, target = activated->action >> 16;
        if (action == 1u) platform->scene_ui_visible &= ~(1u << owner);
        else if (action == 4u)
        { platform->scene_ui_visible |= 1u << target; play_ui_sound(platform, 4u); }
        else if (action == 5u) platform->scene_ui_visible &= ~(1u << target);
        else if (action == 6u)
        {
            platform->scene_ui_visible ^= 1u << target;
            if (platform->scene_ui_visible & (1u << target)) play_ui_sound(platform, 4u);
        }
        else if (action == 7u && target < 32u && target != owner && platform->menu_depth < 31u &&
            (!platform->menu_depth || platform->menu_child[platform->menu_depth - 1] == owner) &&
            !(platform->scene_ui_visible & (1u << target)))
        {
            int cyclic = 0;
            for (u32 i = 0; i < platform->menu_depth; ++i)
                if (platform->menu_parent[i] == target) cyclic = 1;
            if (!cyclic)
            {
                const u32 i = platform->menu_depth++;
                platform->menu_parent[i] = owner;
                platform->menu_child[i] = target;
                platform->menu_return_focus[i] = platform->scene_ui_focus;
                platform->scene_ui_visible = (platform->scene_ui_visible & ~(1u << owner)) | (1u << target);
                play_ui_sound(platform, 4u);
                platform->scene_ui_focus = -1;
                platform->scene_ui_activation_frames = 0;
            }
        }
        else if (action == 8u) ui_go_back(platform);
        else if (action == 9u && activated->target_scene[0])
        {
            char path[512];
            snprintf(path, sizeof(path), "host:assets/%s", activated->target_scene);
            if (!r2_ps2_load_scene_file(platform, path))
                printf("UI target scene failed to load: %s\n", path);
            return; /* The loader can replace/free the current UI records. */
        }
        else if (action == 2u)
        { char slot[32]; r2_ps2_save_checkpoint(platform, ui_checkpoint_slot(activated, slot)); }
        else if (action == 3u)
        { char slot[32]; r2_ps2_load_checkpoint(platform, ui_checkpoint_slot(activated, slot)); }
        // No-op and save actions keep their selected button and pressed feedback.
        // Scene loads reset focus in the loader; canvas changes require reselection.
        if (platform->scene_ui_visible != visible_before_activation && action != 7u && action != 8u)
        {
            platform->scene_ui_focus = -1;
            platform->scene_ui_activation_frames = 0;
        }
        printf("UI button activated: %s\n", platform->activated_ui_button);
    }
    if ((input->buttons & PAD_CIRCLE) != 0 && (platform->previous_buttons & PAD_CIRCLE) == 0)
    {
        play_ui_sound(platform, 2u);
        if (platform->menu_depth)
        {
            if (platform->scene_ui_cancel_input & (1u << platform->menu_child[platform->menu_depth - 1]))
                ui_go_back(platform);
        }
        else if (platform->scene_ui_focus >= 0)
            platform->scene_ui_visible &= ~((1u << (platform->scene_ui_elements[platform->scene_ui_focus].interactable >> 16)) & platform->scene_ui_cancel_input);
        else for(u32 i=0;i<platform->interactable_count;i++)
        {
            u32 bit=1u<<platform->interactables[i].canvas;
            if(platform->scene_ui_visible & platform->scene_ui_cancel_input & bit)
            { platform->scene_ui_visible &= ~bit; break; }
        }
    }
}

int r2_ps2_consume_ui_button(R2Ps2Platform *platform, const char *text)
{
    if (platform == NULL || text == NULL || platform->activated_ui_button[0] == '\0' ||
        strcmp(platform->activated_ui_button, text) != 0) return 0;
    platform->activated_ui_button[0] = '\0';
    return 1;
}

static void set_texture_wrap_mode(R2Ps2Platform *platform, u32 mode)
{
    if (platform == NULL || platform->gs == NULL) return;
    u64 *packet = gsKit_heap_alloc(platform->gs, 1, 16, GIF_AD);
    *packet++ = GIF_TAG_AD(1);
    *packet++ = GIF_AD;
    *packet++ = GS_SETREG_CLAMP(mode, mode, 0, 0, 0, 0);
    *packet++ = GS_CLAMP_1;
}

void r2_ps2_profile_frame(float frame_ms, float input_ui_ms,
    float gameplay_ms, float audio_ms)
{
    r2_profile_frame_bar_ms = frame_ms;
    r2_profile_input_bar_ms = input_ui_ms;
    r2_profile_gameplay_bar_ms = gameplay_ms;
    r2_profile_audio_bar_ms = audio_ms;
}

void r2_ps2_draw_ui(R2Ps2Platform *platform)
{
    /* UI is queued through GIF PATH3. Ensure the final VU batch has finished
       reaching VIF/GIF before changing state and submitting the UI queue. */
    r2_vu1_wait_submission();
    /* UI atlases must not bleed across their outer edge. World rendering
       explicitly switches back to repeat at the beginning of its pass. */
    set_texture_wrap_mode(platform, 1u);
    if(platform && platform->door_focus>=0 && (u32)platform->door_focus<platform->door_count)
    {
        char text[48];u32 button=platform->doors[platform->door_focus].button;
        if(button==0)snprintf(text,sizeof(text),"X - OPEN");else snprintf(text,sizeof(text),"BUTTON %u - OPEN",button);
        int old_z=platform->gs->Test->ZTE;platform->gs->Test->ZTE=0;gsKit_set_test(platform->gs,GS_ATEST_OFF);
        const float white[4]={1,1,1,1};
        draw_text(platform,(platform->gs->Width-strlen(text)*12.0f)*.5f,platform->gs->Height*.9f,text,ui_color(white));
        platform->gs->Test->ZTE=old_z;gsKit_set_test(platform->gs,GS_ATEST_OFF);
    }
    if(platform && platform->interaction_focus>=0 && (u32)platform->interaction_focus<platform->interactable_count)
    {
        R2Interactable *interaction=&platform->interactables[platform->interaction_focus];
        char text[64]; const char *source=interaction->prompt;
        u32 i; for(i=0;i<63 && source[i];i++) text[i]=(source[i]>='a' && source[i]<='z') ? source[i]-32 : source[i]; text[i]=0;
        int old_z=platform->gs->Test->ZTE; platform->gs->Test->ZTE=0; gsKit_set_test(platform->gs,GS_ATEST_OFF);
        const float white[4]={1,1,1,1};
        gsKit_set_primalpha(platform->gs,GS_SETREG_ALPHA(0,1,0,1,0),0);
        platform->gs->PrimAlphaEnable=GS_SETTING_ON;
        if(interaction->font_slot>=0 && (u32)interaction->font_slot<platform->scene_texture_count && platform->scene_textures[interaction->font_slot].ready)
            draw_atlas_text(platform,&platform->scene_textures[interaction->font_slot].texture,
                platform->gs->Width*.5f,platform->gs->Height*.9f,fmaxf(4,interaction->font_size),source,ui_color(white));
        else draw_text(platform,(platform->gs->Width-i*12.0f)*.5f,platform->gs->Height*.9f,text,ui_color(white));
        platform->gs->PrimAlphaEnable=GS_SETTING_OFF;
        platform->gs->Test->ZTE=old_z; gsKit_set_test(platform->gs,GS_ATEST_OFF);
    }
#if !defined(R2_DISC_BUILD) && R2_PS2_PROFILE_OVERLAY
    if (platform != NULL && r2_profile_ready)
    {
        char line[96];
        const u64 white = GS_SETREG_RGBAQ(128, 128, 128, 128, 0x00);
        const int profile_zte = platform->gs->Test->ZTE;
        platform->gs->Test->ZTE = 0;
        gsKit_set_test(platform->gs, GS_ATEST_OFF);
        snprintf(line, sizeof(line), "R2 SKIN %.2F VERT %.2F TRI %.2F RENDER %.2F MS",
            r2_profile_skin_ms, r2_profile_vertex_ms,
            r2_profile_triangle_ms, r2_profile_render_ms);
        draw_text(platform, 8.0f, 8.0f, line, white);
        snprintf(line, sizeof(line), "VU PREP %.2F WAIT %.2F SETUP %.2F SYNC %.2F MS",
            r2_profile_vu_prep_ms, r2_profile_vu_send_ms,
            r2_profile_vu_setup_ms, r2_profile_sync_ms);
        draw_text(platform, 8.0f, 24.0f, line, white);
        snprintf(line, sizeof(line), "VU ELIG %u CLIP %u JOBS %u",
            r2_vu1_eligible_objects, r2_vu1_clipped_objects,
            r2_vu1_dispatched_meshlets);
        draw_text(platform, 8.0f, 40.0f, line, white);
        platform->gs->Test->ZTE = profile_zte;
        gsKit_set_test(platform->gs, GS_ATEST_OFF);
    }
#endif
#if R2_PS2_PROFILE_BARS
    if (platform != NULL)
    {
        const float scale = 12.0f;
        const float widths[9] = {
            fminf(50.0f, r2_profile_frame_bar_ms) * scale,
            fminf(50.0f, r2_profile_input_bar_ms) * scale,
            fminf(50.0f, r2_profile_gameplay_bar_ms) * scale,
            fminf(50.0f, r2_profile_audio_bar_ms) * scale,
            fminf(50.0f, r2_profile_render_ms) * scale,
            fminf(50.0f, r2_profile_vertex_ms) * scale,
            fminf(50.0f, r2_profile_triangle_ms) * scale,
            fminf(50.0f, r2_profile_vu_prep_ms) * scale,
            fminf(50.0f, r2_profile_vu_send_ms + r2_profile_vu_setup_ms) * scale};
        const u64 colors[9] = {
            GS_SETREG_RGBAQ(128,128,128,128,0),
            GS_SETREG_RGBAQ(32,96,128,128,0),
            GS_SETREG_RGBAQ(128,32,32,128,0),
            GS_SETREG_RGBAQ(128,96,16,128,0),
            GS_SETREG_RGBAQ(32,128,32,128,0),
            GS_SETREG_RGBAQ(16,128,128,128,0),
            GS_SETREG_RGBAQ(128,16,128,128,0),
            GS_SETREG_RGBAQ(128,80,16,128,0),
            GS_SETREG_RGBAQ(80,32,128,128,0)};
        const int old_zte = platform->gs->Test->ZTE;
        platform->gs->Test->ZTE = 0;
        gsKit_set_test(platform->gs, GS_ATEST_OFF);
        for (u32 bar = 0; bar < 9u; ++bar)
            gsKit_prim_sprite(platform->gs, 4.0f, 4.0f + bar * 5.0f,
                4.0f + widths[bar], 7.0f + bar * 5.0f, 1, colors[bar]);
        gsKit_prim_sprite(platform->gs, 204.0f, 3.0f, 206.0f, 48.0f, 0,
            GS_SETREG_RGBAQ(128,128,0,128,0));
        platform->gs->Test->ZTE = old_zte;
        gsKit_set_test(platform->gs, GS_ATEST_OFF);
    }
#endif
    if (platform == NULL || platform->scene_ui_elements == NULL || !platform->scene_ui_visible) return;
    /* Match horizontal element scale to vertical scale in display space.
       Anchors still cover the complete anamorphic framebuffer. */
    const float sx = (float)platform->gs->Width /
        (platform->scene_ui_reference[1] * platform->scene_camera.aspect);
    const float sy = (float)platform->gs->Height / platform->scene_ui_reference[1];
    const int previous_zte = platform->gs->Test->ZTE;
    const int previous_ztst = platform->gs->Test->ZTST;
    platform->gs->Test->ZTE = 0;
    gsKit_set_test(platform->gs, GS_ATEST_OFF);
    /* Source-over: (source - destination) * source alpha + destination.
       This does not depend on destination alpha, which CT24 does not store. */
    gsKit_set_primalpha(platform->gs, GS_SETREG_ALPHA(0, 1, 0, 1, 0), 0);
    platform->gs->PrimAlphaEnable = GS_SETTING_ON;
    for (u32 i = 0; i < platform->scene_ui_count; ++i)
    {
        R2Ps2SceneUiElement *element = &platform->scene_ui_elements[i];
        if ((platform->scene_ui_visible & (1u << (element->interactable >> 16))) == 0u) continue;
        const float cx = element->anchor[0] * platform->gs->Width + element->offset[0] * sx;
        const float cy = element->anchor[1] * platform->gs->Height + element->offset[1] * sy;
        const float hw = element->size[0] * sx * 0.5f, hh = element->size[1] * sy * 0.5f;
        const int focused = (s32)i == platform->scene_ui_focus && element->type == 2u;
        const int pressed = focused && (((platform->previous_buttons & PAD_CROSS) != 0) ||
            platform->scene_ui_activation_frames > 0);
        const float *color = pressed ? element->pressed_color :
            focused ? element->hover_color : element->normal_color;
        int normal_slot, hover_slot, pressed_slot;
        ui_texture_slots(element, &normal_slot, &hover_slot, &pressed_slot);
        int texture_slot = pressed && pressed_slot >= 0 ? pressed_slot :
            focused && hover_slot >= 0 ? hover_slot : normal_slot;
        if (texture_slot >= 0 && (u32)texture_slot < platform->scene_texture_count &&
            platform->scene_textures[texture_slot].ready)
        {
            GSTEXTURE *texture = &platform->scene_textures[texture_slot].texture;
            const u64 tint = element->type == 4u ? ui_color(color) :
                GS_SETREG_RGBAQ(128, 128, 128, (int)(color[3] * 128.0f), 0x00);
            const int state = pressed && pressed_slot >= 0 ? 2 : focused && hover_slot >= 0 ? 1 : 0;
            const float *uv = element->sprite_uv_borders[state];
            const float *region = element->sprite_region;
            const float u0 = region[0] * texture->Width, v0 = region[1] * texture->Height;
            const float u1 = region[2] * texture->Width, v1 = region[3] * texture->Height;
            if (uv[0] + uv[1] + uv[2] + uv[3] <= 0.0f)
            {
                gsKit_prim_sprite_texture(platform->gs, texture,
                    cx - hw, cy - hh, u0, v0,
                    cx + hw, cy + hh, u1, v1, 1, tint);
            }
            else
            {
                const float *border = element->sprite_borders;
                float left = border[0] * sx, top = border[1] * sy;
                float right = border[2] * sx, bottom = border[3] * sy;
                const float fit_x = fminf(1.0f, fmaxf(0.0f, 2.0f * hw) / fmaxf(0.0001f, left + right));
                const float fit_y = fminf(1.0f, fmaxf(0.0f, 2.0f * hh) / fmaxf(0.0001f, top + bottom));
                const float x[4] = {cx-hw, cx-hw+left*fit_x, cx+hw-right*fit_x, cx+hw};
                const float y[4] = {cy-hh, cy-hh+top*fit_y, cy+hh-bottom*fit_y, cy+hh};
                const float u[4] = {u0, u0 + uv[0]*(u1-u0), u1 - uv[2]*(u1-u0), u1};
                const float v[4] = {v0, v0 + uv[1]*(v1-v0), v1 - uv[3]*(v1-v0), v1};
                for (int row = 0; row < 3; ++row)
                    for (int col = 0; col < 3; ++col)
                        if (x[col+1] > x[col] && y[row+1] > y[row])
                        {
                            gsKit_prim_sprite_texture(platform->gs, texture, x[col], y[row], u[col], v[row],
                                x[col+1], y[row+1], u[col+1], v[row+1], 1, tint);
                        }
            }
        }
        else if (element->type != 3u && element->type != 4u)
            gsKit_prim_sprite(platform->gs, cx - hw, cy - hh, cx + hw, cy + hh, 1, ui_color(color));
        if (element->type == 2u && element->text[0] != '\0')
        {
            int font_slot=ui_font_slot(element);
            if(font_slot>=0 && (u32)font_slot<platform->scene_texture_count && platform->scene_textures[font_slot].ready)
                draw_atlas_text(platform,&platform->scene_textures[font_slot].texture,cx,cy,
                    fmaxf(4.0f,ui_font_size(element,16.0f)*sy),element->text,
                    GS_SETREG_RGBAQ(128,128,128,128,0x00));
            else
            {
                char upper[64]; u32 length = 0u;
                while (element->text[length] != '\0' && length < 63u)
                { char c = element->text[length]; upper[length] = c >= 'a' && c <= 'z' ? c - 32 : c; ++length; }
                upper[length] = '\0';
                draw_text(platform, cx - (float)length * 6.0f, cy - 7.5f, upper,
                    GS_SETREG_RGBAQ(128, 128, 128, 128, 0x00));
            }
        }
        else if (element->type == 3u)
        {
            const char *label = element->text;
            if (element->save_load_feedback && platform->save_status >= 1 && platform->save_status <= 4)
                label = element->result_messages[platform->save_status - 1];
            int font_slot=ui_font_slot(element);
            if(font_slot>=0 && (u32)font_slot<platform->scene_texture_count && platform->scene_textures[font_slot].ready)
                draw_atlas_text(platform,&platform->scene_textures[font_slot].texture,cx,cy,
                    fmaxf(4.0f,element->sprite_borders[0]*sy),label,ui_color(element->normal_color));
            else
            {
                char upper[64]; u32 length = 0u;
                while (length < 63u && label[length] != '\0')
                { char c = label[length]; upper[length] = c >= 'a' && c <= 'z' ? c - 32 : c; ++length; }
                upper[length] = '\0';
                draw_text(platform, cx - (float)length * 6.0f, cy - 7.5f, upper, ui_color(element->normal_color));
            }
        }
    }
    platform->gs->PrimAlphaEnable = GS_SETTING_OFF;
    if (platform->scene_ui_activation_frames > 0)
        --platform->scene_ui_activation_frames;
    platform->gs->Test->ZTE = previous_zte;
    platform->gs->Test->ZTST = previous_ztst;
    gsKit_set_test(platform->gs, GS_ATEST_OFF);
}

R2Ps2Platform *r2_ps2_create(void)
{
    R2Ps2Platform *platform = calloc(1, sizeof(*platform));
    if (platform == NULL)
        return NULL;

    platform->gs = gsKit_init_global();
    if (platform->gs == NULL)
    {
        free(platform);
        return NULL;
    }

    platform->gs->PSM = GS_PSM_CT24;
    platform->gs->PSMZ = GS_PSMZ_24;
#ifdef R2_PROGRESSIVE_SCAN
    platform->gs->Mode = GS_MODE_DTV_480P;
    platform->gs->Interlace = GS_NONINTERLACED;
    platform->gs->Field = GS_FRAME;
#endif
    platform->gs->ZBuffering = GS_SETTING_ON;
    platform->gs->PrimAlphaEnable = GS_SETTING_OFF;

    dmaKit_init(
        D_CTRL_RELE_OFF,
        D_CTRL_MFD_OFF,
        D_CTRL_STS_UNSPEC,
        D_CTRL_STD_OFF,
        D_CTRL_RCYC_8,
        1 << DMA_CHANNEL_GIF);
    dmaKit_chan_init(DMA_CHANNEL_GIF);
    r2_vu1_ready = R2_VU1_LIVE_DISPATCH ? r2_vu1_initialize() : 0;
    printf("VU1 indexed meshlet path: %s\n", r2_vu1_ready ? "ready" : "fallback only");
    gsKit_init_screen(platform->gs);
    gsKit_mode_switch(platform->gs, GS_PERSISTENT);
    gsKit_set_primalpha(platform->gs, GS_SETREG_ALPHA(0, 1, 0, 1, 0), 0);

    platform->background = GS_SETREG_RGBAQ(3, 7, 18, 0, 0);
    platform->scene_lighting.ambient[0] = 0.28f;
    platform->scene_lighting.ambient[1] = 0.28f;
    platform->scene_lighting.ambient[2] = 0.28f;
    platform->scene_lighting.directional_direction[0] = -0.35f;
    platform->scene_lighting.directional_direction[1] = 0.75f;
    platform->scene_lighting.directional_direction[2] = 0.56f;
    platform->scene_lighting.directional_color[0] = 1.0f;
    platform->scene_lighting.directional_color[1] = 1.0f;
    platform->scene_lighting.directional_color[2] = 1.0f;
    platform->scene_lighting.directional_intensity = 0.72f;
    platform->scene_lighting.directional_enabled = 1;
    platform->scene_fog.color[0] = 0.45f;
    platform->scene_fog.color[1] = 0.50f;
    platform->scene_fog.color[2] = 0.58f;
    platform->scene_fog.start = 20.0f;
    platform->scene_fog.end = 80.0f;
    platform->scene_fog.enabled = 0;
    platform->jump_time = -1.0f;
    platform->audio_volume = -1;

    SifInitRpc(0);
#ifdef R2_DISC_BUILD
    sceCdInit(SCECdINIT);
    sceCdDiskReady(0);
#endif
    SifLoadModule("rom0:LIBSD", 0, NULL);
    if (SifLoadModule(
#ifdef R2_DISC_BUILD
        "cdrom0:\\AUDSRV.IRX;1",
#else
        "host:assets/audsrv.irx",
#endif
        0, NULL) >= 0 && audsrv_init() == 0)
    {
        platform->audio_ready = 1;
        audsrv_adpcm_init();
    }
    if (SifLoadModule("rom0:SIO2MAN", 0, NULL) < 0)
        SifLoadModule("rom0:XSIO2MAN", 0, NULL);
    if (SifLoadModule("rom0:PADMAN", 0, NULL) < 0)
        SifLoadModule("rom0:XPADMAN", 0, NULL);
    /* Module-load calls may report "already resident", which is not failure.
       Let mcInit be the authority on whether the service is usable. The legacy
       ROM pair also coexists with the PADMAN path already initialized above. */
    SifLoadModule("rom0:MCMAN", 0, NULL);
    SifLoadModule("rom0:MCSERV", 0, NULL);
    platform->memory_card_ready = mcInit(MC_TYPE_MC) >= 0;
    printf("Memory card service: %s\n", platform->memory_card_ready ? "ready" : "unavailable");
    if (padInit(0) != 0)
        platform->pad_open = padPortOpen(0, 0, pad_buffer);

    printf("R2Engine PS2 backend probe initialized: %dx%d\n",
        platform->gs->Width,
        platform->gs->Height);
    return platform;
}

void r2_ps2_destroy(R2Ps2Platform *platform)
{
    r2_vu1_shutdown();
    if (platform != NULL && platform->audio_file != NULL)
        fclose(platform->audio_file);
    if (platform != NULL && platform->audio_ready)
        audsrv_quit();
    if (platform != NULL && platform->texture.Mem != NULL)
        free(platform->texture.Mem);
    if (platform != NULL && platform->texture.Clut != NULL)
        free(platform->texture.Clut);
    if (platform != NULL)
    {
        free(platform->mesh_vertices);
        free(platform->mesh_indices);
        free(platform->mesh_projected);
        free(platform->meshlet_blob);
        free(platform->meshlets);
        free(platform->scene_instances);
        free(platform->script_timers);
        free(platform->scene_materials);
        free(platform->scene_draws);
        free_baked_colors(platform->scene_baked_colors,
            platform->scene_baked_color_counts, platform->scene_instance_count);
        free(platform->scene_colliders);
        free(platform->scene_transitions);
        free(platform->scene_save_points);
        free(platform->scene_persistent_objects);
        free(platform->scene_ui_elements);
        for (u32 index = 0; index < R2_PS2_MAX_SCENE_MESHES; ++index)
        {
            free(platform->scene_meshes[index].vertices);
            free(platform->scene_meshes[index].indices);
            free(platform->scene_meshes[index].projected);
            free(platform->scene_meshes[index].animation_frames);
            free(platform->scene_meshes[index].animation_skin);
            free(platform->scene_meshes[index].controller);
            free(platform->scene_meshes[index].animated_vertices);
            free(platform->scene_meshes[index].meshlet_blob);
            free(platform->scene_meshes[index].meshlets);
        }
        for (u32 index = 0; index < R2_PS2_MAX_SCENE_TEXTURES; ++index)
        {
            free(platform->scene_textures[index].texture.Mem);
            free(platform->scene_textures[index].texture.Clut);
        }
    }
    for (u32 i=0; i<4096u; i++) free(platform->playback[i].parameters);
    free(platform);
}

static u32 read_u32_le(const u8 *bytes)
{
    return (u32)bytes[0] |
        ((u32)bytes[1] << 8) |
        ((u32)bytes[2] << 16) |
        ((u32)bytes[3] << 24);
}

static u16 read_u16_le(const u8 *bytes)
{
    return (u16)bytes[0] | ((u16)bytes[1] << 8);
}

int r2_ps2_load_texture_package(
    R2Ps2Platform *platform,
    const void *package_data,
    unsigned int package_size)
{
    const u8 *bytes = package_data;
    if (platform == NULL || bytes == NULL || package_size < 40 ||
        bytes[0] != 'R' || bytes[1] != '2' || bytes[2] != 'T' || bytes[3] != 'X')
        return 0;

    const u32 version = read_u32_le(bytes + 4);
    const u32 format = read_u32_le(bytes + 8);
    const u32 package_width = read_u32_le(bytes + 12);
    const u32 package_height = read_u32_le(bytes + 16);
    const u32 mip_count = read_u32_le(bytes + 20);
    const u32 palette_count = read_u32_le(bytes + 24);
    const u32 filter = version >= 2 ? read_u32_le(bytes + 28) : 0u;
    const u32 palette_bytes = palette_count * 4u;
    const u32 palette_offset = version >= 2 ? 32u : 28u;
    const u32 level_offset = palette_offset + palette_bytes;
    if ((version != 1 && version != 2) || filter > 1 || format > 3 || mip_count == 0 ||
        package_width == 0 || package_height == 0 || level_offset + 12u > package_size)
        return 0;
    if ((format == 2 && palette_count != 256u) ||
        (format == 3 && palette_count != 16u) ||
        (format < 2 && palette_count != 0u))
        return 0;

    const u32 width = read_u32_le(bytes + level_offset);
    const u32 height = read_u32_le(bytes + level_offset + 4u);
    const u32 data_size = read_u32_le(bytes + level_offset + 8u);
    const u32 pixel_offset = level_offset + 12u;
    const u32 pixel_count = width * height;
    const u32 expected_data_size = format == 0 ? pixel_count * 4u :
        (format == 1 ? pixel_count * 2u : (format == 2 ? pixel_count : (pixel_count + 1u) / 2u));
    const u8 pixel_format = format == 0 ? GS_PSM_CT32 :
        (format == 1 ? GS_PSM_CT16 : (format == 2 ? GS_PSM_T8 : GS_PSM_T4));
    if (width == 0u || height == 0u || width > 1024u || height > 1024u ||
        width != package_width || height != package_height ||
        data_size != expected_data_size || pixel_offset + data_size > package_size)
        return 0;

    const u32 transfer_size = (data_size + 15u) & ~15u;
    u32 *pixels = memalign(128, transfer_size);
    if (pixels == NULL)
        return 0;
    memset(pixels, 0, transfer_size);
    memcpy(pixels, bytes + pixel_offset, data_size);
    /* Desktop RGBA stores alpha in 0..255, while the GS uses 0..128 for
       ordinary source alpha. Preserve zero exactly for cutout texels and
       scale the remaining RGBA32 values into the native range. */
    if (format == 0)
    {
        u8 *pixel_channels = (u8 *)pixels;
        for (u32 index = 0; index < pixel_count; ++index)
            pixel_channels[index * 4u + 3u] >>= 1;
    }

    u32 *palette = NULL;
    if (format >= 2)
    {
        palette = memalign(128, palette_bytes);
        if (palette == NULL) { free(pixels); return 0; }
        memcpy(palette, bytes + palette_offset, palette_bytes);
        u8 *palette_channels = (u8 *)palette;
        for (u32 index = 0; index < palette_count; ++index)
            palette_channels[index * 4u + 3u] >>= 1;
        for (u32 index = 0; format == 2 && index < 256u; ++index)
        {
            if ((index & 0x18u) == 8u)
            {
                const u32 temporary = palette[index];
                palette[index] = palette[index + 8u];
                palette[index + 8u] = temporary;
            }
        }
    }

    if (platform->texture.Mem != NULL)
        free(platform->texture.Mem);
    if (platform->texture.Clut != NULL)
        free(platform->texture.Clut);
    memset(&platform->texture, 0, sizeof(platform->texture));
    platform->texture.Width = width;
    platform->texture.Height = height;
    platform->texture.PSM = pixel_format;
    platform->texture.TBW = (width + 63u) / 64u;
    platform->texture.Mem = pixels;
    platform->texture.Clut = palette;
    platform->texture.ClutPSM = GS_PSM_CT32;
    platform->texture.ClutStorageMode = GS_CLUT_STORAGE_CSM1;
    platform->texture.Filter = filter == 0 ? GS_FILTER_NEAREST : GS_FILTER_LINEAR;
    platform->texture.Vram = gsKit_vram_alloc(
        platform->gs,
        gsKit_texture_size(width, height, pixel_format),
        GSKIT_ALLOC_USERBUFFER);
    if (platform->texture.Vram == GSKIT_ALLOC_ERROR)
    {
        printf("PS2 texture VRAM exhausted loading %ux%u texture\n", width, height);
        free(pixels);
        if (palette != NULL) free(palette);
        memset(&platform->texture, 0, sizeof(platform->texture));
        return 0;
    }
    if (format >= 2)
    {
        platform->texture.VramClut = gsKit_vram_alloc(
            platform->gs,
            gsKit_texture_size(format == 2 ? 16 : 8, format == 2 ? 16 : 2, GS_PSM_CT32),
            GSKIT_ALLOC_USERBUFFER);
        if (platform->texture.VramClut == GSKIT_ALLOC_ERROR)
        {
            printf("PS2 palette VRAM exhausted loading %ux%u texture\n", width, height);
            free(pixels);
            free(palette);
            memset(&platform->texture, 0, sizeof(platform->texture));
            return 0;
        }
    }
    gsKit_texture_upload(platform->gs, &platform->texture);
    platform->texture_ready = 1;
    printf("Loaded R2TX v%u %s texture: %ux%u, %u mip(s)\n",
        version, format == 0 ? "RGBA32" :
            (format == 1 ? "RGBA16" : (format == 2 ? "INDEX8" : "INDEX4")),
        width, height, mip_count);
    return 1;
}

static void *read_asset_file(const char *path, u32 maximum_size, u32 *size)
{
    FILE *file = open_asset(path);
    if (file == NULL) return NULL;
    if (fseek(file, 0, SEEK_END) != 0) { fclose(file); return NULL; }
    long length = ftell(file);
    if (length <= 0 || length > (long)maximum_size || fseek(file, 0, SEEK_SET) != 0)
    { fclose(file); return NULL; }
    void *data = memalign(128, (size_t)length);
    if (data == NULL) { fclose(file); return NULL; }
    if (fread(data, 1, (size_t)length, file) != (size_t)length)
    { fclose(file); free(data); return NULL; }
    fclose(file);
    *size = (u32)length;
    return data;
}

static void trace_scene_load(const char *stage, const char *path, int result)
{
    FILE *trace = fopen("host:r2load.log", "a");
    if (trace == NULL) return;
    fprintf(trace, "%s | %s | %s\n", stage != NULL ? stage : "?",
        path != NULL ? path : "?", result ? "ok" : "FAILED");
    fclose(trace);
}

int r2_ps2_load_texture_file(R2Ps2Platform *platform, const char *path)
{
    u32 size = 0;
    void *data = read_asset_file(path, 4u * 1024u * 1024u, &size);
    if (data == NULL) return 0;
    const int loaded = r2_ps2_load_texture_package(platform, data, size);
    free(data);
    return loaded;
}

int r2_ps2_load_mesh_package(
    R2Ps2Platform *platform,
    const void *package_data,
    unsigned int package_size)
{
    const u8 *bytes = package_data;
    if (platform == NULL || bytes == NULL || package_size < sizeof(R2Ps2MeshPackageHeader))
        return 0;
    const R2Ps2MeshPackageHeader *header = (const R2Ps2MeshPackageHeader *)bytes;
    if (header->magic[0] != 'R' || header->magic[1] != '2' ||
        header->magic[2] != 'M' || header->magic[3] != 'S' ||
        (header->version != 1 && header->version != 2) ||
        header->vertex_stride != sizeof(R2Ps2MeshVertex) ||
        header->index_size != sizeof(u16) || header->vertex_count == 0 ||
        header->vertex_count > 65535u || header->index_count == 0 ||
        header->index_count % 3u != 0 || header->index_count > 196605u)
        return 0;

    const u32 vertex_bytes = header->vertex_count * sizeof(R2Ps2MeshVertex);
    const u32 index_bytes = header->index_count * sizeof(u16);
    const u32 vertex_offset = sizeof(R2Ps2MeshPackageHeader);
    const u32 index_offset = vertex_offset + vertex_bytes;
    if (index_offset > package_size || index_bytes > package_size - index_offset)
        return 0;

    R2Ps2MeshVertex *vertices = memalign(128, vertex_bytes);
    u16 *indices = memalign(128, index_bytes);
    R2ProjectedVertex *projected = memalign(
        128, header->vertex_count * sizeof(R2ProjectedVertex));
    if (vertices == NULL || indices == NULL || projected == NULL)
    {
        free(vertices);
        free(indices);
        free(projected);
        return 0;
    }
    memcpy(vertices, bytes + vertex_offset, vertex_bytes);
    memcpy(indices, bytes + index_offset, index_bytes);
    for (u32 index = 0; index < header->index_count; ++index)
    {
        if (indices[index] >= header->vertex_count)
        {
            free(vertices);
            free(indices);
            free(projected);
            return 0;
        }
    }

    void *meshlet_blob = NULL;
    R2Ps2Meshlet *meshlets = NULL;
    u32 meshlet_count = 0u;
    const u32 meshlet_offset = index_offset + index_bytes;
    if (header->version >= 2u)
    {
        if (meshlet_offset + 16u > package_size ||
            memcmp(bytes + meshlet_offset, "R2ML", 4u) != 0 ||
            read_u32_le(bytes + meshlet_offset + 4u) != 1u)
            goto meshlet_failure;
        meshlet_count = read_u32_le(bytes + meshlet_offset + 8u);
        if (meshlet_count == 0u || meshlet_count > header->index_count / 3u)
            goto meshlet_failure;
        const u32 blob_size = package_size - meshlet_offset;
        meshlet_blob = memalign(128, (blob_size + 127u) & ~127u);
        meshlets = calloc(meshlet_count, sizeof(*meshlets));
        if (meshlet_blob == NULL || meshlets == NULL)
            goto meshlet_failure;
        memcpy(meshlet_blob, bytes + meshlet_offset, blob_size);
        u32 cursor = meshlet_offset + 16u;
        for (u32 meshlet = 0; meshlet < meshlet_count; ++meshlet)
        {
            if (cursor + 16u > package_size) goto meshlet_failure;
            const u16 local_vertex_count = read_u16_le(bytes + cursor);
            const u16 triangle_count = read_u16_le(bytes + cursor + 2u);
            const u32 data_bytes = read_u32_le(bytes + cursor + 4u);
            cursor += 16u;
            const u32 required = (u32)local_vertex_count * 2u +
                (u32)triangle_count * 3u;
            if (local_vertex_count == 0u || local_vertex_count > 64u ||
                triangle_count == 0u || triangle_count > 96u ||
                data_bytes != required || cursor + required > package_size)
                goto meshlet_failure;
            R2Ps2Meshlet *output = &meshlets[meshlet];
            output->vertex_count = local_vertex_count;
            output->triangle_count = triangle_count;
            output->vertices = (const u16 *)((u8 *)meshlet_blob +
                (cursor - meshlet_offset));
            output->indices = (const u8 *)output->vertices +
                (u32)local_vertex_count * 2u;
            for (u32 vertex = 0; vertex < local_vertex_count; ++vertex)
                if (output->vertices[vertex] >= header->vertex_count)
                    goto meshlet_failure;
            for (u32 axis = 0; axis < 3u; ++axis)
            {
                output->bounds_min[axis] = 1.0e30f;
                output->bounds_max[axis] = -1.0e30f;
            }
            for (u32 vertex = 0; vertex < local_vertex_count; ++vertex)
            {
                const R2Ps2MeshVertex *point = &vertices[output->vertices[vertex]];
                const float value[3] = {point->x, point->y, point->z};
                for (u32 axis = 0; axis < 3u; ++axis)
                {
                    if (value[axis] < output->bounds_min[axis]) output->bounds_min[axis] = value[axis];
                    if (value[axis] > output->bounds_max[axis]) output->bounds_max[axis] = value[axis];
                }
            }
            for (u32 corner = 0; corner < (u32)triangle_count * 3u; ++corner)
                if (output->indices[corner] >= local_vertex_count)
                    goto meshlet_failure;
            cursor = (cursor + required + 15u) & ~15u;
        }
    }

    free(platform->mesh_vertices);
    free(platform->mesh_indices);
    free(platform->mesh_projected);
    free(platform->meshlet_blob);
    free(platform->meshlets);
    platform->mesh_vertices = vertices;
    platform->mesh_indices = indices;
    platform->mesh_projected = projected;
    platform->meshlet_blob = meshlet_blob;
    platform->meshlets = meshlets;
    platform->meshlet_count = meshlet_count;
    platform->mesh_vertex_count = header->vertex_count;
    platform->mesh_index_count = header->index_count;
    printf("Loaded R2MS v%u mesh: %u vertices, %u triangles, %u meshlets\n",
        header->version, header->vertex_count, header->index_count / 3u,
        meshlet_count);
    return 1;

meshlet_failure:
    free(vertices);
    free(indices);
    free(projected);
    free(meshlet_blob);
    free(meshlets);
    return 0;
}

int r2_ps2_load_mesh_file(R2Ps2Platform *platform, const char *path)
{
    u32 size = 0;
    void *data = read_asset_file(path, 8u * 1024u * 1024u, &size);
    if (data == NULL) return 0;
    const int loaded = r2_ps2_load_mesh_package(platform, data, size);
    free(data);
    return loaded;
}

static int load_animation_file(R2Ps2MeshResource *resource, const char *path)
{
    u32 size = 0;
    u8 *bytes = read_asset_file(path, 32u * 1024u * 1024u, &size);
    if (bytes == NULL) return 0;
    if (size < 16u || bytes[0] != 'R' || bytes[1] != '2' ||
        bytes[2] != 'A' || bytes[3] != 'N' || (read_u32_le(bytes + 4) < 2u || read_u32_le(bytes + 4) > 10u))
    { free(bytes); return 0; }
    const u32 version = read_u32_le(bytes + 4);
    const int compact_skeletal = version == 6u || version == 7u || version == 9u;
    const u32 vertex_count = read_u32_le(bytes + 8);
    const u32 state_count = read_u32_le(bytes + 12);
    const u32 stride = version >= 9u ? 44u : version >= 7u ? 40u : version >= 4u ? 36u : version >= 3u ? 28u : 20u;
    if (vertex_count != resource->vertex_count || state_count == 0u ||
        state_count > R2_PS2_MAX_ANIMATION_STATES || 16u + state_count * stride > size)
    { free(bytes); return 0; }
    u32 data_start = 16u + state_count * stride;
    u32 bone_count = 0u;
    u32 skin_offset = 0u;
    if (compact_skeletal)
    {
        if (size - data_start < 4u) { free(bytes); return 0; }
        bone_count = read_u32_le(bytes + data_start);
        data_start += 4u;
        skin_offset = data_start;
        const u64 skin_bytes = (u64)vertex_count * 8u;
        if (bone_count == 0u || bone_count > 128u || skin_bytes > size - data_start)
        { free(bytes); return 0; }
        data_start += (u32)skin_bytes;
    }
    R2AnimController parsed = {0};
    if (version >= 5u)
    {
        if (size-data_start < 8u) { free(bytes); return 0; }
        parsed.parameter_count = read_u32_le(bytes+data_start);
        parsed.transition_count = read_u32_le(bytes+data_start+4u);
        data_start += 8u;
        if (parsed.parameter_count>32u || parsed.transition_count>128u ||
            parsed.parameter_count*72u+parsed.transition_count*24u>size-data_start) { free(bytes); return 0; }
        for (u32 i=0; i<parsed.parameter_count; i++,data_start+=72u)
        {
            R2AnimParameter *p=&parsed.parameters[i];
            p->type=read_u32_le(bytes+data_start);
            p->binding=p->type>>8; p->type &= 255u;
            memcpy(&p->initial,bytes+data_start+4u,4);
            memcpy(p->name,bytes+data_start+8u,64);
            if (p->type>3u || p->binding>8u || !isfinite(p->initial) || !memchr(p->name,0,64)) { free(bytes); return 0; }
        }
        for (u32 i=0; i<parsed.transition_count; i++,data_start+=24u)
        {
            R2AnimTransition *t=&parsed.transitions[i];
            t->from=read_u32_le(bytes+data_start); t->to=read_u32_le(bytes+data_start+4u);
            t->condition=read_u32_le(bytes+data_start+8u); t->parameter=read_u32_le(bytes+data_start+12u);
            memcpy(&t->threshold,bytes+data_start+16u,4); memcpy(&t->blend,bytes+data_start+20u,4);
            if(t->from>=state_count || t->to>=state_count || t->condition>7u ||
                (t->condition!=6u && t->condition!=7u && t->parameter>=parsed.parameter_count) ||
                !isfinite(t->threshold) || !isfinite(t->blend) || t->blend<0) { free(bytes); return 0; }
        }
    }
    const u32 payload_size = size - data_start;
    float *frames = memalign(128, payload_size);
    if (frames == NULL) { free(bytes); return 0; }
    memcpy(frames, bytes + data_start, payload_size);
    for (u32 state = 0; state < state_count; ++state)
    {
        const u32 record = 16u + state * stride;
        R2Ps2AnimationState *output = &resource->animation_states[state];
        output->kind = read_u32_le(bytes + record);
        output->frame_count = read_u32_le(bytes + record + 4u);
        memcpy(&output->fps, bytes + record + 8u, sizeof(float));
        memcpy(&output->duration, bytes + record + 12u, sizeof(float));
        output->loop = stride >= 28u ? read_u32_le(bytes + record + 20u) : 1u;
        output->speed = 1.0f;
        if (stride >= 28u) memcpy(&output->speed, bytes + record + 24u, sizeof(float));
        output->finished_target = stride >= 36u ? read_u32_le(bytes + record + 28u) : 0xffffffffu;
        output->finished_blend = 0;
        if (stride >= 36u) memcpy(&output->finished_blend, bytes + record + 32u, sizeof(float));
        output->jump_launch_time = -1.0f;
        if (stride >= 40u) memcpy(&output->jump_launch_time, bytes + record + 36u, sizeof(float));
        output->lock_movement = stride >= 44u ? read_u32_le(bytes + record + 40u) : 0u;
        if (output->lock_movement > 1u) { free(frames); free(bytes); return 0; }
        const u32 file_offset = read_u32_le(bytes + record + 16u);
        const u64 state_bytes = compact_skeletal
            ? (u64)output->frame_count * bone_count * 12u * sizeof(float)
            : (u64)output->frame_count * vertex_count * 6u * sizeof(float);
        if ((output->finished_target != 0xffffffffu && output->finished_target >= state_count) || !isfinite(output->finished_blend) || output->finished_blend < 0 ||
            !isfinite(output->jump_launch_time) || output->jump_launch_time < -1.0f || output->jump_launch_time > output->duration ||
            output->loop > 1u || !isfinite(output->speed) || output->frame_count < 2u || !isfinite(output->fps) || output->fps <= 0.0f ||
            !isfinite(output->duration) || output->duration <= 0.0f || file_offset < data_start || file_offset > size || state_bytes > size - file_offset)
        { free(frames); free(bytes); return 0; }
        output->float_offset = (file_offset - data_start) / sizeof(float);
    }
    u8 *skin = NULL;
    if (compact_skeletal)
    {
        const u32 skin_bytes = vertex_count * 8u;
        skin = memalign(128, skin_bytes);
        if (skin == NULL) { free(frames); free(bytes); return 0; }
        memcpy(skin, bytes + skin_offset, skin_bytes);
        for (u32 vertex = 0; vertex < vertex_count; ++vertex)
            for (u32 influence = 0; influence < 4u; ++influence)
                if (skin[vertex * 8u + influence] >= bone_count)
                { free(skin); free(frames); free(bytes); return 0; }
    }
    free(bytes);
    resource->animated_vertices = memalign(128, vertex_count * sizeof(R2Ps2MeshVertex));
    if (resource->animated_vertices == NULL) { free(skin); free(frames); return 0; }
    memcpy(resource->animated_vertices, resource->vertices, vertex_count * sizeof(R2Ps2MeshVertex));
    resource->animation_frames = frames;
    resource->animation_skin = skin;
    resource->animation_bone_count = bone_count;
    if (parsed.parameter_count || parsed.transition_count)
    {
        resource->controller = malloc(sizeof(parsed));
        if (!resource->controller) { free(resource->animated_vertices); resource->animated_vertices=NULL; free(skin); resource->animation_skin=NULL; free(frames); resource->animation_frames=NULL; return 0; }
        *resource->controller = parsed;
    }
    resource->animation_state_count = state_count;
    resource->animation_state = 0u;
    resource->animation_previous_state = 0u;
    resource->animation_time = 0.0f;
    resource->animation_blend = 1.0f;
    printf("Loaded R2AN controller: %u states\n", state_count);
    return 1;
}

int r2_ps2_load_scene_package(
    R2Ps2Platform *platform,
    const void *package_data,
    unsigned int package_size)
{
    const u8 *bytes = package_data;
    if (platform == NULL || bytes == NULL || package_size < 20u ||
        bytes[0] != 'R' || bytes[1] != '2' || bytes[2] != 'S' || bytes[3] != 'C')
        return 0;
    const u32 version = read_u32_le(bytes + 4);
    const u32 mesh_count = read_u32_le(bytes + 8);
    const u32 texture_count = read_u32_le(bytes + 12);
    const u32 instance_count = read_u32_le(bytes + 16);
    if ((version < 1 || version > 54) ||
        mesh_count > R2_PS2_MAX_SCENE_MESHES ||
        texture_count > R2_PS2_MAX_SCENE_TEXTURES ||
        instance_count > 4096u)
        return 0;
    u32 offset = 20u;
    if (version >= 2)
    {
        if (offset + sizeof(R2Ps2SceneCamera) > package_size) return 0;
        memcpy(&platform->scene_camera, bytes + offset, sizeof(R2Ps2SceneCamera));
        offset += sizeof(R2Ps2SceneCamera);
        if (platform->scene_camera.near_clip <= 0.0f)
            platform->scene_camera.near_clip = 0.01f;
        if (platform->scene_camera.far_clip <= platform->scene_camera.near_clip)
            platform->scene_camera.far_clip = platform->scene_camera.near_clip + 0.01f;
        if (platform->scene_camera.field_of_view < 1.0f ||
            platform->scene_camera.field_of_view > 179.0f ||
            platform->scene_camera.aspect <= 0.0f)
            return 0;
    }
    else
    {
        memset(&platform->scene_camera, 0, sizeof(platform->scene_camera));
        platform->scene_camera.position[2] = 6.5f;
        platform->scene_camera.field_of_view = 60.0f;
        platform->scene_camera.near_clip = 0.1f;
        platform->scene_camera.far_clip = 1000.0f;
        platform->scene_camera.aspect = 4.0f / 3.0f;
    }
    if (version >= 3)
    {
        if (offset + sizeof(R2Ps2SceneLighting) > package_size) return 0;
        memcpy(&platform->scene_lighting, bytes + offset, sizeof(R2Ps2SceneLighting));
        offset += sizeof(R2Ps2SceneLighting);
        for (int channel = 0; channel < 3; ++channel)
        {
            if (platform->scene_lighting.background[channel] < 0.0f)
                platform->scene_lighting.background[channel] = 0.0f;
            if (platform->scene_lighting.background[channel] > 1.0f)
                platform->scene_lighting.background[channel] = 1.0f;
            if (platform->scene_lighting.ambient[channel] < 0.0f)
                platform->scene_lighting.ambient[channel] = 0.0f;
        }
        platform->background = GS_SETREG_RGBAQ(
            (u8)(platform->scene_lighting.background[0] * 255.0f),
            (u8)(platform->scene_lighting.background[1] * 255.0f),
            (u8)(platform->scene_lighting.background[2] * 255.0f), 0, 0);
    }
    if (version >= 4)
    {
        if (offset + sizeof(R2Ps2SceneFog) > package_size) return 0;
        memcpy(&platform->scene_fog, bytes + offset, sizeof(R2Ps2SceneFog));
        offset += sizeof(R2Ps2SceneFog);
        if (platform->scene_fog.start < 0.0f) platform->scene_fog.start = 0.0f;
        if (platform->scene_fog.end <= platform->scene_fog.start)
            platform->scene_fog.end = platform->scene_fog.start + 0.1f;
        for (int channel = 0; channel < 3; ++channel)
        {
            if (platform->scene_fog.color[channel] < 0.0f) platform->scene_fog.color[channel] = 0.0f;
            if (platform->scene_fog.color[channel] > 1.0f) platform->scene_fog.color[channel] = 1.0f;
        }
    }
    memset(platform->scene_mesh_paths, 0, sizeof(platform->scene_mesh_paths));
    memset(platform->scene_texture_paths, 0, sizeof(platform->scene_texture_paths));
    for (u32 table = 0; table < mesh_count + texture_count; ++table)
    {
        if (offset + 4u > package_size) return 0;
        const u32 length = read_u32_le(bytes + offset); offset += 4u;
        if (length == 0 || length > 1024u || length > package_size - offset) return 0;
        char *destination = table < mesh_count
            ? platform->scene_mesh_paths[table]
            : platform->scene_texture_paths[table - mesh_count];
        const u32 copy_length = length < 255u ? length : 255u;
        memcpy(destination, bytes + offset, copy_length);
        destination[copy_length] = '\0';
        offset += length;
    }
    const u32 records_size = instance_count * sizeof(R2Ps2SceneInstance);
    if (records_size > package_size - offset) return 0;
    /* UI-only scenes have empty world tables. Avoid allocator-specific
       memalign(0) behavior while retaining zero record counts on disk/runtime. */
    R2Ps2SceneInstance *instances = memalign(128, records_size ? records_size : 128u);
    if (instances == NULL) return 0;
    memcpy(instances, bytes + offset, records_size);
    for (u32 index = 0; index < instance_count; ++index)
    {
        if ((instances[index].mesh_slot != 0xffffffffu &&
             instances[index].mesh_slot >= mesh_count) ||
            (instances[index].texture_slot != 0xffffffffu &&
             instances[index].texture_slot >= texture_count))
        { free(instances); return 0; }
    }

    const u32 materials_size = instance_count * sizeof(R2Ps2SceneMaterial);
    R2Ps2SceneMaterial *materials = memalign(128, materials_size ? materials_size : 128u);
    if (materials == NULL) { free(instances); return 0; }
    if (version >= 5)
    {
        if (materials_size > package_size - offset - records_size)
        { free(instances); free(materials); return 0; }
        memcpy(materials, bytes + offset + records_size, materials_size);
        for (u32 index = 0; index < instance_count; ++index)
        {
            const u32 maximum_lighting_mode = version >= 50u ? 2u : 1u;
            if (materials[index].lit > maximum_lighting_mode || materials[index].surface_mode > 2u ||
                materials[index].double_sided > 1u || materials[index].receive_fog > 1u ||
                materials[index].alpha_cutoff < 0.0f || materials[index].alpha_cutoff > 1.0f)
            { free(instances); free(materials); return 0; }
        }
    }
    else
    {
        for (u32 index = 0; index < instance_count; ++index)
        {
            materials[index].lit = 1;
            materials[index].surface_mode = 0;
            materials[index].double_sided = 0;
            materials[index].receive_fog = 1;
            materials[index].alpha_cutoff = 0.5f;
        }
    }

    const u32 collider_record_size = version >= 52 ? sizeof(R2Ps2SceneCollider) : 32u;
    const u32 colliders_size = instance_count * collider_record_size;
    const u32 colliders_allocation = instance_count * sizeof(R2Ps2SceneCollider);
    R2Ps2SceneCollider *colliders = memalign(128, colliders_allocation ? colliders_allocation : 128u);
    if (colliders == NULL) { free(instances); free(materials); return 0; }
    memset(colliders, 0, colliders_allocation);
    if (version >= 6)
    {
        const u32 collider_offset = offset + records_size + materials_size;
        if (collider_offset > package_size || colliders_size > package_size - collider_offset)
        { free(instances); free(materials); free(colliders); return 0; }
        for (u32 index = 0; index < instance_count; ++index)
        {
            memcpy(&colliders[index], bytes + collider_offset + index * collider_record_size,
                collider_record_size);
            if (version < 52)
            {
                colliders[index].layer = 0u;
                colliders[index].collision_mask = 0xffffffffu;
            }
            if (colliders[index].type > 4u || colliders[index].radius < 0.0f ||
                colliders[index].height < 0.0f || colliders[index].slope_limit < 0.0f ||
                colliders[index].layer > 31u ||
                (colliders[index].type != 3u && colliders[index].type != 4u && colliders[index].slope_limit > 89.0f))
            { free(instances); free(materials); free(colliders); return 0; }
        }
    }

    const u32 transitions_size = instance_count * sizeof(R2Ps2SceneTransition);
    R2Ps2SceneTransition *transitions = memalign(128, transitions_size ? transitions_size : 128u);
    if (transitions == NULL)
    { free(instances); free(materials); free(colliders); return 0; }
    memset(transitions, 0, transitions_size);
    if (version >= 7)
    {
        const u32 transition_offset = offset + records_size + materials_size + colliders_size;
        if (transition_offset > package_size || transitions_size > package_size - transition_offset)
        { free(instances); free(materials); free(colliders); free(transitions); return 0; }
        memcpy(transitions, bytes + transition_offset, transitions_size);
        for (u32 index = 0; index < instance_count; ++index)
        {
            transitions[index].scene_name[127] = '\0';
            if (transitions[index].enabled > 1u)
            { free(instances); free(materials); free(colliders); free(transitions); return 0; }
        }
    }

    R2Ps2SceneControls controls = {
        1u, 0, 1, 2, 1, 0, 0.2f, 0.2f, 4.0f, 120.0f, 1.75f, 5.0f,
        1u, 1u, 1u, 1u, 1u, 90.0f, -75.0f, 60.0f, 0u, 1u, 0.2f, 0.05f, 6.0f, 1.0f,
        1u, -100.0f, 0xffffffffu, 1u, 0.35f
    };
    const u32 controls_size = version >= 54 ? sizeof(R2Ps2SceneControls) : version >= 53 ? 116u : version >= 51 ? 112u : version >= 18 ? 104u : version >= 17 ? 84u : 68u;
    if (version >= 8)
    {
        const u32 controls_offset = offset + records_size + materials_size +
            colliders_size + transitions_size;
        if (controls_offset > package_size ||
            controls_size > package_size - controls_offset)
        { free(instances); free(materials); free(colliders); free(transitions); return 0; }
        memcpy(&controls, bytes + controls_offset, controls_size);
        if (controls.enabled > 1u || controls.move_x_axis < -1 || controls.move_x_axis > 3 ||
            controls.move_y_axis < -1 || controls.move_y_axis > 3 ||
            controls.turn_axis < -1 || controls.turn_axis > 3 ||
            controls.sprint_button < -1 || controls.sprint_button > 15 ||
            controls.jump_button < -1 || controls.jump_button > 15 ||
            controls.move_dead_zone < 0.0f || controls.move_dead_zone > 0.95f ||
            controls.turn_dead_zone < 0.0f || controls.turn_dead_zone > 0.95f ||
            controls.move_speed < 0.0f || controls.turn_speed < 0.0f ||
            controls.sprint_multiplier < 1.0f || controls.jump_strength < 0.0f ||
            controls.invert_move_x > 1u || controls.invert_move_y > 1u ||
            controls.invert_turn > 1u || controls.enable_sprint > 1u ||
            controls.enable_jump > 1u || controls.invert_camera_pitch > 1u ||
            !isfinite(controls.camera_pitch_speed) || controls.camera_pitch_speed < 0.0f ||
            !isfinite(controls.camera_min_pitch) || !isfinite(controls.camera_max_pitch) ||
            controls.camera_min_pitch < -85.0f || controls.camera_max_pitch > 85.0f ||
            controls.camera_min_pitch > controls.camera_max_pitch ||
            controls.camera_collision > 1u || !isfinite(controls.camera_collision_radius) || controls.camera_collision_radius < 0.01f ||
            !isfinite(controls.camera_clearance) || controls.camera_clearance < 0 ||
            !isfinite(controls.camera_return_speed) || controls.camera_return_speed < 0 || !isfinite(controls.camera_target_height) ||
            controls.kill_floor_enabled > 1u || !isfinite(controls.kill_floor_height) ||
            controls.launch_jump_from_animation_event > 1u || !isfinite(controls.jump_event_timeout) || controls.jump_event_timeout < 0.0f)
        { free(instances); free(materials); free(colliders); free(transitions); return 0; }
    }

    R2Ps2SceneAudio audio;
    memset(&audio, 0, sizeof(audio));
    if (version >= 9)
    {
        const u32 audio_offset = offset + records_size + materials_size +
            colliders_size + transitions_size + controls_size;
        if (audio_offset > package_size || sizeof(R2Ps2SceneAudio) > package_size - audio_offset)
        { free(instances); free(materials); free(colliders); free(transitions); return 0; }
        memcpy(&audio, bytes + audio_offset, sizeof(audio));
        audio.clip_path[255] = '\0';
        if (audio.enabled > 1u || audio.loop > 1u || audio.spatial > 1u ||
            audio.volume < 0.0f || audio.volume > 1.0f ||
            audio.pitch < 0.25f || audio.pitch > 4.0f ||
            audio.min_distance <= 0.0f || audio.max_distance < audio.min_distance)
        { free(instances); free(materials); free(colliders); free(transitions); return 0; }
    }

    const u32 save_points_size = instance_count * sizeof(R2Ps2SceneSavePoint);
    R2Ps2SceneSavePoint *save_points = memalign(128, save_points_size ? save_points_size : 128u);
    if (save_points == NULL)
    { free(instances); free(materials); free(colliders); free(transitions); return 0; }
    memset(save_points, 0, save_points_size);
    if (version >= 10)
    {
        const u32 save_points_offset = offset + records_size + materials_size +
            colliders_size + transitions_size + controls_size +
            sizeof(R2Ps2SceneAudio);
        if (save_points_offset > package_size ||
            save_points_size > package_size - save_points_offset)
        { free(instances); free(materials); free(colliders); free(transitions); free(save_points); return 0; }
        memcpy(save_points, bytes + save_points_offset, save_points_size);
        for (u32 index = 0; index < instance_count; ++index)
        {
            save_points[index].slot_name[31] = '\0';
            if (save_points[index].enabled > 1u)
            { free(instances); free(materials); free(colliders); free(transitions); free(save_points); return 0; }
        }
    }

    const u32 persistent_record_size = version >= 12
        ? sizeof(R2Ps2ScenePersistentObject) : 36u;
    const u32 persistent_objects_size = instance_count * sizeof(R2Ps2ScenePersistentObject);
    R2Ps2ScenePersistentObject *persistent_objects = memalign(128, persistent_objects_size ? persistent_objects_size : 128u);
    if (persistent_objects == NULL)
    { free(instances); free(materials); free(colliders); free(transitions); free(save_points); return 0; }
    memset(persistent_objects, 0, persistent_objects_size);
    if (version >= 11)
    {
        const u32 persistent_objects_offset = offset + records_size + materials_size +
            colliders_size + transitions_size + controls_size +
            sizeof(R2Ps2SceneAudio) + save_points_size;
        const u32 stored_persistent_size = instance_count * persistent_record_size;
        if (persistent_objects_offset > package_size ||
            stored_persistent_size > package_size - persistent_objects_offset)
        { free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); return 0; }
        for (u32 index = 0; index < instance_count; ++index)
        {
            const unsigned char *record = bytes + persistent_objects_offset + index * persistent_record_size;
            memcpy(&persistent_objects[index], record, persistent_record_size);
            persistent_objects[index].save_id[31] = '\0';
            if (version < 12)
            {
                persistent_objects[index].save_transform = persistent_objects[index].enabled;
                persistent_objects[index].save_active_state = 0u;
                persistent_objects[index].active = 1u;
            }
            if (persistent_objects[index].enabled > 1u ||
                persistent_objects[index].save_transform > 1u ||
                persistent_objects[index].save_active_state > 1u ||
                persistent_objects[index].save_integer > 1u ||
                persistent_objects[index].save_boolean > 1u ||
                persistent_objects[index].active > 1u ||
                persistent_objects[index].boolean_value > 1u)
            { free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); return 0; }
        }
    }

    float ui_reference[2] = {640.0f, 448.0f};
    u32 ui_count = 0u;
    u32 playback_offset = 0u;
    u32 ui_visible = 0u;
    u32 ui_pause_gameplay = 1u;
    u32 ui_loading = 0u;
    u32 ui_toggle_input = 0xffffffffu, ui_cancel_input = 0xffffffffu;
    R2Ps2SceneUiElement *ui_elements = NULL;
    if (version >= 13)
    {
        const u32 ui_offset = offset + records_size + materials_size + colliders_size +
            transitions_size + controls_size + sizeof(R2Ps2SceneAudio) +
            save_points_size + instance_count * persistent_record_size;
        const u32 ui_header_size = version >= 20 ? 32u : version >= 19 ? 24u : version >= 14 ? 20u : 12u;
        const u32 ui_record_size = version >= 23 ? sizeof(R2Ps2SceneUiElement) : version >= 22 ? 520u : version >= 16 ? 504u : version >= 15 ? 440u : version >= 14 ? 180u : 144u;
        if (ui_offset > package_size || ui_header_size > package_size - ui_offset)
        { free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); return 0; }
        memcpy(ui_reference, bytes + ui_offset, sizeof(float) * 2u);
        ui_count = read_u32_le(bytes + ui_offset + 8u);
        playback_offset = ui_offset + ui_header_size + ui_count * ui_record_size;
        if (version >= 14)
        {
            ui_visible = read_u32_le(bytes + ui_offset + 12u);
            ui_pause_gameplay = read_u32_le(bytes + ui_offset + 16u);
        }
        if (version >= 19) ui_loading = read_u32_le(bytes + ui_offset + 20u);
        if (version >= 20)
        {
            ui_toggle_input = read_u32_le(bytes + ui_offset + 24u);
            ui_cancel_input = read_u32_le(bytes + ui_offset + 28u);
        }
        if (ui_reference[0] <= 0.0f || ui_reference[1] <= 0.0f || ui_count > R2_PS2_MAX_UI_ELEMENTS ||
            ui_count * ui_record_size > package_size - ui_offset - ui_header_size)
        { free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); return 0; }
        if (ui_count > 0u)
        {
            ui_elements = memalign(128, ui_count * sizeof(R2Ps2SceneUiElement));
            if (ui_elements == NULL)
            { free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); return 0; }
            memset(ui_elements, 0, ui_count * sizeof(R2Ps2SceneUiElement));
            for (u32 index = 0; index < ui_count; ++index)
            {
                memcpy(&ui_elements[index], bytes + ui_offset + ui_header_size + index * ui_record_size,
                    ui_record_size);
                if (version < 22)
                { ui_elements[index].sprite_region[2] = 1; ui_elements[index].sprite_region[3] = 1; }
                ui_elements[index].text[63] = '\0';
                ui_elements[index].save_slot[31] = '\0';
                ui_elements[index].target_scene[127] = '\0';
                for (u32 message = 0; message < 4u; ++message)
                    ui_elements[index].result_messages[message][63] = '\0';
                if (ui_elements[index].type < 1u || ui_elements[index].type > 4u ||
                    ui_elements[index].save_load_feedback > 1u ||
                    (ui_elements[index].interactable & 0xffffu) > 1u || (ui_elements[index].interactable >> 16) >= 32u ||
                    (ui_elements[index].action & 0xffffu) > 9u || (ui_elements[index].action >> 16) >= 32u)
                { free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
            }
        }
    }

    R2AnimatorZone parsed_zones[64] = {0};
    u32 zone_count=0;
    if(version>=25u)
    {
        int valid=playback_offset<=package_size && package_size-playback_offset>=4u;
        if(valid) { zone_count=read_u32_le(bytes+playback_offset); playback_offset+=4u; }
        valid=valid && zone_count<=64u && zone_count*96u<=package_size-playback_offset;
        if(valid) for(u32 i=0;i<zone_count;i++,playback_offset+=96u)
        {
            R2AnimatorZone *z=&parsed_zones[i];
            z->target=read_u32_le(bytes+playback_offset);
            memcpy(z->center,bytes+playback_offset+4u,12); memcpy(z->size,bytes+playback_offset+16u,12);
            memcpy(z->trigger,bytes+playback_offset+28u,64); z->enabled=read_u32_le(bytes+playback_offset+92u);
            if(z->target>=instance_count || !memchr(z->trigger,0,64) || z->enabled>1u) valid=0;
            for(u32 axis=0;axis<3;axis++) if(!isfinite(z->center[axis]) || !isfinite(z->size[axis]) || z->size[axis]<0) valid=0;
        }
        if(!valid) { free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
    }
    R2Interactable *parsed_interactions=NULL; u32 interaction_count=0;
    if(version>=26u)
    {
        int valid=playback_offset<=package_size && package_size-playback_offset>=4u;
        if(valid) { interaction_count=read_u32_le(bytes+playback_offset); playback_offset+=4u; }
        const u32 interaction_size=version>=28u ? 624u : version>=27u ? 616u : 100u;
        valid=valid && interaction_count<=64u && interaction_count*interaction_size<=package_size-playback_offset;
        if(valid && interaction_count)
        {
            parsed_interactions=calloc(interaction_count,sizeof(R2Interactable));
            if(!parsed_interactions) valid=0;
        }
        if(valid) for(u32 i=0;i<interaction_count;i++,playback_offset+=interaction_size)
        {
            R2Interactable *p=&parsed_interactions[i];
            memcpy(p->center,bytes+playback_offset,12); memcpy(p->size,bytes+playback_offset+12,12);
            p->canvas=read_u32_le(bytes+playback_offset+24); p->button=read_u32_le(bytes+playback_offset+28);
            memcpy(p->prompt,bytes+playback_offset+32,64); p->source=read_u32_le(bytes+playback_offset+96);
            if(version>=27u)
            {
                p->page_count=read_u32_le(bytes+playback_offset+100);
                memcpy(p->pages,bytes+playback_offset+104,512);
            }
            p->font_slot=-1; p->font_size=18.0f;
            if(version>=28u)
            {
                p->font_slot=(s32)read_u32_le(bytes+playback_offset+616);
                memcpy(&p->font_size,bytes+playback_offset+620,4);
            }
            if(p->canvas>=32u || p->button>15u || !memchr(p->prompt,0,64) ||
                (p->source!=0xffffffffu && p->source>=instance_count) || p->page_count>8u ||
                p->font_slot>=(s32)texture_count || !isfinite(p->font_size) || p->font_size<4) valid=0;
            for(u32 page=0;page<p->page_count;page++) if(!memchr(p->pages[page],0,64)) valid=0;
            for(u32 a=0;a<3;a++) if(!isfinite(p->center[a]) || !isfinite(p->size[a]) || p->size[a]<0) valid=0;
        }
        if(!valid) { free(parsed_interactions); free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
    }
    R2SlidingDoor parsed_doors[64];u32 door_count=0;
    if(version>=44)
    {
        int valid=playback_offset<=package_size && package_size-playback_offset>=4;
        if(valid){door_count=read_u32_le(bytes+playback_offset);playback_offset+=4;}
        valid=valid && door_count<=64 && door_count*32u<=package_size-playback_offset;
        for(u32 i=0;valid && i<door_count;i++,playback_offset+=32)
        {
            valid=r2_door_read(&parsed_doors[i],bytes+playback_offset,instance_count);
            if(valid && colliders[parsed_doors[i].source].type!=3u)valid=0;
            for(u32 j=0;valid && j<i;j++)if(parsed_doors[j].source==parsed_doors[i].source)valid=0;
        }
        if(!valid){free(parsed_interactions);free(instances);free(materials);free(colliders);free(transitions);free(save_points);free(persistent_objects);free(ui_elements);return 0;}
    }
    R2Ps2SceneDraw *parsed_draws = NULL; u32 draw_count = 0u;
    if(version>=45)
    {
        const u32 draw_record_size=version>=46 ? sizeof(R2Ps2SceneDraw) : 56u;
        int valid=playback_offset<=package_size && package_size-playback_offset>=4u;
        if(valid){draw_count=read_u32_le(bytes+playback_offset);playback_offset+=4u;}
        valid=valid && (draw_count>0u || instance_count==0u) && draw_count<=4096u &&
            draw_count*draw_record_size<=package_size-playback_offset;
        if(valid && draw_count>0u) parsed_draws=calloc(draw_count,sizeof(R2Ps2SceneDraw));
        if(valid && draw_count>0u && parsed_draws==NULL)valid=0;
        for(u32 i=0;valid && i<draw_count;i++,playback_offset+=draw_record_size)
        {
            memcpy(&parsed_draws[i],bytes+playback_offset,draw_record_size);
            R2Ps2SceneDraw *d=&parsed_draws[i];
            if(version<46)
            { d->texture_tiling[0]=1.0f; d->texture_tiling[1]=1.0f; }
            valid=d->source_instance<instance_count && d->mesh_slot<mesh_count &&
                (d->texture_slot==0xffffffffu || d->texture_slot<texture_count) &&
                d->index_count>0u && d->index_count%3u==0u &&
                d->material.lit<=(version>=50u?2u:1u) && d->material.surface_mode<=2u &&
                d->material.double_sided<=1u && d->material.receive_fog<=1u &&
                isfinite(d->material.alpha_cutoff) && d->material.alpha_cutoff>=0.0f && d->material.alpha_cutoff<=1.0f;
            for(u32 c=0;valid && c<4u;c++)valid=isfinite(d->color[c]);
            for(u32 c=0;valid && c<2u;c++)valid=isfinite(d->texture_tiling[c]) && isfinite(d->texture_offset[c]);
        }
        if(!valid){free(parsed_draws);free(parsed_interactions);free(instances);free(materials);free(colliders);free(transitions);free(save_points);free(persistent_objects);free(ui_elements);return 0;}
    }
    u8 **parsed_baked_colors = NULL;
    u32 *parsed_baked_counts = NULL;
    if (version >= 47)
    {
        if (instance_count != 0u)
        {
            parsed_baked_colors = calloc(instance_count, sizeof(*parsed_baked_colors));
            parsed_baked_counts = calloc(instance_count, sizeof(*parsed_baked_counts));
        }
        /* calloc(0, ...) may legally return NULL. UI-only/loading scenes have
           no world instances and therefore no bake records to allocate. */
        int valid = instance_count == 0u ||
            (parsed_baked_colors != NULL && parsed_baked_counts != NULL);
        for (u32 instance = 0; valid && instance < instance_count; ++instance)
        {
            if (playback_offset > package_size || package_size - playback_offset < 4u)
            { valid = 0; break; }
            const u32 count = read_u32_le(bytes + playback_offset); playback_offset += 4u;
            if (count > 1048576u || count > (package_size - playback_offset) / 3u)
            { valid = 0; break; }
            parsed_baked_counts[instance] = count;
            if (count != 0u)
            {
                parsed_baked_colors[instance] = malloc(count * 3u);
                if (parsed_baked_colors[instance] == NULL) { valid = 0; break; }
                memcpy(parsed_baked_colors[instance], bytes + playback_offset, count * 3u);
                playback_offset += count * 3u;
            }
        }
        if (!valid)
        {
            free_baked_colors(parsed_baked_colors, parsed_baked_counts, instance_count);
            free(parsed_draws);free(parsed_interactions);free(instances);free(materials);free(colliders);free(transitions);free(save_points);free(persistent_objects);free(ui_elements);return 0;
        }
    }
    R2Ps2LocalLight parsed_local_lights[3];
    memset(parsed_local_lights, 0, sizeof(parsed_local_lights));
    u32 parsed_local_light_count = 0u;
    if (version >= 48)
    {
        int valid = playback_offset <= package_size && package_size - playback_offset >= 4u;
        if (valid)
        {
            parsed_local_light_count = read_u32_le(bytes + playback_offset);
            playback_offset += 4u;
        }
        const u32 local_light_record_size = version >= 49 ? sizeof(R2Ps2LocalLight) : 52u;
        valid = valid && parsed_local_light_count <= 3u &&
            parsed_local_light_count * local_light_record_size <= package_size - playback_offset;
        for (u32 index = 0; valid && index < parsed_local_light_count; ++index)
        {
            R2Ps2LocalLight *light = &parsed_local_lights[index];
            light->layer_mask = 0xffffffffu;
            memcpy(light, bytes + playback_offset, local_light_record_size);
            playback_offset += local_light_record_size;
            valid = light->type <= 1u && isfinite(light->intensity) && light->intensity >= 0.0f &&
                isfinite(light->range) && light->range > 0.0f &&
                isfinite(light->spot_cos) && light->spot_cos >= -1.0f && light->spot_cos <= 1.0f;
            for (u32 component = 0; valid && component < 3u; ++component)
                valid = isfinite(light->position[component]) && isfinite(light->direction[component]) &&
                    isfinite(light->color[component]);
        }
        if (!valid)
        {
            free_baked_colors(parsed_baked_colors, parsed_baked_counts, instance_count);
            free(parsed_draws);free(parsed_interactions);free(instances);free(materials);free(colliders);free(transitions);free(save_points);free(persistent_objects);free(ui_elements);return 0;
        }
    }
    u32 parsed_directional_layer_mask = 0xffffffffu;
    u8 *parsed_instance_layers = NULL;
    if (version >= 49)
    {
        int valid = playback_offset <= package_size &&
            package_size - playback_offset >= 4u + instance_count * 4u;
        if (valid)
        {
            parsed_directional_layer_mask = read_u32_le(bytes + playback_offset);
            playback_offset += 4u;
            if (instance_count) parsed_instance_layers = malloc(instance_count);
            if (instance_count && parsed_instance_layers == NULL) valid = 0;
        }
        for (u32 index = 0; valid && index < instance_count; ++index)
        {
            u32 layer = read_u32_le(bytes + playback_offset); playback_offset += 4u;
            if (layer > 31u) valid = 0;
            else parsed_instance_layers[index] = (u8)layer;
        }
        if (!valid)
        {
            free(parsed_instance_layers);
            free_baked_colors(parsed_baked_colors, parsed_baked_counts, instance_count);
            free(parsed_draws);free(parsed_interactions);free(instances);free(materials);free(colliders);free(transitions);free(save_points);free(persistent_objects);free(ui_elements);return 0;
        }
    }
    const u32 playback_tail_stride = version >= 43 ? 92u : version >= 30 ? 68u : version >= 29 ? 32u : 8u;
    if (version >= 24 && (instance_count > 4096u || playback_offset > package_size || package_size - playback_offset != instance_count * playback_tail_stride))
    { free(parsed_draws); free(parsed_interactions); free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
    if (version >= 29)
        for (u32 i=0; i<instance_count; ++i)
        {
            R2ScriptMotion motion;
            if (!r2_script_motion_read(&motion, bytes + playback_offset + instance_count * 8u + i * 24u))
            { free(parsed_interactions); free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
            if (version >= 43 && !r2_script_motion_read(&motion, bytes + playback_offset + instance_count * 68u + i * 24u))
            { free(parsed_interactions); free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
        }
    R2ScriptTimer *parsed_timers = NULL;
    if (version >= 30 && instance_count)
    {
        parsed_timers = calloc(instance_count, sizeof(R2ScriptTimer));
        int valid = parsed_timers != NULL;
        for (u32 i=0; valid && i<instance_count; ++i)
        {
            valid = r2_script_condition_read(&parsed_timers[i], bytes + playback_offset + instance_count * 32u + i * 36u,
                version >= 42 ? 12 : version >= 41 ? 11 : version >= 40 ? 10 : version >= 39 ? 9 : version >= 38 ? 8 : version >= 37 ? 7 : version >= 36 ? 6 : version >= 35 ? 5 : version >= 34 ? 4 : version >= 33 ? 3 : version >= 32 ? 2 : version >= 31 ? 1 : 0);
            if (valid && r2_script_is_trigger(parsed_timers[i].operation))
                for (u32 axis=0; axis<3; ++axis)
                    if (transitions[i].center[axis] != 0 || !isfinite(transitions[i].size[axis]) || transitions[i].size[axis] <= 0)
                        valid = 0;
        }
        if (!valid)
        { free(parsed_timers); free(parsed_interactions); free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
    }
    for (u32 i=0; i<4096u; i++) free(platform->playback[i].parameters);
    memset(platform->playback, 0, sizeof(platform->playback));
    platform->script_motion_count = 0;
    memcpy(platform->animator_zones,parsed_zones,sizeof(parsed_zones));
    platform->animator_zone_count=zone_count;
    memset(platform->interactables,0,sizeof(platform->interactables));
    if(interaction_count) memcpy(platform->interactables,parsed_interactions,interaction_count*sizeof(R2Interactable));
    free(parsed_interactions);
    platform->interactable_count=interaction_count; platform->interaction_focus=-1; platform->interaction_canvas=-1;
    for (u32 i = 0; i < instance_count; ++i)
    {
        R2Ps2Playback *p = &platform->playback[i];
        p->flags = version >= 24 ? read_u32_le(bytes + playback_offset + i * 8u) : 7u;
        p->state = version >= 24 ? read_u32_le(bytes + playback_offset + i * 8u + 4u) : 0u;
        if (version >= 29)
        {
            r2_script_motion_read(&p->script_motion, bytes + playback_offset + instance_count * 8u + i * 24u);
            const R2ScriptMotion *m = &p->script_motion;
            if (m->position[0] || m->position[1] || m->position[2] ||
                m->rotation[0] || m->rotation[1] || m->rotation[2]) ++platform->script_motion_count;
        }
        p->previous = p->state;
        if (parsed_timers && parsed_timers[i].operation) ++platform->script_motion_count;
        p->blend = 1.0f;
        if ((p->flags & ~7u) || p->state >= R2_PS2_MAX_ANIMATION_STATES)
        { free(parsed_timers); free(instances); free(materials); free(colliders); free(transitions); free(save_points); free(persistent_objects); free(ui_elements); return 0; }
        if (p->flags & 2u) p->flags |= 8u; /* has started: keep final pose after stopping */
    }
    free(platform->scene_instances);
    free(platform->scene_materials);
    free(platform->scene_draws);
    free_baked_colors(platform->scene_baked_colors,
        platform->scene_baked_color_counts, platform->scene_instance_count);
    free(platform->scene_colliders);
    free(platform->scene_transitions);
    free(platform->scene_save_points);
    free(platform->scene_persistent_objects);
    free(platform->scene_ui_elements);
    platform->scene_instances = instances;
    platform->door_count=door_count;platform->door_focus=-1;
    if(door_count)memcpy(platform->doors,parsed_doors,door_count*sizeof(R2SlidingDoor));
    free(platform->script_timers);
    platform->script_timers = parsed_timers;
    memset(&platform->script_input_edges, 0, sizeof(platform->script_input_edges));
    platform->scene_materials = materials;
    platform->scene_draws = parsed_draws;
    platform->scene_draw_count = draw_count;
    platform->scene_baked_colors = parsed_baked_colors;
    platform->scene_baked_color_counts = parsed_baked_counts;
    memcpy(platform->scene_local_lights, parsed_local_lights, sizeof(parsed_local_lights));
    platform->scene_local_light_count = parsed_local_light_count;
    platform->scene_directional_layer_mask = parsed_directional_layer_mask;
    memset(platform->scene_instance_layers, 0, sizeof(platform->scene_instance_layers));
    if (parsed_instance_layers != NULL)
        memcpy(platform->scene_instance_layers, parsed_instance_layers, instance_count);
    free(parsed_instance_layers);
    platform->scene_colliders = colliders;
    platform->scene_transitions = transitions;
    platform->scene_save_points = save_points;
    platform->scene_persistent_objects = persistent_objects;
    platform->scene_ui_elements = ui_elements;
    platform->scene_ui_count = ui_count;
    platform->scene_ui_reference[0] = ui_reference[0];
    platform->scene_ui_reference[1] = ui_reference[1];
    platform->scene_ui_focus = -1;
    platform->scene_ui_axis_latched = 0;
    platform->scene_ui_activation_frames = 0;
    platform->activated_ui_button[0] = '\0';
    platform->scene_ui_visible = ui_visible;
    platform->scene_ui_pause_gameplay = ui_pause_gameplay;
    platform->scene_ui_loading = ui_loading;
    platform->menu_depth = 0;
    platform->scene_ui_toggle_input = ui_toggle_input & ~ui_loading;
    platform->scene_ui_cancel_input = ui_cancel_input & ~ui_loading;
    platform->scene_controls = controls;
    platform->startup_control_frames = 0;
    memset(platform->camera_correction, 0, sizeof(platform->camera_correction));
    platform->camera_retraction = 0;
    platform->scene_audio = audio;
    platform->scene_instance_count = instance_count;
    platform->scene_mesh_count = mesh_count;
    platform->scene_texture_count = texture_count;
    platform->scene_time = 0.0f;
    platform->jump_time = -1.0f;
    platform->player_idle_time = 0.0f;
    for (u32 i = 0; parsed_timers && i < instance_count; ++i)
        r2_script_start_apply(&parsed_timers[i], instances[i].position, instances[i].rotation);
    if (version >= 43)
        for (u32 i = 0; i < instance_count; ++i)
        {
            R2ScriptMotion startup;
            r2_script_motion_read(&startup, bytes + playback_offset + instance_count * 68u + i * 24u);
            r2_script_motion_tick(&startup, instances[i].position, instances[i].rotation, 1.0f);
        }
    platform->transition_latched = 0;
    platform->transition_prompt_mask = 0u;
    platform->autosave_latched = 0;
    printf("Loaded R2SC v%u scene: %u mesh instance(s)\n", version, instance_count);
    return 1;
}

static void clear_scene_resources(R2Ps2Platform *platform)
{
    if (platform->audio_file != NULL)
    {
        fclose(platform->audio_file);
        platform->audio_file = NULL;
    }
    platform->audio_remaining = 0u;
    if (platform->audio_ready) audsrv_stop_audio();
    for (u32 index = 0; index < R2_PS2_MAX_SCENE_MESHES; ++index)
    {
        free(platform->scene_meshes[index].vertices);
        free(platform->scene_meshes[index].indices);
        free(platform->scene_meshes[index].projected);
        free(platform->scene_meshes[index].animation_frames);
        free(platform->scene_meshes[index].animation_skin);
        free(platform->scene_meshes[index].controller);
        free(platform->scene_meshes[index].animated_vertices);
        free(platform->scene_meshes[index].meshlet_blob);
        free(platform->scene_meshes[index].meshlets);
        memset(&platform->scene_meshes[index], 0, sizeof(R2Ps2MeshResource));
    }
    for (u32 index = 0; index < R2_PS2_MAX_SCENE_TEXTURES; ++index)
    {
        free(platform->scene_textures[index].texture.Mem);
        free(platform->scene_textures[index].texture.Clut);
        memset(&platform->scene_textures[index], 0, sizeof(R2Ps2TextureResource));
    }
    /* CPU texture ownership and GS VRAM allocation are separate. Reclaim the
       user texture region on a scene swap; otherwise every transition keeps
       advancing CurrentPointer until later textures receive invalid/overlapping
       VRAM addresses. TexturePointer begins after gsKit's frame/depth buffers. */
    if (platform->gs != NULL)
        platform->gs->CurrentPointer = platform->gs->TexturePointer;
}

static int start_scene_audio(R2Ps2Platform *platform, const char *path)
{
    if (!platform->audio_ready || !platform->scene_audio.enabled ||
        platform->scene_audio.clip_path[0] == '\0') return 1;
    FILE *file = open_asset(path);
    if (file == NULL) { printf("Audio clip not found: %s\n", path); return 0; }
    u8 header[12];
    if (fread(header, 1, sizeof(header), file) != sizeof(header) ||
        memcmp(header, "RIFF", 4) != 0 || memcmp(header + 8, "WAVE", 4) != 0)
    { fclose(file); printf("Unsupported audio container: %s\n", path); return 0; }
    int format_found = 0, data_found = 0;
    u16 encoding = 0, channels = 0, bits = 0;
    u32 frequency = 0, data_offset = 0, data_length = 0;
    while (!data_found)
    {
        u8 chunk[8];
        if (fread(chunk, 1, sizeof(chunk), file) != sizeof(chunk)) break;
        const u32 size = read_u32_le(chunk + 4);
        if (memcmp(chunk, "fmt ", 4) == 0 && size >= 16u)
        {
            u8 format[16];
            if (fread(format, 1, sizeof(format), file) != sizeof(format)) break;
            encoding = read_u16_le(format); channels = read_u16_le(format + 2);
            frequency = read_u32_le(format + 4); bits = read_u16_le(format + 14);
            if (size > 16u) fseek(file, (long)(size - 16u), SEEK_CUR);
            format_found = 1;
        }
        else if (memcmp(chunk, "data", 4) == 0)
        {
            data_offset = (u32)ftell(file); data_length = size; data_found = 1;
        }
        else fseek(file, (long)size, SEEK_CUR);
        if ((size & 1u) != 0u && !data_found) fseek(file, 1, SEEK_CUR);
    }
    if (!format_found || !data_found || encoding != 1u ||
        (channels != 1u && channels != 2u) || (bits != 8u && bits != 16u))
    { fclose(file); printf("PS2 audio requires PCM WAV (8/16-bit mono/stereo): %s\n", path); return 0; }
    audsrv_fmt_t format;
    format.freq = (int)((float)frequency * platform->scene_audio.pitch);
    /* audsrv's 16-bit path is reliable across real consoles and emulators.
       Keep 8-bit assets compact on disc, then expand their streamed chunks
       before submitting them to the driver. */
    format.bits = 16; format.channels = channels;
    audsrv_stop_audio();
    if (audsrv_set_format(&format) != 0)
    { fclose(file); printf("PS2 audio format rejected: %s\n", audsrv_get_error_string()); return 0; }
    platform->audio_volume = -1;
    platform->audio_file = file;
    platform->audio_data_offset = data_offset;
    platform->audio_data_length = data_length;
    platform->audio_remaining = data_length;
    platform->audio_source_bits = bits;
    printf("Streaming PS2 audio: %s (%u Hz, %u-bit, %u channel(s))\n",
        path, frequency, bits, channels);
    return 1;
}

static void load_ui_sounds(R2Ps2Platform *platform, const char *scene_path)
{
    if (platform == NULL || !platform->audio_ready) return;
    audsrv_adpcm_init();
    memset(platform->ui_sounds, 0, sizeof(platform->ui_sounds));
    memset(platform->ui_sound_counts, 0, sizeof(platform->ui_sound_counts));
    for (u32 kind = 0; kind < 5u; ++kind) platform->ui_sound_last[kind] = -1;
    const char *slash = strrchr(scene_path, '/');
    const int prefix_length = slash == NULL ? 0 : (int)(slash - scene_path + 1);
    for (u32 kind = 0; kind < 5u; ++kind)
    {
        if (platform->scene_audio.ui_sound_paths[kind][0] == '\0') continue;
        for (u32 variant = 0; variant < 16u; ++variant)
        {
            char resource_path[640];
            snprintf(resource_path, sizeof(resource_path), "%.*s%s.ui%u.adp",
                prefix_length, scene_path,
                platform->scene_audio.ui_sound_paths[kind], variant);
            u32 size = 0;
            void *data = read_asset_file(resource_path, 2u * 1024u * 1024u, &size);
            if (data == NULL) break;
            audsrv_adpcm_t *sample = &platform->ui_sounds[kind][variant];
            const int loaded = audsrv_load_adpcm(sample, data, (int)size) == 0;
            free(data);
            if (!loaded) break;
            platform->ui_sound_counts[kind]++;
        }
        printf("UI sound set %u: %u variation(s)\n", kind,
            platform->ui_sound_counts[kind]);
    }
    if (platform->scene_audio.ui_open_on_scene_start && platform->scene_ui_visible)
        play_ui_sound(platform, 4u);
}

int r2_ps2_load_scene_file(R2Ps2Platform *platform, const char *path)
{
    trace_scene_load("scene begin", path, 1);
    /* Present while the outgoing scene's textures still exist. Both buffers
       receive the screen: sync_flip presents the previous submitted buffer.
       The GS framebuffer then stays untouched throughout blocking disc I/O. */
    u32 old_visible = platform->scene_ui_visible;
    if (platform->scene_ui_loading)
    {
        platform->scene_ui_visible = platform->scene_ui_loading;
        platform->scene_ui_focus = -1;
        for (int frame = 0; frame < 2; ++frame)
        {
            r2_ps2_begin_frame(platform);
            r2_ps2_draw_ui(platform);
            r2_ps2_end_frame(platform);
        }
        gsKit_finish();
        gsKit_sync_flip(platform->gs);
    }
    u32 length = 0;
    void *data = read_asset_file(path, 1024u * 1024u, &length);
    if (data == NULL)
    {
        trace_scene_load("scene file read", path, 0);
        platform->scene_ui_visible = old_visible;
        return 0;
    }
    trace_scene_load("scene file read", path, 1);
    clear_scene_resources(platform);
    int loaded = r2_ps2_load_scene_package(platform, data, length);
    trace_scene_load("scene package", path, loaded);
    free(data);
    if (loaded)
    {
        const char *slash = strrchr(path, '/');
        const int prefix_length = slash == NULL ? 0 : (int)(slash - path + 1);
        char resource_path[512];
        for (u32 index = 0; loaded && index < platform->scene_mesh_count; ++index)
        {
            snprintf(resource_path, sizeof(resource_path), "%.*s%s", prefix_length,
                path, platform->scene_mesh_paths[index]);
            loaded = r2_ps2_load_mesh_file(platform, resource_path);
            trace_scene_load("mesh", resource_path, loaded);
            if (loaded)
            {
                R2Ps2MeshResource *resource = &platform->scene_meshes[index];
                resource->vertices = platform->mesh_vertices;
                resource->indices = platform->mesh_indices;
                resource->projected = platform->mesh_projected;
                resource->meshlet_blob = platform->meshlet_blob;
                resource->meshlets = platform->meshlets;
                resource->meshlet_count = platform->meshlet_count;
                resource->vertex_count = platform->mesh_vertex_count;
                resource->index_count = platform->mesh_index_count;
                resource->ready = 1;
                platform->mesh_vertices = NULL;
                platform->mesh_indices = NULL;
                platform->mesh_projected = NULL;
                platform->meshlet_blob = NULL;
                platform->meshlets = NULL;
                platform->meshlet_count = 0u;
                platform->mesh_vertex_count = platform->mesh_index_count = 0;
                char animation_path[512];
                snprintf(animation_path, sizeof(animation_path), "%s", resource_path);
                char *extension = strrchr(animation_path, '.');
                if (extension != NULL)
                {
                    snprintf(extension, (size_t)(animation_path + sizeof(animation_path) - extension), ".r2anim");
                    const int animation_loaded = load_animation_file(resource, animation_path);
                    /* An animation sidecar is optional. Log its result without making
                       an otherwise valid static mesh prevent the scene from loading. */
                    trace_scene_load("animation (optional)", animation_path, animation_loaded);
                }
            }
        }
        for (u32 index = 0; loaded && index < platform->scene_texture_count; ++index)
        {
            snprintf(resource_path, sizeof(resource_path), "%.*s%s", prefix_length,
                path, platform->scene_texture_paths[index]);
            loaded = r2_ps2_load_texture_file(platform, resource_path);
            trace_scene_load("texture", resource_path, loaded);
            if (loaded)
            {
                platform->scene_textures[index].texture = platform->texture;
                platform->scene_textures[index].ready = 1;
                memset(&platform->texture, 0, sizeof(platform->texture));
                platform->texture_ready = 0;
            }
        }
        if (loaded && platform->scene_audio.enabled)
        {
            snprintf(resource_path, sizeof(resource_path), "%.*s%s", prefix_length,
                path, platform->scene_audio.clip_path);
            /* Audio is optional scene content. A missing, unsupported, or
               temporarily unavailable clip must never leave the player stuck
               on the loading screen or discard an otherwise valid scene. */
            const int audio_loaded = start_scene_audio(platform, resource_path);
            trace_scene_load("audio (optional)", resource_path, audio_loaded);
        }
        if (loaded) load_ui_sounds(platform, path);
    }
    platform->external_scene_loaded = loaded;
    if (loaded) snprintf(platform->current_scene_path,
        sizeof(platform->current_scene_path), "%s", path);
    trace_scene_load("scene complete", path, loaded);
    return loaded;
}

void r2_ps2_update_audio(R2Ps2Platform *platform)
{
    if (platform == NULL || platform->audio_file == NULL || !platform->audio_ready) return;
    float gain = platform->scene_audio.volume;
    if (platform->scene_audio.spatial)
    {
        const float *listener = platform->scene_camera.position;
        float attached_listener[3];
        if (platform->scene_audio.listener_object_index >= 0 &&
            (u32)platform->scene_audio.listener_object_index < platform->scene_instance_count)
        {
            const float *anchor = platform->scene_instances[
                platform->scene_audio.listener_object_index].position;
            attached_listener[0] = anchor[0] + platform->scene_audio.listener_offset[0];
            attached_listener[1] = anchor[1] + platform->scene_audio.listener_offset[1];
            attached_listener[2] = anchor[2] + platform->scene_audio.listener_offset[2];
            listener = attached_listener;
        }
        const float dx = platform->scene_audio.position[0] - listener[0];
        const float dy = platform->scene_audio.position[1] - listener[1];
        const float dz = platform->scene_audio.position[2] - listener[2];
        const float distance = sqrtf(dx * dx + dy * dy + dz * dz);
        const float minimum = platform->scene_audio.min_distance;
        const float maximum = platform->scene_audio.max_distance > minimum
            ? platform->scene_audio.max_distance : minimum + 0.01f;
        float attenuation = 1.0f - (distance - minimum) / (maximum - minimum);
        if (attenuation < 0.0f) attenuation = 0.0f;
        if (attenuation > 1.0f) attenuation = 1.0f;
        gain *= attenuation;
    }
    int volume = (int)(gain * (float)MAX_VOLUME + 0.5f);
    if (volume != platform->audio_volume)
    {
        audsrv_set_volume(volume);
        platform->audio_volume = volume;
    }
    int available = audsrv_available();
    while (available > 0)
    {
        if (platform->audio_remaining == 0u)
        {
            if (!platform->scene_audio.loop) { fclose(platform->audio_file); platform->audio_file = NULL; return; }
            fseek(platform->audio_file, (long)platform->audio_data_offset, SEEK_SET);
            platform->audio_remaining = platform->audio_data_length;
        }
        u8 source_chunk[4096];
        u8 output_chunk[4096];
        u32 amount = platform->audio_remaining;
        const int expands_8_bit = platform->audio_source_bits == 8u;
        const u32 source_capacity = expands_8_bit
            ? (u32)(sizeof(output_chunk) / 2u)
            : (u32)sizeof(output_chunk);
        const u32 available_source = expands_8_bit
            ? (u32)(available / 2)
            : (u32)available;
        if (amount > source_capacity) amount = source_capacity;
        if (amount > available_source) amount = available_source;
        if (amount == 0u) break;
        const size_t read = fread(source_chunk, 1, amount, platform->audio_file);
        if (read == 0u) { platform->audio_remaining = 0u; continue; }
        size_t output_size = read;
        if (expands_8_bit)
        {
            output_size = read * 2u;
            for (size_t index = 0; index < read; ++index)
            {
                const s16 sample = (s16)(((s32)source_chunk[index] - 128) << 8);
                output_chunk[index * 2u] = (u8)(sample & 0xff);
                output_chunk[index * 2u + 1u] = (u8)((sample >> 8) & 0xff);
            }
        }
        else memcpy(output_chunk, source_chunk, read);
        if (audsrv_play_audio((char *)output_chunk, (int)output_size) < 0) break;
        platform->audio_remaining -= (u32)read;
        available -= (int)output_size;
    }
}

typedef struct R2Ps2SaveHeader
{
    char magic[4];
    u32 version;
    u32 payload_size;
    u32 checksum;
} R2Ps2SaveHeader;

static u32 save_checksum(const void *data, u32 size)
{
    const u8 *bytes = data;
    u32 hash = 2166136261u;
    for (u32 index = 0; index < size; ++index)
    { hash ^= bytes[index]; hash *= 16777619u; }
    return hash;
}

static int mc_wait_result(void)
{
    int result = -1;
    return mcSync(MC_WAIT, NULL, &result) < 0 ? -1 : result;
}

static int save_slot_path(const char *folder, const char *slot, const char *suffix, char *path, size_t capacity)
{
    if (slot == NULL || slot[0] == '\0') return 0;
    char clean[17];
    size_t length = 0;
    while (slot[length] != '\0' && length < sizeof(clean) - 1u)
    {
        const char value = slot[length];
        if (!((value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z') ||
              (value >= '0' && value <= '9') || value == '_' || value == '-')) return 0;
        clean[length++] = value;
    }
    clean[length] = '\0';
    if (slot[length] != '\0') return 0;
    int count=snprintf(path, capacity, "%s/%s%s", folder, clean, suffix);
    return count>=0 && (size_t)count<capacity;
}

static int load_save_identity(char folder[32],int *legacy)
{
    u32 size=0;unsigned char *data=read_asset_file("host:assets/Save/identity.bin",32,&size);
    int okay=r2_save_identity(data,size,folder,legacy);free(data);
    if(!okay)printf("Save unavailable: missing or invalid project Save ID. Rebuild the game.\n");
    return okay;
}

static int mc_open_sync(const char *path, int flags)
{
    if (mcOpen(0, 0, path, flags) < 0) return -1;
    return mc_wait_result();
}

static int mc_close_sync(int descriptor)
{
    if (mcClose(descriptor) < 0) return 0;
    return mc_wait_result() >= 0;
}

static int mc_write_sync(int descriptor, const void *data, u32 size)
{
    if (mcWrite(descriptor, data, (int)size) < 0) return 0;
    return mc_wait_result() == (int)size;
}

static int write_save_file(const char *path, const void *envelope, u32 envelope_size)
{
    const int descriptor = mc_open_sync(path, O_WRONLY | O_CREAT | O_TRUNC);
    if (descriptor < 0) return 0;
    int okay = mc_write_sync(descriptor, envelope, envelope_size);
    if (okay) okay = mcFlush(descriptor) >= 0 && mc_wait_result() >= 0;
    if (!mc_close_sync(descriptor)) okay = 0;
    return okay;
}

/* Presentation files are independent of .SAV/.TMP payloads. Compare first to
   avoid rewriting the icon on every checkpoint. Never remove existing saves. */
static int save_presentation_file(const char *path,const void *data,u32 size)
{
    char device_path[80];snprintf(device_path,sizeof(device_path),"mc0:%s",path);
    int fd=open(device_path,O_RDONLY),same=0;
    if(fd>=0){
        unsigned char chunk[256];u32 offset=0;same=1;
        while(offset<size){
            u32 amount=size-offset;if(amount>sizeof(chunk))amount=sizeof(chunk);
            int received=read(fd,chunk,amount);
            if(received!=(int)amount || memcmp(chunk,(const unsigned char *)data+offset,amount)!=0){same=0;break;}
            offset+=amount;
        }
        if(same && read(fd,chunk,1)!=0)same=0;
        close(fd);
    }
    return same || write_save_file(path,data,size);
}

static int ensure_save_presentation(const char *folder)
{
    u32 system_size=0,icon_size=0;
    unsigned char *system=read_asset_file("host:assets/Save/icon.sys",964u,&system_size);
    unsigned char *icon=read_asset_file("host:assets/Save/save.icn",256u*1024u,&icon_size);
    int okay=system && icon && system_size==964u && icon_size>=20u &&
        memcmp(system,"PS2D",4)==0 && icon[0]==0 && icon[1]==0 && icon[2]==1 && icon[3]==0 &&
        memcmp(system+260,"save.icn\0",9)==0 && memcmp(system+324,"save.icn\0",9)==0 &&
        memcmp(system+388,"save.icn\0",9)==0;
    /* Install the referenced model before publishing its browser metadata. */
    char path[64];
    snprintf(path,sizeof(path),"%s/save.icn",folder);
    if(okay)okay=save_presentation_file(path,icon,icon_size);
    snprintf(path,sizeof(path),"%s/icon.sys",folder);
    if(okay)okay=save_presentation_file(path,system,system_size);
    free(system);free(icon);
    if(!okay)printf("Save failed: memory-card browser metadata could not be installed.\n");
    return okay;
}

int r2_ps2_save_blob(R2Ps2Platform *platform, const char *slot,
    const void *data, unsigned int size)
{
    if (platform == NULL || !platform->memory_card_ready || data == NULL ||
        size == 0u || size > 1024u * 1024u) return 0;
    int type = 0, free_space = 0, formatted = 0;
    if (mcGetInfo(0, 0, &type, &free_space, &formatted) < 0 || mc_wait_result() < -1 ||
        type != MC_TYPE_PS2) return 0;
    char folder[32];int legacy=0;
    if(!load_save_identity(folder,&legacy))return 0;
    if (mcMkDir(0, 0, folder) >= 0) mc_wait_result();
    char target[64], temporary[64];
    if (!save_slot_path(folder, slot, ".SAV", target, sizeof(target)) ||
        !save_slot_path(folder, slot, ".TMP", temporary, sizeof(temporary))) return 0;
    if(!ensure_save_presentation(folder))return 0;
    if (mcDelete(0, 0, temporary) >= 0) mc_wait_result();
    R2Ps2SaveHeader header = {{'R','2','S','V'}, 1u, size, save_checksum(data, size)};
    const u32 envelope_size = (u32)sizeof(header) + size;
    unsigned char *envelope = (unsigned char *)memalign(64, envelope_size);
    if (envelope == NULL) return 0;
    memcpy(envelope, &header, sizeof(header));
    memcpy(envelope + sizeof(header), data, size);
    int okay = write_save_file(temporary, envelope, envelope_size);
    if (!okay) {
        free(envelope);
        if (mcDelete(0, 0, temporary) >= 0) mc_wait_result();
        return 0;
    }
    /* The legacy ROM MCSERV used alongside PADMAN does not implement rename.
       Keep the fully flushed TMP as a recovery copy, then commit the same
       validated envelope to the public slot. */
    if (mcDelete(0, 0, target) >= 0) mc_wait_result();
    okay = write_save_file(target, envelope, envelope_size);
    free(envelope);
    return okay;
}

static int load_save_path(const char *path, void *data, u32 capacity, u32 *size,
    int *error)
{
    char device_path[80];
    snprintf(device_path, sizeof(device_path), "mc0:%s", path);
    const int descriptor = open(device_path, O_RDONLY);
    if (descriptor < 0) { if (error != NULL) *error = errno==ENOENT ? 1 : 9; return 0; }
    const u32 envelope_capacity = (u32)sizeof(R2Ps2SaveHeader) + capacity;
    unsigned char *envelope = (unsigned char *)memalign(64, envelope_capacity);
    if (envelope == NULL) {
        if (error != NULL) *error = 2;
        close(descriptor);
        return 0;
    }
    const int bytes_read = (int)read(descriptor, envelope, envelope_capacity);
    close(descriptor);
    R2Ps2SaveHeader *header = (R2Ps2SaveHeader *)envelope;
    int okay = bytes_read >= (int)sizeof(*header);
    if (!okay && error != NULL) *error = 3;
    if (okay && (memcmp(header->magic, "R2SV", 4) != 0 || header->version != 1u)) {
        okay = 0;
        if (error != NULL) *error = 4;
    }
    if (okay && (header->payload_size == 0u || header->payload_size > capacity)) {
        okay = 0;
        if (error != NULL) *error = 5;
    }
    if (okay && bytes_read < (int)(sizeof(*header) + header->payload_size)) {
        okay = 0;
        if (error != NULL) *error = 7;
    }
    const unsigned char *payload = envelope + sizeof(*header);
    if (okay && save_checksum(payload, header->payload_size) != header->checksum) {
        okay = 0;
        if (error != NULL) *error = 8;
    }
    if (okay) {
        memcpy(data, payload, header->payload_size);
        if (size != NULL) *size = header->payload_size;
    }
    free(envelope);
    if (okay && error != NULL) *error = 0;
    return okay;
}

int r2_ps2_load_blob(R2Ps2Platform *platform, const char *slot,
    void *data, unsigned int capacity, unsigned int *size)
{
    if (platform == NULL || !platform->memory_card_ready || data == NULL || capacity == 0u)
        return 0;
    char target[64], temporary[64];
    char folder[32];int legacy=0;
    if(!load_save_identity(folder,&legacy)){platform->load_error=9;return 0;}
    if (!save_slot_path(folder, slot, ".SAV", target, sizeof(target)) ||
        !save_slot_path(folder, slot, ".TMP", temporary, sizeof(temporary))) return 0;
    platform->load_error = 0;
    if (load_save_path(target, data, capacity, size, &platform->load_error)) return 1;
    int primary_error=platform->load_error;
    if(load_save_path(temporary, data, capacity, size, &platform->load_error))return 1;
    /* Never replace a corrupt/newer project save with unrelated stale data.
       Legacy import is read-only and only eligible when BOTH new files are absent. */
    if(!r2_save_legacy_allowed(legacy,primary_error,platform->load_error))return 0;
    if(!save_slot_path("/R2ENGINE",slot,".SAV",target,sizeof(target)) ||
        !save_slot_path("/R2ENGINE",slot,".TMP",temporary,sizeof(temporary)))return 0;
    if(load_save_path(target,data,capacity,size,&platform->load_error))return 1;
    return load_save_path(temporary,data,capacity,size,&platform->load_error);
}

typedef struct R2Ps2Checkpoint
{
    char scene_path[256];
    float position[3];
    float rotation[3];
    float camera_position[3];
    float camera_rotation[3];
    u32 persistent_object_count;
    struct
    {
        char save_id[32];
        float position[3];
        float rotation[3];
        float scale[3];
        u32 active;
        s32 integer_value;
        u32 boolean_value;
    } persistent_objects[R2_PS2_MAX_PERSISTENT_OBJECTS];
    u32 door_count;
    R2DoorCheckpoint doors[64];
} R2Ps2Checkpoint;
#define R2_PS2_CHECKPOINT_V1_SIZE ((u32)offsetof(R2Ps2Checkpoint,door_count))

static R2Ps2SceneInstance *checkpoint_character(R2Ps2Platform *platform)
{
    for (u32 object = 0; object < platform->scene_instance_count; ++object)
        if ((platform->playback[object].flags & 4u) && platform->scene_instances[object].mesh_slot < platform->scene_mesh_count &&
            platform->scene_meshes[platform->scene_instances[object].mesh_slot].animation_state_count > 0u)
            return &platform->scene_instances[object];
    return NULL;
}

static R2Ps2ScenePersistentObject *find_persistent_object(
    R2Ps2Platform *platform, const char *save_id)
{
    if (platform == NULL || platform->scene_persistent_objects == NULL ||
        save_id == NULL || save_id[0] == '\0') return NULL;
    for (u32 object = 0; object < platform->scene_instance_count; ++object)
    {
        R2Ps2ScenePersistentObject *persistent = &platform->scene_persistent_objects[object];
        if (persistent->enabled && strcmp(persistent->save_id, save_id) == 0)
            return persistent;
    }
    return NULL;
}

int r2_ps2_set_persistent_active(R2Ps2Platform *platform, const char *save_id, int active)
{
    R2Ps2ScenePersistentObject *persistent = find_persistent_object(platform, save_id);
    if (persistent == NULL) return 0;
    persistent->active = active != 0;
    return 1;
}

int r2_ps2_get_persistent_active(R2Ps2Platform *platform, const char *save_id, int *active)
{
    R2Ps2ScenePersistentObject *persistent = find_persistent_object(platform, save_id);
    if (persistent == NULL || active == NULL) return 0;
    *active = persistent->active != 0u;
    return 1;
}

int r2_ps2_set_persistent_integer(R2Ps2Platform *platform, const char *save_id, int value)
{
    R2Ps2ScenePersistentObject *persistent = find_persistent_object(platform, save_id);
    if (persistent == NULL) return 0;
    persistent->integer_value = value;
    return 1;
}

int r2_ps2_get_persistent_integer(R2Ps2Platform *platform, const char *save_id, int *value)
{
    R2Ps2ScenePersistentObject *persistent = find_persistent_object(platform, save_id);
    if (persistent == NULL || value == NULL) return 0;
    *value = persistent->integer_value;
    return 1;
}

int r2_ps2_set_persistent_boolean(R2Ps2Platform *platform, const char *save_id, int value)
{
    R2Ps2ScenePersistentObject *persistent = find_persistent_object(platform, save_id);
    if (persistent == NULL) return 0;
    persistent->boolean_value = value != 0;
    return 1;
}

int r2_ps2_get_persistent_boolean(R2Ps2Platform *platform, const char *save_id, int *value)
{
    R2Ps2ScenePersistentObject *persistent = find_persistent_object(platform, save_id);
    if (persistent == NULL || value == NULL) return 0;
    *value = persistent->boolean_value != 0u;
    return 1;
}

int r2_ps2_save_checkpoint(R2Ps2Platform *platform, const char *slot)
{
    if (platform == NULL) return 0;
    platform->save_status = 2; /* Every early failure must replace a stale success message. */
    if (!platform->external_scene_loaded) return 0;
    R2Ps2SceneInstance *character = checkpoint_character(platform);
    if (character == NULL) return 0;
    R2Ps2Checkpoint checkpoint;
    memset(&checkpoint, 0, sizeof(checkpoint));
    snprintf(checkpoint.scene_path, sizeof(checkpoint.scene_path), "%s",
        platform->current_scene_path);
    memcpy(checkpoint.position, character->position, sizeof(checkpoint.position));
    memcpy(checkpoint.rotation, character->rotation, sizeof(checkpoint.rotation));
    memcpy(checkpoint.camera_position, platform->scene_camera.position,
        sizeof(checkpoint.camera_position));
    for (int axis = 0; axis < 3; axis++) checkpoint.camera_position[axis] += platform->camera_correction[axis];
    memcpy(checkpoint.camera_rotation, platform->scene_camera.rotation,
        sizeof(checkpoint.camera_rotation));
    if (platform->scene_persistent_objects != NULL)
    {
        for (u32 object = 0; object < platform->scene_instance_count &&
            checkpoint.persistent_object_count < R2_PS2_MAX_PERSISTENT_OBJECTS; ++object)
        {
            R2Ps2ScenePersistentObject *persistent = &platform->scene_persistent_objects[object];
            if (!persistent->enabled || persistent->save_id[0] == '\0') continue;
            if(!r2_script_checkpoint_capture(platform->script_timers ? &platform->script_timers[object] : NULL,
                persistent->save_integer,&persistent->integer_value,
                persistent->save_boolean,&persistent->boolean_value))
            { platform->save_status=2;printf("Checkpoint rejected incompatible script persistence: %s.\n",persistent->save_id);return 0; }
            const u32 output = checkpoint.persistent_object_count++;
            snprintf(checkpoint.persistent_objects[output].save_id,
                sizeof(checkpoint.persistent_objects[output].save_id), "%s", persistent->save_id);
            if (persistent->save_transform)
            {
                memcpy(checkpoint.persistent_objects[output].position,
                    platform->scene_instances[object].position, sizeof(float) * 3u);
                memcpy(checkpoint.persistent_objects[output].rotation,
                    platform->scene_instances[object].rotation, sizeof(float) * 3u);
                memcpy(checkpoint.persistent_objects[output].scale,
                    platform->scene_instances[object].scale, sizeof(float) * 3u);
            }
            checkpoint.persistent_objects[output].active = persistent->active;
            checkpoint.persistent_objects[output].integer_value = persistent->integer_value;
            checkpoint.persistent_objects[output].boolean_value = persistent->boolean_value;
        }
    }
    checkpoint.door_count=platform->door_count;
    for(u32 door=0;door<checkpoint.door_count;door++)
        r2_door_capture(&platform->doors[door],&checkpoint.doors[door]);
    const int saved = r2_ps2_save_blob(platform, slot, &checkpoint, sizeof(checkpoint));
    platform->save_status = saved ? 1 : 2;
    if (saved) snprintf(platform->active_checkpoint_slot,
        sizeof(platform->active_checkpoint_slot), "%s", slot);
    printf(saved ? "Memory card checkpoint saved to %s.\n" :
        "Memory card checkpoint save failed for %s.\n", slot);
    return saved;
}

int r2_ps2_load_checkpoint(R2Ps2Platform *platform, const char *slot)
{
    if (platform == NULL) return 0;
    R2Ps2Checkpoint checkpoint; u32 loaded_size = 0u;
    memset(&checkpoint,0,sizeof(checkpoint));
    if (!r2_ps2_load_blob(platform, slot, &checkpoint, sizeof(checkpoint), &loaded_size) ||
        (loaded_size != R2_PS2_CHECKPOINT_V1_SIZE && loaded_size != sizeof(checkpoint)))
    { platform->save_status = 4; printf("Memory card checkpoint load failed.\n"); return 0; }
    checkpoint.scene_path[sizeof(checkpoint.scene_path) - 1u] = '\0';
    if (checkpoint.scene_path[0] == '\0' ||
        (strcmp(checkpoint.scene_path, platform->current_scene_path) != 0 &&
         !r2_ps2_load_scene_file(platform, checkpoint.scene_path)))
    {
        platform->save_status = 4; platform->load_error = 9;
        printf("Checkpoint scene failed to load: %s\n", checkpoint.scene_path);
        return 0;
    }
    R2Ps2SceneInstance *character = checkpoint_character(platform);
    if (character == NULL)
    {
        platform->save_status = 4; platform->load_error = 9;
        printf("Checkpoint scene has no animated character.\n");
        return 0;
    }
    if (checkpoint.persistent_object_count > R2_PS2_MAX_PERSISTENT_OBJECTS)
    {
        platform->save_status = 4; platform->load_error = 10;
        return 0;
    }
    if(loaded_size==sizeof(checkpoint) && checkpoint.door_count>64u)
    {
        platform->save_status=4;platform->load_error=10;return 0;
    }
    if (platform->scene_persistent_objects != NULL)
    {
        for (u32 saved = 0; saved < checkpoint.persistent_object_count; ++saved)
        {
            checkpoint.persistent_objects[saved].save_id[31] = '\0';
            for (u32 object = 0; object < platform->scene_instance_count; ++object)
            {
                R2Ps2ScenePersistentObject *persistent = &platform->scene_persistent_objects[object];
                if (!persistent->enabled || strcmp(persistent->save_id,
                    checkpoint.persistent_objects[saved].save_id) != 0) continue;
                if (persistent->save_transform)
                {
                    memcpy(platform->scene_instances[object].position,
                        checkpoint.persistent_objects[saved].position, sizeof(float) * 3u);
                    memcpy(platform->scene_instances[object].rotation,
                        checkpoint.persistent_objects[saved].rotation, sizeof(float) * 3u);
                    memcpy(platform->scene_instances[object].scale,
                        checkpoint.persistent_objects[saved].scale, sizeof(float) * 3u);
                }
                if (persistent->save_active_state)
                    persistent->active = checkpoint.persistent_objects[saved].active != 0u;
                if (persistent->save_integer)
                    persistent->integer_value = checkpoint.persistent_objects[saved].integer_value;
                if (persistent->save_boolean)
                    persistent->boolean_value = checkpoint.persistent_objects[saved].boolean_value != 0u;
                if(!r2_script_checkpoint_restore(platform->script_timers ? &platform->script_timers[object] : NULL,
                    persistent->save_integer,persistent->integer_value,
                    persistent->save_boolean,persistent->boolean_value))
                { platform->save_status=4;platform->load_error=10;return 0; }
                break;
            }
        }
    }
    if(loaded_size==sizeof(checkpoint))
    {
        for(u32 saved=0;saved<checkpoint.door_count;saved++)
            for(u32 door=0;door<platform->door_count;door++)
                if(r2_door_restore(&platform->doors[door],
                    platform->scene_instances[platform->doors[door].source].position,
                    &checkpoint.doors[saved]))break;
    }
    memcpy(character->position, checkpoint.position, sizeof(checkpoint.position));
    memcpy(character->rotation, checkpoint.rotation, sizeof(checkpoint.rotation));
    memcpy(platform->scene_camera.position, checkpoint.camera_position,
        sizeof(checkpoint.camera_position));
    memcpy(platform->scene_camera.rotation, checkpoint.camera_rotation,
        sizeof(checkpoint.camera_rotation));
    memset(platform->camera_correction, 0, sizeof(platform->camera_correction));
    platform->camera_retraction = 0;
    platform->vertical_velocity = 0.0f;
    platform->save_status = 3;
    snprintf(platform->active_checkpoint_slot,
        sizeof(platform->active_checkpoint_slot), "%s", slot);
    printf("Memory card checkpoint loaded from %s.\n", slot);
    return 1;
}

void r2_ps2_begin_frame(R2Ps2Platform *platform)
{
    platform->queued_world_triangles = 0u;
    r2_vu1_eligible_objects = 0u;
    r2_vu1_clipped_objects = 0u;
    r2_vu1_dispatched_meshlets = 0u;
    r2_vu1_active_texture = NULL;
    r2_vu1_active_fog = -1;
    gsKit_clear(platform->gs, platform->background);
}

void r2_ps2_poll_input(R2Ps2Platform *platform, R2Ps2Input *input)
{
    input->connected = 0;
    input->analog = 0;
    input->buttons = 0;
    input->left_x = 128;
    input->left_y = 128;
    input->right_x = 128;
    input->right_y = 128;
    if (!platform->pad_open)
        return;

    const int state = padGetState(0, 0);
    if (state != PAD_STATE_STABLE && state != PAD_STATE_EXECCMD)
        return;

    /* padPortOpen starts many controllers in digital mode. Request and lock
       DualShock mode once the pad is ready, then retry if an adapter ignores
       the first request. This stays non-blocking so a bad pad cannot hang a frame. */
    if (platform->pad_mode_request == 0 && platform->pad_mode_retry_frames <= 0)
    {
        const int mode_count = padInfoMode(0, 0, PAD_MODETABLE, -1);
        for (int mode = 0; mode < mode_count; ++mode)
        {
            if (padInfoMode(0, 0, PAD_MODETABLE, mode) == PAD_TYPE_DUALSHOCK)
            {
                if (padSetMainMode(0, 0, PAD_MMODE_DUALSHOCK, PAD_MMODE_LOCK) == 1)
                    platform->pad_mode_request = 1;
                break;
            }
        }
        platform->pad_mode_retry_frames = 120;
    }

    if (platform->pad_mode_request == 1 && padGetReqState(0, 0) == PAD_RSTAT_COMPLETE)
        platform->pad_mode_request = 2;

    if (platform->pad_mode_retry_frames > 0)
        --platform->pad_mode_retry_frames;

    struct padButtonStatus status;
    memset(&status, 0, sizeof(status));
    if (padRead(0, 0, &status) == 0)
        return;

    input->connected = 1;
    input->analog = ((status.mode >> 4) == PAD_TYPE_DUALSHOCK);
    input->buttons = (u16)(0xffffu ^ status.btns);
    /* Digital packets have no valid stick fields. Leave the neutral values
       set above until DualShock analog reporting is actually active. */
    if (input->analog)
    {
        input->left_x = status.ljoy_h;
        input->left_y = status.ljoy_v;
        input->right_x = status.rjoy_h;
        input->right_y = status.rjoy_v;
    }

    if (!input->analog && platform->pad_mode_request == 2 && platform->pad_mode_retry_frames <= 0)
        platform->pad_mode_request = 0;
}

static float debug_axis(u8 value)
{
    float axis = ((float)value - 128.0f) / 127.0f;
    if (axis > -0.16f && axis < 0.16f)
        return 0.0f;
    return axis;
}

static float input_axis(const R2Ps2Input *input, s32 axis)
{
    if (input == NULL || !input->connected || !input->analog) return 0.0f;
    switch (axis)
    {
        case 0: return debug_axis(input->left_x);
        case 1: return debug_axis(input->left_y);
        case 2: return debug_axis(input->right_x);
        case 3: return debug_axis(input->right_y);
        default: return 0.0f;
    }
}

static float input_dead_zone(float value, float dead_zone)
{
    const float magnitude = fabsf(value);
    if (magnitude <= dead_zone) return 0.0f;
    const float normalized = (magnitude - dead_zone) / (1.0f - dead_zone);
    return value < 0.0f ? -normalized : normalized;
}

static u16 input_button_mask(s32 button)
{
    /* The editor's controller button indices intentionally keep the tested
       PS controller defaults first: Cross=0 and R1=1. */
    switch (button)
    {
        case 0: return PAD_CROSS;
        case 1: return PAD_R1;
        case 2: return PAD_CIRCLE;
        case 3: return PAD_SQUARE;
        case 4: return PAD_TRIANGLE;
        case 5: return PAD_L1;
        case 6: return PAD_L2;
        case 7: return PAD_R2;
        case 8: return PAD_SELECT;
        case 9: return PAD_START;
        case 10: return PAD_L3;
        case 11: return PAD_R3;
        case 12: return PAD_UP;
        case 13: return PAD_DOWN;
        case 14: return PAD_LEFT;
        case 15: return PAD_RIGHT;
        default: return 0u;
    }
}

void r2_ps2_update_debug_camera(
    R2Ps2Platform *platform,
    const R2Ps2Input *input,
    float delta_seconds)
{
    if (platform == NULL || input == NULL || !input->connected)
        return;

    const float degrees = 0.0174532925f;
    const float move_speed = 4.0f;
    const float look_speed = 100.0f;
    const float strafe = debug_axis(input->left_x);
    const float forward = -debug_axis(input->left_y);
    const float look_x = debug_axis(input->right_x);
    const float look_y = debug_axis(input->right_y);

    platform->scene_camera.rotation[1] += look_x * look_speed * delta_seconds;
    platform->scene_camera.rotation[0] -= look_y * look_speed * delta_seconds;
    if (platform->scene_camera.rotation[0] > 89.0f)
        platform->scene_camera.rotation[0] = 89.0f;
    if (platform->scene_camera.rotation[0] < -89.0f)
        platform->scene_camera.rotation[0] = -89.0f;

    const float yaw = platform->scene_camera.rotation[1] * degrees;
    const float sine = sinf(yaw);
    const float cosine = cosf(yaw);
    const float distance = move_speed * delta_seconds;
    platform->scene_camera.position[0] +=
        (strafe * cosine - forward * sine) * distance;
    platform->scene_camera.position[2] +=
        (-strafe * sine - forward * cosine) * distance;

    if ((input->buttons & PAD_R1) != 0)
        platform->scene_camera.position[1] += distance;
    if ((input->buttons & PAD_L1) != 0)
        platform->scene_camera.position[1] -= distance;
}

static u32 animation_state_for_kind(const R2Ps2MeshResource *mesh, u32 kind)
{
    for (u32 state = 0; state < mesh->animation_state_count; ++state)
        if (mesh->animation_states[state].kind == kind) return state;
    return 0u;
}

static void collider_world_vertex(
    const R2Ps2SceneInstance *instance, const R2Ps2SceneCollider *collider,
    const R2Ps2MeshVertex *vertex,
    float sx, float cx, float sy, float cy, float sz, float cz,
    float *x, float *y, float *z)
{
    float px = (vertex->x + collider->center[0]) * instance->scale[0];
    float py = (vertex->y + collider->center[1]) * instance->scale[1];
    float pz = (vertex->z + collider->center[2]) * instance->scale[2];
    float temporary = cx * py - sx * pz; pz = sx * py + cx * pz; py = temporary;
    temporary = cy * px + sy * pz; pz = -sy * px + cy * pz; px = temporary;
    temporary = cz * px - sz * py; py = sz * px + cz * py; px = temporary;
    *x = px + instance->position[0]; *y = py + instance->position[1]; *z = pz + instance->position[2];
}

static void inverse_rotate_xyz(
    float x, float y, float z,
    float sx, float cx, float sy, float cy, float sz, float cz,
    float *result_x, float *result_y, float *result_z)
{
    /* collider_world_vertex applies X, then Y, then Z. Undo in reverse. */
    float temporary = cz * x + sz * y;
    y = -sz * x + cz * y; x = temporary;
    temporary = cy * x - sy * z;
    z = sy * x + cy * z; x = temporary;
    temporary = cx * y + sx * z;
    z = -sx * y + cx * z; y = temporary;
    *result_x = x; *result_y = y; *result_z = z;
}

static int ray_hits_oriented_box_top(
    const R2Ps2SceneInstance *instance, const R2Ps2SceneCollider *collider,
    float world_x, float world_z, float maximum_y,
    float *hit_y, float *hit_normal_y)
{
    const float degrees = 0.0174532925f;
    const float sx = sinf(instance->rotation[0] * degrees), cx = cosf(instance->rotation[0] * degrees);
    const float sy = sinf(instance->rotation[1] * degrees), cy = cosf(instance->rotation[1] * degrees);
    const float sz = sinf(instance->rotation[2] * degrees), cz = cosf(instance->rotation[2] * degrees);
    float center_x = collider->center[0] * instance->scale[0];
    float center_y = collider->center[1] * instance->scale[1];
    float center_z = collider->center[2] * instance->scale[2];
    float temporary = cx * center_y - sx * center_z;
    center_z = sx * center_y + cx * center_z; center_y = temporary;
    temporary = cy * center_x + sy * center_z;
    center_z = -sy * center_x + cy * center_z; center_x = temporary;
    temporary = cz * center_x - sz * center_y;
    center_y = sz * center_x + cz * center_y; center_x = temporary;
    center_x += instance->position[0]; center_y += instance->position[1]; center_z += instance->position[2];
    float origin_x, origin_y, origin_z, direction_x, direction_y, direction_z;
    inverse_rotate_xyz(world_x - center_x, maximum_y - center_y, world_z - center_z,
        sx, cx, sy, cy, sz, cz, &origin_x, &origin_y, &origin_z);
    inverse_rotate_xyz(0.0f, -1.0f, 0.0f,
        sx, cx, sy, cy, sz, cz, &direction_x, &direction_y, &direction_z);
    const float half[3] = {
        fabsf(collider->radius * instance->scale[0]) * 0.5f,
        fabsf(collider->height * instance->scale[1]) * 0.5f,
        fabsf(collider->slope_limit * instance->scale[2]) * 0.5f };
    const float origin[3] = { origin_x, origin_y, origin_z };
    const float direction[3] = { direction_x, direction_y, direction_z };
    float minimum_t = 0.0f, maximum_t = 1000000.0f;
    int entering_axis = 1;
    for (int axis = 0; axis < 3; ++axis)
    {
        if (fabsf(direction[axis]) < 0.000001f)
        {
            if (origin[axis] < -half[axis] || origin[axis] > half[axis]) return 0;
            continue;
        }
        float first = (-half[axis] - origin[axis]) / direction[axis];
        float second = (half[axis] - origin[axis]) / direction[axis];
        if (first > second) { const float swap = first; first = second; second = swap; }
        if (first > minimum_t) { minimum_t = first; entering_axis = axis; }
        if (second < maximum_t) maximum_t = second;
        if (minimum_t > maximum_t) return 0;
    }
    if (maximum_t < 0.0f) return 0;
    const float distance = minimum_t >= 0.0f ? minimum_t : maximum_t;
    *hit_y = maximum_y - distance;
    /* A local box-face normal transformed into world space has this Y magnitude. */
    if (entering_axis == 0) *hit_normal_y = fabsf(sz * cy);
    else if (entering_axis == 1) *hit_normal_y = fabsf(sz * sy * sx + cz * cx);
    else *hit_normal_y = fabsf(sz * sy * cx - cz * sx);
    return 1;
}

static int colliders_interact(const R2Ps2SceneCollider *a, const R2Ps2SceneCollider *b)
{
    return a != NULL && b != NULL &&
        (a->collision_mask & (1u << b->layer)) != 0u &&
        (b->collision_mask & (1u << a->layer)) != 0u;
}

static int scene_ground_height(
    const R2Ps2Platform *platform, float x, float z, float maximum_y,
    const R2Ps2SceneInstance *excluded_instance,
    float *height, float *normal_y)
{
    int found = 0; float best = -1000000.0f; float best_normal = 1.0f;
    for (u32 object = 0; object < platform->scene_instance_count; ++object)
    {
        if (platform->scene_colliders == NULL) continue;
        const R2Ps2SceneInstance *instance = &platform->scene_instances[object];
        /* A character's own solid collider is not walkable world geometry. */
        if (instance == excluded_instance) continue;
        const R2Ps2SceneCollider *collider = &platform->scene_colliders[object];
        if (excluded_instance != NULL)
        {
            const u32 excluded_index = (u32)(excluded_instance - platform->scene_instances);
            if (excluded_index < platform->scene_instance_count &&
                !colliders_interact(&platform->scene_colliders[excluded_index], collider)) continue;
        }
        if (collider->type == 3u || collider->type == 4u)
        {
            float box_y = 0.0f, box_normal_y = 1.0f;
            if (ray_hits_oriented_box_top(instance, collider, x, z, maximum_y,
                    &box_y, &box_normal_y) && box_y > best)
            { best = box_y; best_normal = box_normal_y; found = 1; }
            continue;
        }
        if (collider->type != 2u) continue;
        if (instance->mesh_slot >= platform->scene_mesh_count) continue;
        const R2Ps2MeshResource *mesh = &platform->scene_meshes[instance->mesh_slot];
        if (!mesh->ready) continue;
        const float degrees = 0.0174532925f;
        const float sx = sinf(instance->rotation[0] * degrees), cxr = cosf(instance->rotation[0] * degrees);
        const float sy = sinf(instance->rotation[1] * degrees), cyr = cosf(instance->rotation[1] * degrees);
        const float sz = sinf(instance->rotation[2] * degrees), czr = cosf(instance->rotation[2] * degrees);
        u32 collider_start = collider->height > 0.0f ? (u32)collider->radius : 0u;
        u32 collider_count = collider->height > 0.0f ? (u32)collider->height : mesh->index_count;
        if (collider_start > mesh->index_count || collider_count > mesh->index_count - collider_start)
            continue;
        const int axis_aligned = fabsf(instance->rotation[0]) < 0.001f &&
            fabsf(instance->rotation[1]) < 0.001f && fabsf(instance->rotation[2]) < 0.001f &&
            fabsf(instance->scale[0]) > 0.00001f && fabsf(instance->scale[2]) > 0.00001f;
        const float local_query_x = axis_aligned
            ? (x - instance->position[0]) / instance->scale[0] - collider->center[0] : 0.0f;
        const float local_query_z = axis_aligned
            ? (z - instance->position[2]) / instance->scale[2] - collider->center[2] : 0.0f;
        u32 meshlet_cursor = 0u, meshlet_end = 0u;
        if (mesh->meshlet_count != 0u) meshlet_end = (u32)mesh->meshlets[0].triangle_count * 3u;
        for (u32 index = collider_start; index + 2u < collider_start + collider_count; index += 3u)
        {
            while (meshlet_cursor + 1u < mesh->meshlet_count && index >= meshlet_end)
            {
                ++meshlet_cursor;
                meshlet_end += (u32)mesh->meshlets[meshlet_cursor].triangle_count * 3u;
            }
            if (axis_aligned && mesh->meshlet_count != 0u)
            {
                const R2Ps2Meshlet *cluster = &mesh->meshlets[meshlet_cursor];
                if (local_query_x < cluster->bounds_min[0] - 0.001f ||
                    local_query_x > cluster->bounds_max[0] + 0.001f ||
                    local_query_z < cluster->bounds_min[2] - 0.001f ||
                    local_query_z > cluster->bounds_max[2] + 0.001f)
                    continue;
            }
            float ax, ay, az, bx, by, bz, cx, cy, cz;
            collider_world_vertex(instance, collider, &mesh->vertices[mesh->indices[index]], sx, cxr, sy, cyr, sz, czr, &ax, &ay, &az);
            collider_world_vertex(instance, collider, &mesh->vertices[mesh->indices[index + 1u]], sx, cxr, sy, cyr, sz, czr, &bx, &by, &bz);
            collider_world_vertex(instance, collider, &mesh->vertices[mesh->indices[index + 2u]], sx, cxr, sy, cyr, sz, czr, &cx, &cy, &cz);
            const float denominator = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
            if (fabsf(denominator) < 0.000001f) continue;
            const float wa = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / denominator;
            const float wb = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / denominator;
            const float wc = 1.0f - wa - wb;
            if (wa < -0.001f || wb < -0.001f || wc < -0.001f) continue;
            const float triangle_y = wa * ay + wb * by + wc * cy;
            if (triangle_y > maximum_y || triangle_y <= best) continue;
            const float abx = bx - ax, aby = by - ay, abz = bz - az;
            const float acx = cx - ax, acy = cy - ay, acz = cz - az;
            const float nx = aby * acz - abz * acy;
            const float ny = abz * acx - abx * acz;
            const float nz = abx * acy - aby * acx;
            const float length = sqrtf(nx * nx + ny * ny + nz * nz);
            best = triangle_y; best_normal = length > 0.00001f ? fabsf(ny / length) : 1.0f; found = 1;
        }
    }
    if (found) { *height = best; *normal_y = best_normal; }
    return found;
}

static float scene_camera_sweep(const R2Ps2Platform *platform, const R2Ps2SceneInstance *character,
    R2CamVec origin, R2CamVec destination, float radius, u32 collision_mask)
{
    R2CamVec delta=cv_sub(destination,origin);
    float limit=cv_len(delta);
    if(limit<0.00001f || platform->scene_colliders==NULL) return limit;
    R2CamVec dir=cv_mul(delta,1.0f/limit);
    for(u32 object=0;object<platform->scene_instance_count;object++)
    {
        const R2Ps2SceneInstance *instance=&platform->scene_instances[object];
        const R2Ps2SceneCollider *collider=&platform->scene_colliders[object];
        if(instance==character || collider->type==0u) continue;
        if((collision_mask & (1u << collider->layer)) == 0u) continue;
        if(platform->scene_persistent_objects && platform->scene_persistent_objects[object].enabled &&
            !platform->scene_persistent_objects[object].active) continue;
        const float degrees=0.0174532925f;
        float sx=sinf(instance->rotation[0]*degrees),cx=cosf(instance->rotation[0]*degrees);
        float sy=sinf(instance->rotation[1]*degrees),cy=cosf(instance->rotation[1]*degrees);
        float sz=sinf(instance->rotation[2]*degrees),cz=cosf(instance->rotation[2]*degrees);
        /* Plane colliders are exported as thin oriented boxes (type 4), so
           camera collision uses the same swept-sphere path as BoxCollider. */
        if(collider->type==3u || collider->type==4u)
        {
            R2Ps2MeshVertex zero={0};
            R2CamVec center,local,local_dir;
            collider_world_vertex(instance,collider,&zero,sx,cx,sy,cy,sz,cz,&center.x,&center.y,&center.z);
            R2CamVec offset=cv_sub(origin,center);
            inverse_rotate_xyz(offset.x,offset.y,offset.z,sx,cx,sy,cy,sz,cz,&local.x,&local.y,&local.z);
            inverse_rotate_xyz(dir.x,dir.y,dir.z,sx,cx,sy,cy,sz,cz,&local_dir.x,&local_dir.y,&local_dir.z);
            R2CamVec half={fabsf(collider->radius*instance->scale[0])*0.5f,
                fabsf(collider->height*instance->scale[1])*0.5f,fabsf(collider->slope_limit*instance->scale[2])*0.5f};
            limit=camera_box_sweep(local,local_dir,limit,radius,half);
        }
        else if(collider->type==2u && instance->mesh_slot<platform->scene_mesh_count)
        {
            const R2Ps2MeshResource *mesh=&platform->scene_meshes[instance->mesh_slot];
            if(!mesh->ready) continue;
            u32 collider_start=collider->height>0.0f?(u32)collider->radius:0u;
            u32 collider_count=collider->height>0.0f?(u32)collider->height:mesh->index_count;
            if(collider_start>mesh->index_count||collider_count>mesh->index_count-collider_start)continue;
            for(u32 i=collider_start;i+2<collider_start+collider_count;i+=3)
            {
                R2CamVec p[3];
                for(int j=0;j<3;j++) collider_world_vertex(instance,collider,&mesh->vertices[mesh->indices[i+j]],
                    sx,cx,sy,cy,sz,cz,&p[j].x,&p[j].y,&p[j].z);
                limit=camera_triangle_sweep(origin,dir,limit,radius,p[0],p[1],p[2]);
            }
        }
        else if(collider->type==1u)
        {
            R2CamVec center={instance->position[0]+collider->center[0]*instance->scale[0],
                instance->position[1]+collider->center[1]*instance->scale[1],
                instance->position[2]+collider->center[2]*instance->scale[2]};
            float r=fabsf(collider->radius)*fmaxf(fabsf(instance->scale[0]),fabsf(instance->scale[2]));
            float half=fmaxf(0,fabsf(collider->height*instance->scale[1])*0.5f-r);
            limit=camera_capsule_sweep(cv_sub(origin,center),dir,limit,radius,half,r);
        }
    }
    return limit;
}

typedef struct {
    const R2Ps2Platform *platform;
    const R2Ps2SceneInstance *character;
    R2CamVec segment_a,segment_b;
    float radius;
    float slope_cosine;
} CharacterSweepContext;

static R2CharacterHit scene_character_sweep(void *opaque,R2CamVec root,R2CamVec movement)
{
    CharacterSweepContext *context=(CharacterSweepContext *)opaque;
    R2CharacterHit hit={cv_len(movement),(R2CamVec){0,0,0}};
    if(hit.distance<0.00001f || context->platform->scene_colliders==NULL)return hit;
    R2CamVec dir=cv_mul(movement,1.0f/hit.distance);
    R2CamVec root_delta=cv_sub(root,(R2CamVec){context->character->position[0],context->character->position[1],context->character->position[2]});
    R2CamVec p=cv_add(context->segment_a,root_delta),q=cv_add(context->segment_b,root_delta);
    const u32 character_index=(u32)(context->character-context->platform->scene_instances);
    const R2Ps2SceneCollider *character_collider=character_index<context->platform->scene_instance_count
        ? &context->platform->scene_colliders[character_index] : NULL;
    static const unsigned char faces[36]={0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,1,5,6,1,6,2,2,6,7,2,7,3,3,7,4,3,4,0};
    for(u32 object=0;object<context->platform->scene_instance_count;object++){
        const R2Ps2SceneInstance *instance=&context->platform->scene_instances[object];
        const R2Ps2SceneCollider *collider=&context->platform->scene_colliders[object];
        if(instance==context->character || (collider->type!=2u && collider->type!=3u))continue;
        if(!colliders_interact(character_collider,collider))continue;
        if(context->platform->scene_persistent_objects && context->platform->scene_persistent_objects[object].enabled &&
            !context->platform->scene_persistent_objects[object].active)continue;
        const float degrees=0.0174532925f;
        float sx=sinf(instance->rotation[0]*degrees),cx=cosf(instance->rotation[0]*degrees);
        float sy=sinf(instance->rotation[1]*degrees),cy=cosf(instance->rotation[1]*degrees);
        float sz=sinf(instance->rotation[2]*degrees),cz=cosf(instance->rotation[2]*degrees);
        if(collider->type==3u){
            float hx=fabsf(collider->radius)*.5f,hy=fabsf(collider->height)*.5f,hz=fabsf(collider->slope_limit)*.5f;
            R2Ps2MeshVertex local[8]={{-hx,-hy,-hz},{hx,-hy,-hz},{hx,-hy,hz},{-hx,-hy,hz},{-hx,hy,-hz},{hx,hy,-hz},{hx,hy,hz},{-hx,hy,hz}};
            R2CamVec v[8];
            for(int i=0;i<8;i++)collider_world_vertex(instance,collider,&local[i],sx,cx,sy,cy,sz,cz,&v[i].x,&v[i].y,&v[i].z);
            float box_top=v[0].y;
            for(int i=1;i<8;i++)if(v[i].y>box_top)box_top=v[i].y;
            /* When the capsule is already supported by this box's top, its
               vertical perimeter is a ledge, not a wall. Ground sampling
               decides whether support continues after the horizontal move. */
            const float capsule_bottom=fminf(p.y,q.y)-context->radius;
            if(capsule_bottom>=box_top-0.04f)continue;
            for(int i=0;i<36;i+=3)
                if(!character_triangle_walkable(v[faces[i]],v[faces[i+1]],v[faces[i+2]],context->slope_cosine))
                    character_triangle_sweep(p,q,dir,context->radius,v[faces[i]],v[faces[i+1]],v[faces[i+2]],&hit);
        } else if(instance->mesh_slot<context->platform->scene_mesh_count){
            const R2Ps2MeshResource *mesh=&context->platform->scene_meshes[instance->mesh_slot];if(!mesh->ready)continue;
            u32 collider_start=collider->height>0.0f?(u32)collider->radius:0u;
            u32 collider_count=collider->height>0.0f?(u32)collider->height:mesh->index_count;
            if(collider_start>mesh->index_count||collider_count>mesh->index_count-collider_start)continue;
            for(u32 i=collider_start;i+2<collider_start+collider_count;i+=3){
                R2CamVec v[3];
                for(int j=0;j<3;j++)collider_world_vertex(instance,collider,&mesh->vertices[mesh->indices[i+j]],sx,cx,sy,cy,sz,cz,&v[j].x,&v[j].y,&v[j].z);
                if(!character_triangle_walkable(v[0],v[1],v[2],context->slope_cosine))
                    character_triangle_sweep(p,q,dir,context->radius,v[0],v[1],v[2],&hit);
            }
        }
    }
    return hit;
}

static int character_inside_trigger(
    const R2Ps2SceneInstance *character,
    const R2Ps2SceneCollider *character_collider,
    const R2Ps2SceneInstance *trigger,
    const float center[3], const float size[3])
{
    const float character_radius = character_collider != NULL
        ? (character_collider->type == 3u
            ? 0.5f * fmaxf(character_collider->radius * fabsf(character->scale[0]),
                character_collider->slope_limit * fabsf(character->scale[2]))
            : character_collider->radius * fabsf(character->scale[0]))
        : 0.4f;
    const float character_half_height = character_collider != NULL
        ? character_collider->height * fabsf(character->scale[1]) * 0.5f : 0.9f;
    const float character_x = character->position[0] +
        (character_collider != NULL ? character_collider->center[0] * character->scale[0] : 0.0f);
    const float character_y = character->position[1] +
        (character_collider != NULL ? character_collider->center[1] * character->scale[1] : 0.9f);
    const float character_z = character->position[2] +
        (character_collider != NULL ? character_collider->center[2] * character->scale[2] : 0.0f);
    const float trigger_x = trigger->position[0] + center[0] * trigger->scale[0];
    const float trigger_y = trigger->position[1] + center[1] * trigger->scale[1];
    const float trigger_z = trigger->position[2] + center[2] * trigger->scale[2];
    const float half_x = fabsf(size[0] * trigger->scale[0]) * 0.5f;
    const float half_y = fabsf(size[1] * trigger->scale[1]) * 0.5f;
    const float half_z = fabsf(size[2] * trigger->scale[2]) * 0.5f;
    return fabsf(character_x - trigger_x) <= half_x + character_radius &&
        fabsf(character_y - trigger_y) <= half_y + character_half_height &&
        fabsf(character_z - trigger_z) <= half_z + character_radius;
}

static void initialize_animation_parameters(R2Ps2Playback *p, const R2AnimController *c)
{
    if (p->parameters_ready || !c) return;
    p->parameters = calloc(c->parameter_count ? c->parameter_count : 1u, sizeof(float));
    p->parameters_ready=1;
    if (!p->parameters) return;
    for (u32 i=0; i<c->parameter_count; i++) p->parameters[i]=c->parameters[i].initial;
}

int r2_ps2_animator_set_parameter(R2Ps2Platform *platform, unsigned int instance,
    const char *name, unsigned int type, float value)
{
    if (!platform || instance>=platform->scene_instance_count || instance>=4096u) return 0;
    u32 slot=platform->scene_instances[instance].mesh_slot;
    if(slot>=platform->scene_mesh_count) return 0;
    R2Ps2Playback *p=&platform->playback[instance];
    R2AnimController *c=platform->scene_meshes[slot].controller;
    initialize_animation_parameters(p,c);
    return r2_anim_set(c,p->parameters,name,type,value);
}

void r2_ps2_update_gameplay(
    R2Ps2Platform *platform,
    const R2Ps2Input *input,
    float delta_seconds)
{
    if (platform == NULL || input == NULL || !platform->external_scene_loaded)
        return;
    platform->scene_ui_visible &= ~platform->transition_prompt_mask;
    platform->transition_prompt_mask = 0u;
    platform->interaction_focus=-1;
    platform->door_focus=-1;
    const u16 script_released = r2_script_released(&platform->script_input_edges, input->buttons,
        input->connected, (platform->scene_ui_visible & platform->scene_ui_pause_gameplay) != 0u);
    const u16 script_pressed = r2_script_pressed(&platform->script_input_edges, input->buttons,
        input->connected, (platform->scene_ui_visible & platform->scene_ui_pause_gameplay) != 0u);
    if ((platform->scene_ui_visible & platform->scene_ui_pause_gameplay) != 0u)
    {
        platform->previous_buttons = input->buttons;
        return;
    }
    platform->scene_time += delta_seconds;
    for(u32 i=0;i<platform->door_count;i++)
        r2_door_tick(&platform->doors[i],platform->scene_instances[platform->doors[i].source].position,delta_seconds);
    for (u32 object = 0; platform->script_motion_count && object < platform->scene_instance_count; ++object)
    {
        if (platform->scene_persistent_objects && platform->scene_persistent_objects[object].enabled &&
            !platform->scene_persistent_objects[object].active) continue;
        R2Ps2SceneInstance *instance = &platform->scene_instances[object];
        R2ScriptTimer *condition = platform->script_timers ? &platform->script_timers[object] : NULL;
        if (condition && r2_script_is_trigger(condition->operation)) continue; /* evaluated after player movement */
        if (condition && condition->operation == 12u)
            r2_script_toggle_tick(condition, &platform->playback[object].script_motion,
                instance->position, instance->rotation, delta_seconds,
                (script_pressed & input_button_mask((s32)condition->threshold)) != 0);
        else if (condition && (condition->operation == 14u || condition->operation == 16u))
            r2_script_dual_press_tick(condition, &platform->playback[object].script_motion,
                instance->position, instance->rotation,
                (r2_script_pair_edges(condition, 0, script_pressed, script_released) & input_button_mask((s32)condition->threshold & 15)) != 0,
                (r2_script_pair_edges(condition, 1, script_pressed, script_released) & input_button_mask(((s32)condition->threshold >> 4) & 15)) != 0);
        else if (condition && condition->operation == 17u)
            r2_script_dual_held_tick(condition, &platform->playback[object].script_motion,
                instance->position, instance->rotation, delta_seconds,
                input->connected && (input->buttons & input_button_mask((s32)condition->threshold & 15)) != 0,
                input->connected && (input->buttons & input_button_mask(((s32)condition->threshold >> 4) & 15)) != 0);
        else if (condition && condition->operation == 15u)
            r2_script_press_tick(&platform->playback[object].script_motion,
                instance->position, instance->rotation,
                (script_released & input_button_mask((s32)condition->threshold)) != 0);
        else if (condition && condition->operation == 8u)
            r2_script_press_tick(&platform->playback[object].script_motion,
                instance->position, instance->rotation,
                (script_pressed & input_button_mask((s32)condition->threshold)) != 0);
        else if (condition && condition->operation == 19u)
            r2_script_start_held_tick(&platform->playback[object].script_motion,
                instance->position, instance->rotation, delta_seconds,
                input->connected && (input->buttons & input_button_mask((s32)condition->threshold)) != 0);
        else if (condition && condition->operation == 7u)
            r2_script_input_tick(condition, &platform->playback[object].script_motion,
                instance->position, instance->rotation, delta_seconds,
                input->connected && (input->buttons & input_button_mask((s32)condition->threshold)) != 0);
        else r2_script_timer_tick(condition,
            &platform->playback[object].script_motion,
            instance->position, instance->rotation, delta_seconds);
    }
    /* Playback clocks belong to objects, while baked frames remain shared. */
    for (u32 object = 0; object < platform->scene_instance_count; ++object)
    {
        u32 slot = platform->scene_instances[object].mesh_slot;
        R2Ps2Playback *p = &platform->playback[object];
        if (slot >= platform->scene_mesh_count || !(p->flags & 1u)) continue;
        R2Ps2MeshResource *resource = &platform->scene_meshes[slot];
        if (p->state >= resource->animation_state_count) continue;
        const R2Ps2AnimationState *state = &resource->animation_states[p->state];
        initialize_animation_parameters(p,resource->controller);
        int edge = r2_anim_transition(resource->controller, p->parameters, p->state,
            !state->loop && p->time >= state->duration);
        u32 target = edge >= 0 ? resource->controller->transitions[edge].to : state->finished_target;
        float transition_blend = edge >= 0 ? resource->controller->transitions[edge].blend : state->finished_blend;
        /* Editor evaluates Finished before advancing the next frame, including
           a non-looping clip that stopped on the preceding frame. */
        if (edge >= 0 || (!resource->controller && r2_animation_finished(p->flags, p->time, state->duration, state->loop,
                state->finished_target, resource->animation_state_count)))
        {
            p->previous = p->state;
            p->previous_time = p->time;
            p->state = target;
            p->time = 0;
            p->blend_duration = transition_blend;
            p->blend = p->blend_duration > 0 ? 0 : 1;
            p->flags |= 10u;
            state = &resource->animation_states[p->state];
        }
        float old_blend = p->blend;
        int was_playing = (p->flags & 2u) != 0;
        r2_animation_tick(&p->flags, &p->time, &p->blend, delta_seconds, state->speed, state->duration, state->loop);
        p->blend = !was_playing ? old_blend : p->blend_duration > 0 ? fminf(1, old_blend + delta_seconds / p->blend_duration) : 1;
    }
    R2Ps2SceneInstance *character = NULL;
    R2Ps2MeshResource *animated_mesh = NULL;
    R2Ps2Playback *player_playback = NULL;
    R2Ps2SceneCollider *character_collider = NULL;
    for (u32 object = 0; object < platform->scene_instance_count; ++object)
    {
        R2Ps2SceneInstance *candidate = &platform->scene_instances[object];
        /* Player control belongs to the scene object, not its animation data.
           Static meshes are valid controllable characters too; animation is
           an optional layer handled later when states are present. */
        if ((platform->playback[object].flags & 4u) &&
            candidate->mesh_slot < platform->scene_mesh_count)
        {
            character = candidate;
            player_playback = &platform->playback[object];
            animated_mesh = &platform->scene_meshes[candidate->mesh_slot];
            if (platform->scene_colliders != NULL &&
                (platform->scene_colliders[object].type == 1u ||
                 platform->scene_colliders[object].type == 3u))
                character_collider = &platform->scene_colliders[object];
            break;
        }
    }
    if (character == NULL || animated_mesh == NULL) return;

    if (platform->scene_controls.kill_floor_enabled &&
        character->position[1] < platform->scene_controls.kill_floor_height)
    {
        char restart_path[256];
        snprintf(restart_path, sizeof(restart_path), "%s", platform->current_scene_path);
        if (!r2_ps2_load_scene_file(platform, restart_path))
            printf("Kill floor scene restart failed: %s\n", restart_path);
        return;
    }

    u32 blocking=platform->scene_ui_pause_gameplay;
    for(u32 i=0;i<platform->interactable_count;i++) blocking |= 1u<<platform->interactables[i].canvas;
    for(u32 i=0;i<platform->scene_ui_count;i++)
        if(platform->scene_ui_elements[i].type==2u) blocking |= 1u << (platform->scene_ui_elements[i].interactable>>16);
    if(!(platform->scene_ui_visible & blocking))
    {
        float nearest=1e30f;
        for(u32 i=0;i<platform->interactable_count;i++)
        {
            R2Interactable *p=&platform->interactables[i];
            if(platform->scene_ui_visible & (1u<<p->canvas)) continue;
            R2Ps2SceneInstance volume={0}; volume.scale[0]=volume.scale[1]=volume.scale[2]=1;
            if(p->source<platform->scene_instance_count)
                for(u32 a=0;a<3;a++) volume.position[a]=platform->scene_instances[p->source].position[a]+p->center[a];
            else memcpy(volume.position,p->center,12);
            const float zero[3]={0,0,0};
            if(character_inside_trigger(character,character_collider,&volume,zero,p->size))
            {
                float distance=0; for(u32 a=0;a<3;a++) { float d=character->position[a]-volume.position[a]; distance+=d*d; }
                if(distance<nearest) { nearest=distance; platform->interaction_focus=(int)i; }
            }
        }
        if(platform->interaction_focus>=0)
        {
            R2Interactable *p=&platform->interactables[platform->interaction_focus];
            u16 mask=input_button_mask((int)p->button);
            if((input->buttons & mask) && !(platform->previous_buttons & mask))
            {
                platform->scene_ui_visible |= 1u<<p->canvas;
                play_ui_sound(platform, 4u);
                platform->interaction_canvas=(int)p->canvas;
                p->page=0u;
                if(p->page_count)
                    for(u32 element=0;element<platform->scene_ui_count;element++)
                        if(platform->scene_ui_elements[element].type==3u &&
                            (platform->scene_ui_elements[element].interactable>>16)==p->canvas)
                        { snprintf(platform->scene_ui_elements[element].text,64,"%s",p->pages[0]); break; }
                platform->scene_ui_focus=-1; platform->interaction_focus=-1;
                platform->previous_buttons=input->buttons;
                return; /* consume the opening press before jump/movement */
            }
        }
    }

    if(!(platform->scene_ui_visible & blocking) && platform->interaction_focus<0)
    {
        float nearest=1e30f;
        for(u32 i=0;i<platform->door_count;i++)
        {
            R2SlidingDoor *d=&platform->doors[i];float distance=r2_door_distance(d,character->position);
            if(!d->opening && distance<=d->range*d->range && distance<nearest){nearest=distance;platform->door_focus=(int)i;}
        }
        if(platform->door_focus>=0)
        {
            R2SlidingDoor *d=&platform->doors[platform->door_focus];u16 mask=input_button_mask((int)d->button);
            if(input->connected && (script_pressed & mask))
            {d->opening=1;platform->door_focus=-1;platform->previous_buttons=input->buttons;return;}
        }
    }

    /* An interaction owns character and orbit-camera input until its root
       canvas (or one of its nested submenu canvases) has finished. Animation
       playback above keeps running, so NPC reactions and dialogue motion are
       still alive while the player is locked. */
    if(platform->interaction_canvas>=0 && platform->interaction_canvas<32)
    {
        const u32 root=(u32)platform->interaction_canvas;
        int active=(platform->scene_ui_visible & (1u<<root))!=0u;
        int interaction_chain=0;
        for(u32 i=0;i<platform->menu_depth;i++)
        {
            if(platform->menu_parent[i]==root) interaction_chain=1;
            if(interaction_chain && (platform->scene_ui_visible & (1u<<platform->menu_child[i]))) active=1;
        }
        if(active)
        {
            R2Interactable *p=NULL;
            for(u32 i=0;i<platform->interactable_count;i++)
                if(platform->interactables[i].canvas==root) { p=&platform->interactables[i]; break; }
            if(p)
            {
                const u16 mask=input_button_mask((int)p->button);
                if((input->buttons & mask) && !(platform->previous_buttons & mask))
                {
                    if(p->page_count && p->page+1u<p->page_count)
                    {
                        p->page++;
                        for(u32 element=0;element<platform->scene_ui_count;element++)
                            if(platform->scene_ui_elements[element].type==3u &&
                                (platform->scene_ui_elements[element].interactable>>16)==root)
                            { snprintf(platform->scene_ui_elements[element].text,64,"%s",p->pages[p->page]); break; }
                    }
                    else
                    {
                        while(platform->menu_depth) ui_go_back(platform);
                        platform->scene_ui_visible &= ~(1u<<root);
                        platform->interaction_canvas=-1;
                    }
                }
            }
            platform->previous_buttons=input->buttons;
            return;
        }
        platform->interaction_canvas=-1;
    }

    const R2Ps2SceneControls *controls = &platform->scene_controls;
    if (!controls->enabled) return;
    if (platform->startup_control_frames == 0)
        printf("R2 startup: character yaw=%.3f camera yaw=%.3f analog=%d\n",
            character->rotation[1], platform->scene_camera.rotation[1], input->analog);
    if (platform->startup_control_frames < 120) ++platform->startup_control_frames;
    /* Recover the authored orbit endpoint before this frame's input. */
    for(int axis=0;axis<3;axis++) platform->scene_camera.position[axis]+=platform->camera_correction[axis];
    memset(platform->camera_correction,0,sizeof(platform->camera_correction));
    float strafe = input_dead_zone(input_axis(input, controls->move_x_axis),
        controls->move_dead_zone);
    float forward = input_dead_zone(input_axis(input, controls->move_y_axis),
        controls->move_dead_zone);
    float turn = input_dead_zone(input_axis(input, controls->turn_axis),
        controls->turn_dead_zone);
    if (controls->invert_move_x) strafe = -strafe;
    if (controls->invert_move_y) forward = -forward;
    if (controls->invert_turn) turn = -turn;
    const float turn_degrees = turn * controls->turn_speed * delta_seconds;
    /* Orbit the camera independently of Mimi's facing, retaining its authored
       distance, height and pitch. Save/load already stores this world pose. */
    const float turn_radians = turn_degrees * 0.0174532925f;
    const float turn_sine = sinf(turn_radians);
    const float turn_cosine = cosf(turn_radians);
    const float camera_offset_x = platform->scene_camera.position[0] - character->position[0];
    const float camera_offset_z = platform->scene_camera.position[2] - character->position[2];
    platform->scene_camera.position[0] = character->position[0] +
        turn_cosine * camera_offset_x + turn_sine * camera_offset_z;
    platform->scene_camera.position[2] = character->position[2] -
        turn_sine * camera_offset_x + turn_cosine * camera_offset_z;
    platform->scene_camera.rotation[1] += turn_degrees;
    const float radians = platform->scene_camera.rotation[1] * 0.0174532925f;
    float pitch_input = -input_dead_zone(input_axis(input, 3), 0.2f);
    if (controls->invert_camera_pitch) pitch_input = -pitch_input;
    if (fabsf(pitch_input) > 0.0001f)
    {
        const float old_pitch = platform->scene_camera.rotation[0];
        const float pitch = fmaxf(controls->camera_min_pitch, fminf(controls->camera_max_pitch,
            old_pitch + pitch_input * controls->camera_pitch_speed * delta_seconds));
        const float angle = (pitch - old_pitch) * 0.0174532925f;
        const float cs = cosf(angle), sn = sinf(angle);
        const float ax = cosf(radians), az = -sinf(radians);
        const float x = platform->scene_camera.position[0] - character->position[0];
        const float y = platform->scene_camera.position[1] - character->position[1];
        const float z = platform->scene_camera.position[2] - character->position[2];
        const float dot = ax * x + az * z;
        /* Rotate around the camera's horizontal right axis, not world X. */
        platform->scene_camera.position[0] = character->position[0] + x * cs - az * y * sn + ax * dot * (1-cs);
        platform->scene_camera.position[1] = character->position[1] + y * cs + (az * x - ax * z) * sn;
        platform->scene_camera.position[2] = character->position[2] + z * cs + ax * y * sn + az * dot * (1-cs);
        platform->scene_camera.rotation[0] = pitch;
    }
    const u16 sprint_mask = input_button_mask(controls->sprint_button);
    const float movement_length = sqrtf(strafe * strafe + forward * forward);
    const int movement_locked = player_playback->state < animated_mesh->animation_state_count &&
        animated_mesh->animation_states[player_playback->state].lock_movement;
    const int sprinting = r2_anim_sprinting(controls->enable_sprint,
        sprint_mask != 0u && (input->buttons & sprint_mask) != 0, movement_length);
    const float sprint = sprinting ? controls->sprint_multiplier : 1.0f;
    if (movement_length > 1.0f)
    { strafe /= movement_length; forward /= movement_length; }
    const float distance = movement_locked ? 0.0f : controls->move_speed * sprint * delta_seconds;
    const float move_x = -sinf(radians) * forward + cosf(radians) * strafe;
    const float move_z = -cosf(radians) * forward - sinf(radians) * strafe;
    if (!movement_locked && movement_length > 0.0001f)
    {
        if (platform->startup_control_frames < 120 &&
            fabsf(character->rotation[1] - atan2f(move_x, move_z) / 0.0174532925f) > 0.1f)
            printf("R2 startup turn: yaw=%.3f -> %.3f analog=%d sticks=%u,%u move=%.3f,%.3f\n",
                character->rotation[1], atan2f(move_x, move_z) / 0.0174532925f,
                input->analog, input->left_x, input->left_y, strafe, forward);
        character->rotation[1] = atan2f(move_x, move_z) / 0.0174532925f;
    }
    const float dx = move_x * distance;
    const float dz = move_z * distance;
    float accepted_dx = dx, accepted_dz = dz;
    float current_ground = 0.0f, candidate_ground = 0.0f, candidate_normal_y = 1.0f;
    float unused_normal = 1.0f;
    const float capsule_bottom = character_collider != NULL
        ? character_collider->center[1] * character->scale[1] -
            fabsf(character_collider->height * character->scale[1]) * 0.5f
        : 0.0f;
    const float step_height = character_collider != NULL && character_collider->type == 1u
        ? fmaxf(0.0f, character_collider->step_height) : 0.35f;
    const float feet_y = character->position[1] + capsule_bottom;
    const int has_current_ground = scene_ground_height(platform,
        character->position[0], character->position[2], feet_y + 0.04f,
        character,
        &current_ground, &unused_normal);
    const int has_candidate_ground = scene_ground_height(platform,
        character->position[0] + dx, character->position[2] + dz, feet_y + step_height + 0.04f,
        character,
        &candidate_ground, &candidate_normal_y);
    const float slope_cosine = cosf((character_collider != NULL && character_collider->type == 1u ?
        character_collider->slope_limit : 45.0f) * 0.0174532925f);
    /* Missing ground is an actual ledge, not an invisible wall. Accept the
       horizontal move and let gravity make the capsule fall. Only reject a
       candidate when real geometry exists there but is too steep/high. */
    if (platform->character_grounded && has_candidate_ground && (
        candidate_normal_y < slope_cosine ||
        (has_current_ground && candidate_ground - current_ground > step_height)))
    { accepted_dx = 0.0f; accepted_dz = 0.0f; }
    if(character_collider!=NULL && character_collider->type==1u &&
        (fabsf(accepted_dx)>0.000001f || fabsf(accepted_dz)>0.000001f))
    {
        const float radius=fabsf(character_collider->radius)*
            fmaxf(fabsf(character->scale[0]),fabsf(character->scale[2]));
        const float height=fmaxf(radius*2.0f,
            fabsf(character_collider->height*character->scale[1]));
        const float half=fmaxf(0.0f,height*0.5f-radius);
        R2CamVec center={character->position[0]+character_collider->center[0]*character->scale[0],
            character->position[1]+character_collider->center[1]*character->scale[1],
            character->position[2]+character_collider->center[2]*character->scale[2]};
        CharacterSweepContext sweep_context={platform,character,
            {center.x,center.y-half,center.z},{center.x,center.y+half,center.z},radius,slope_cosine};
        R2CamVec root={character->position[0],character->position[1],character->position[2]};
        R2CamVec resolved=character_slide(&sweep_context,scene_character_sweep,root,
            (R2CamVec){accepted_dx,0.0f,accepted_dz});
        accepted_dx=resolved.x;accepted_dz=resolved.z;
    }
    character->position[0] += accepted_dx;
    character->position[2] += accepted_dz;
    /* Preserve the camera authored in the cooked scene. Following simply moves
       its existing offset with the character; the old right-stick fly camera
       is no longer part of cooked-scene execution. */
    platform->scene_camera.position[0] += accepted_dx;
    platform->scene_camera.position[2] += accepted_dz;

    const u16 jump_mask = input_button_mask(controls->jump_button);
    const int jump_pressed = controls->enable_jump && jump_mask != 0u &&
        (input->buttons & jump_mask) != 0 &&
        (platform->previous_buttons & jump_mask) == 0;
    const int jump_requested = jump_pressed && platform->character_grounded && platform->jump_time < 0.0f;
    const u32 jump_state = animation_state_for_kind(animated_mesh, 4u);
    const int has_jump_launch_marker = jump_state < animated_mesh->animation_state_count &&
        animated_mesh->animation_states[jump_state].jump_launch_time >= 0.0f;
    if (jump_requested)
    {
        if (controls->launch_jump_from_animation_event && animated_mesh->controller && has_jump_launch_marker)
            platform->jump_time = 0.0f;
        else
        {
            platform->vertical_velocity = controls->jump_strength;
            platform->character_grounded = 0;
        }
    }
    if (platform->jump_time >= 0.0f)
    {
        platform->jump_time += delta_seconds;
        const float marker = animated_mesh->animation_states[jump_state].jump_launch_time;
        const int marker_reached = player_playback->state == jump_state && player_playback->time >= marker;
        const int timed_out = platform->jump_time >= controls->jump_event_timeout;
        if (marker_reached || timed_out)
        {
            platform->vertical_velocity = controls->jump_strength;
            platform->character_grounded = 0;
            platform->jump_time = -1.0f;
        }
    }
    u32 desired_kind = 0u;
    const float old_y = character->position[1];
    /* Fixed vertical substeps keep jump height and collision identical when a
       heavier room runs below the emulator's usual 60 Hz. */
    float remaining_vertical_time = fminf(delta_seconds, 0.1f);
    while (remaining_vertical_time > 0.000001f)
    {
        const float step_seconds = fminf(remaining_vertical_time, 1.0f / 60.0f);
        const float step_old_y = character->position[1];
        float vertical_move = platform->vertical_velocity * step_seconds -
            4.9f * step_seconds * step_seconds;
        platform->vertical_velocity -= 9.8f * step_seconds;
        character->position[1] += vertical_move;
        float ground_y = 0.0f, ground_normal_y = 1.0f;
        const int has_ground = scene_ground_height(platform,
            character->position[0], character->position[2], step_old_y + capsule_bottom +
                (platform->character_grounded ? step_height : 0.0f) + 0.04f,
            character, &ground_y, &ground_normal_y);
        const float grounded_root_y = ground_y - capsule_bottom;
        if (has_ground && platform->vertical_velocity <= 0.0f &&
            character->position[1] <= grounded_root_y + 0.04f)
        {
            character->position[1] = grounded_root_y;
            platform->vertical_velocity = 0.0f;
            platform->character_grounded = ground_normal_y >= slope_cosine;
        }
        else platform->character_grounded = 0;
        remaining_vertical_time -= step_seconds;
    }
    platform->scene_camera.position[1] += character->position[1] - old_y;
    if(controls->camera_collision)
    {
        R2CamVec pivot={character->position[0],character->position[1]+controls->camera_target_height,character->position[2]};
        R2CamVec desired={platform->scene_camera.position[0],platform->scene_camera.position[1],platform->scene_camera.position[2]};
        R2CamVec offset=cv_sub(desired,pivot);
        float length=cv_len(offset);
        float safe=scene_camera_sweep(platform,character,pivot,desired,
            controls->camera_collision_radius+controls->camera_clearance,
            controls->camera_collision_mask);
        platform->camera_retraction=fmaxf(length-safe,platform->camera_retraction-controls->camera_return_speed*delta_seconds);
        if(length>0.00001f)
        {
            R2CamVec resolved=cv_add(pivot,cv_mul(offset,fmaxf(0,length-platform->camera_retraction)/length));
            platform->camera_correction[0]=desired.x-resolved.x;
            platform->camera_correction[1]=desired.y-resolved.y;
            platform->camera_correction[2]=desired.z-resolved.z;
            platform->scene_camera.position[0]=resolved.x;
            platform->scene_camera.position[1]=resolved.y;
            platform->scene_camera.position[2]=resolved.z;
        }
    }
    else platform->camera_retraction=0;
    if (!platform->character_grounded) desired_kind = 4u;
    else if (movement_length > 0.01f && sprinting) desired_kind = 2u;
    else if (movement_length > 0.01f) desired_kind = 1u;
    if (platform->character_grounded && movement_length <= 0.0001f)
        platform->player_idle_time += delta_seconds;
    else
        platform->player_idle_time = 0.0f;

    const u32 desired_state = animation_state_for_kind(animated_mesh, desired_kind);
    if (animated_mesh->controller && player_playback->parameters)
    {
        initialize_animation_parameters(player_playback,animated_mesh->controller);
        for (u32 i=0; i<animated_mesh->controller->parameter_count; i++)
        {
            switch(animated_mesh->controller->parameters[i].binding) {
                case 1: player_playback->parameters[i]=fminf(1,movement_length)*sprint; break;
                case 2: player_playback->parameters[i]=platform->vertical_velocity; break;
                case 3: player_playback->parameters[i]=movement_length>0.0001f; break;
                case 4: player_playback->parameters[i]=0; break; /* camera-relative movement */
                case 5: player_playback->parameters[i]=sprinting; break;
                case 6: player_playback->parameters[i]=platform->character_grounded; break;
                case 7: if(jump_requested) player_playback->parameters[i]=1; break;
                case 8: player_playback->parameters[i]=platform->player_idle_time; break;
            }
        }
    }
    if ((!animated_mesh->controller || !animated_mesh->controller->parameter_count) &&
        (player_playback->flags & 1u) && desired_state != player_playback->state)
    {
        player_playback->previous = player_playback->state;
        player_playback->previous_time = player_playback->time;
        player_playback->blend_duration = .15f;
        player_playback->state = desired_state;
        player_playback->time = 0;
        player_playback->blend = 0;
        player_playback->flags |= 10u;
    }
    platform->previous_buttons = input->buttons;

    int inside_save_point = 0;
    if (platform->script_timers && platform->scene_transitions)
        for (u32 object=0; object<platform->scene_instance_count; ++object)
        {
            R2ScriptTimer *condition=&platform->script_timers[object];
            if (!r2_script_is_trigger(condition->operation)) continue;
            R2Ps2SceneInstance *instance=&platform->scene_instances[object];
            const R2Ps2SceneTransition *volume=&platform->scene_transitions[object];
            int active = !platform->scene_persistent_objects || !platform->scene_persistent_objects[object].enabled ||
                platform->scene_persistent_objects[object].active;
            int inside = active && character_inside_trigger(character,character_collider,instance,volume->center,volume->size);
            r2_script_trigger_tick(condition,&platform->playback[object].script_motion,
                instance->position,instance->rotation,inside);
        }
    for(u32 i=0;i<platform->animator_zone_count;i++)
    {
        R2AnimatorZone *z=&platform->animator_zones[i];
        R2Ps2SceneInstance volume={0};
        memcpy(volume.position,z->center,12);
        volume.scale[0]=volume.scale[1]=volume.scale[2]=1;
        const float zero[3]={0,0,0};
        int inside=z->enabled && character_inside_trigger(character,character_collider,&volume,zero,z->size);
        if(inside && !z->inside) r2_ps2_animator_set_parameter(platform,z->target,z->trigger,3u,1);
        z->inside=inside;
    }
    if (platform->scene_save_points != NULL && platform->scene_time >= 0.25f)
    {
        for (u32 object = 0; object < platform->scene_instance_count; ++object)
        {
            R2Ps2SceneSavePoint *save_point = &platform->scene_save_points[object];
            if (!save_point->enabled || save_point->slot_name[0] == '\0' ||
                !character_inside_trigger(character, character_collider,
                    &platform->scene_instances[object], save_point->center, save_point->size))
                continue;
            inside_save_point = 1;
            if (!platform->autosave_latched)
                r2_ps2_save_checkpoint(platform, save_point->slot_name);
            break;
        }
    }
    platform->autosave_latched = inside_save_point;

    if (!platform->transition_latched && platform->scene_time >= 0.25f &&
        platform->scene_transitions != NULL)
    {
        for (u32 object = 0; object < platform->scene_instance_count; ++object)
        {
            R2Ps2SceneTransition *transition = &platform->scene_transitions[object];
            if (!transition->enabled || transition->scene_name[0] == '\0' ||
                !character_inside_trigger(character, character_collider,
                    &platform->scene_instances[object], transition->center, transition->size))
                continue;
            char target_scene[128];
            int require_interact = 0, button = 0, prompt_canvas = -1;
            float arrival[3] = {0,0,0}, arrival_rotation[3] = {0,0,0};
            int portal_fields = sscanf(transition->scene_name,
                "%127[^|]|I|%d|%f|%f|%f|%f|%f|%f|%d", target_scene, &button,
                &arrival[0], &arrival[1], &arrival[2],
                &arrival_rotation[0], &arrival_rotation[1], &arrival_rotation[2], &prompt_canvas);
            if (portal_fields >= 8)
            {
                require_interact = 1;
                if (portal_fields == 9 && prompt_canvas >= 0 && prompt_canvas < 32)
                {
                    const u32 prompt_bit = 1u << (u32)prompt_canvas;
                    platform->transition_prompt_mask |= prompt_bit;
                    const int prompt_was_hidden = (platform->scene_ui_visible & prompt_bit) == 0u;
                    platform->scene_ui_visible |= prompt_bit;
                    if (prompt_was_hidden) play_ui_sound(platform, 4u);
                    platform->scene_ui_pause_gameplay &= ~prompt_bit;
                }
                if ((script_pressed & input_button_mask(button)) == 0) continue;
            }
            else snprintf(target_scene, sizeof(target_scene), "%s", transition->scene_name);
            char scene_path[512];
            const char *extension = strrchr(target_scene, '.');
            snprintf(scene_path, sizeof(scene_path), extension == NULL
                ? "host:assets/%s.r2scene" : "host:assets/%s",
                target_scene);
            platform->transition_latched = 1;
            printf("Loading triggered scene: %s\n", scene_path);
            if (!r2_ps2_load_scene_file(platform, scene_path))
                printf("Triggered scene failed to load: %s\n", scene_path);
            else if (require_interact)
            {
                for (u32 destination = 0; destination < platform->scene_instance_count; ++destination)
                {
                    if ((platform->playback[destination].flags & 4u) == 0) continue;
                    memcpy(platform->scene_instances[destination].position, arrival, sizeof(arrival));
                    memcpy(platform->scene_instances[destination].rotation, arrival_rotation, sizeof(arrival_rotation));
                    break;
                }
                platform->previous_buttons = input->buttons;
            }
            break;
        }
    }
}

static R2ProjectedVertex project_mesh_vertex(
    const R2Ps2Platform *platform,
    const R2Ps2MeshVertex *vertex,
    const float *camera_transform,
    float focal)
{
    float camera[3];
    r2_transform_camera(camera_transform, vertex->x, vertex->y, vertex->z, camera);
    const float x = camera[0], y = camera[1], depth = -camera[2];
    const float reciprocal_depth = 1.0f / depth;
    R2ProjectedVertex result;
    result.visible = depth >= platform->scene_camera.near_clip &&
        depth <= platform->scene_camera.far_clip;
    result.camera_x = x;
    result.camera_y = y;
    result.depth = depth;
    const float horizontal_scale = (float)platform->gs->Width /
        ((float)platform->gs->Height * platform->scene_camera.aspect);
    result.x = (float)platform->gs->Width * 0.5f + x * focal * horizontal_scale * reciprocal_depth;
    result.y = (float)platform->gs->Height * 0.5f - y * focal * reciprocal_depth;
    result.q = reciprocal_depth;
    /* PSMZ24 reciprocal depth. Keep nearer fragments larger for the
       greater/equal depth comparison while using the full 24-bit range. */
    float depth_value = 16777215.0f * platform->scene_camera.near_clip * reciprocal_depth;
    if (depth_value < 0.0f) depth_value = 0.0f;
    if (depth_value > 16777215.0f) depth_value = 16777215.0f;
    result.z = (u32)depth_value;
    result.red = result.green = result.blue = 255.0f;
    result.alpha = 128.0f;
    result.fog = 255.0f;
    return result;
}

static R2ProjectedVertex project_camera_vertex(
    const R2Ps2Platform *platform, float x, float y, float depth)
{
    const float degrees = 0.0174532925f;
    const float focal = ((float)platform->gs->Height * 0.5f) /
        tanf(platform->scene_camera.field_of_view * degrees * 0.5f);
    R2ProjectedVertex result;
    result.camera_x = x;
    result.camera_y = y;
    result.depth = depth;
    result.visible = depth >= platform->scene_camera.near_clip &&
        depth <= platform->scene_camera.far_clip;
    const float horizontal_scale = (float)platform->gs->Width /
        ((float)platform->gs->Height * platform->scene_camera.aspect);
    result.x = (float)platform->gs->Width * 0.5f + x * focal * horizontal_scale / depth;
    result.y = (float)platform->gs->Height * 0.5f - y * focal / depth;
    result.q = 1.0f / depth;
    float depth_value = 16777215.0f * platform->scene_camera.near_clip / depth;
    if (depth_value < 0.0f) depth_value = 0.0f;
    if (depth_value > 16777215.0f) depth_value = 16777215.0f;
    result.z = (u32)depth_value;
    result.red = result.green = result.blue = 255.0f;
    result.alpha = 128.0f;
    result.fog = 255.0f;
    return result;
}

static void interpolate_clip_vertex(
    const R2Ps2Platform *platform,
    const R2Ps2MeshVertex *a, const R2ProjectedVertex *pa,
    const R2Ps2MeshVertex *b, const R2ProjectedVertex *pb,
    float amount, R2Ps2MeshVertex *result, R2ProjectedVertex *projected)
{
    const float *from = (const float *)a;
    const float *to = (const float *)b;
    float *destination = (float *)result;
    for (int component = 0; component < 8; ++component)
        destination[component] = from[component] + (to[component] - from[component]) * amount;
    const float x = pa->camera_x + (pb->camera_x - pa->camera_x) * amount;
    const float y = pa->camera_y + (pb->camera_y - pa->camera_y) * amount;
    const float depth = pa->depth + (pb->depth - pa->depth) * amount;
    *projected = project_camera_vertex(platform, x, y, depth);
    projected->red = pa->red + (pb->red - pa->red) * amount;
    projected->green = pa->green + (pb->green - pa->green) * amount;
    projected->blue = pa->blue + (pb->blue - pa->blue) * amount;
    projected->alpha = pa->alpha + (pb->alpha - pa->alpha) * amount;
    projected->fog = pa->fog + (pb->fog - pa->fog) * amount;
}

static float clip_plane_distance(
    const R2Ps2Platform *platform, const R2ProjectedVertex *vertex, int plane,
    float horizontal, float vertical)
{
    switch (plane)
    {
        case 0: return vertex->depth - platform->scene_camera.near_clip;
        case 1: return platform->scene_camera.far_clip - vertex->depth;
        case 2: return vertex->camera_x + vertex->depth * horizontal;
        case 3: return vertex->depth * horizontal - vertex->camera_x;
        case 4: return vertex->camera_y + vertex->depth * vertical;
        default: return vertex->depth * vertical - vertex->camera_y;
    }
}

static int clip_polygon_plane(
    const R2Ps2Platform *platform,
    const R2Ps2MeshVertex *input_vertices,
    const R2ProjectedVertex *input_projected,
    int input_count,
    R2Ps2MeshVertex *output_vertices,
    R2ProjectedVertex *output_projected,
    int plane, float horizontal, float vertical)
{
    int output_count = 0;
    for (int current = 0; current < input_count; ++current)
    {
        const int previous = current == 0 ? input_count - 1 : current - 1;
        const float previous_distance = clip_plane_distance(
            platform, &input_projected[previous], plane, horizontal, vertical);
        const float current_distance = clip_plane_distance(
            platform, &input_projected[current], plane, horizontal, vertical);
        const int previous_inside = previous_distance >= 0.0f;
        const int current_inside = current_distance >= 0.0f;
        if (previous_inside != current_inside)
        {
            const float amount = previous_distance /
                (previous_distance - current_distance);
            interpolate_clip_vertex(platform,
                &input_vertices[previous], &input_projected[previous],
                &input_vertices[current], &input_projected[current], amount,
                &output_vertices[output_count], &output_projected[output_count]);
            ++output_count;
        }
        if (current_inside)
        {
            output_vertices[output_count] = input_vertices[current];
            output_projected[output_count] = input_projected[current];
            ++output_count;
        }
    }
    return output_count;
}

static u32 clip_outcode(
    const R2Ps2Platform *platform, const R2ProjectedVertex *vertex,
    float horizontal, float vertical)
{
    u32 code = 0u;
    if (vertex->depth < platform->scene_camera.near_clip) code |= 1u << 0;
    if (vertex->depth > platform->scene_camera.far_clip) code |= 1u << 1;
    if (vertex->camera_x < -vertex->depth * horizontal) code |= 1u << 2;
    if (vertex->camera_x > vertex->depth * horizontal) code |= 1u << 3;
    if (vertex->camera_y < -vertex->depth * vertical) code |= 1u << 4;
    if (vertex->camera_y > vertex->depth * vertical) code |= 1u << 5;
    return code;
}

#define R2_WORLD_BATCH_TRIANGLES 1280u
typedef struct R2WorldTriangleBatch
{
    GSPRIMSTQPOINT textured[R2_WORLD_BATCH_TRIANGLES * 3u];
    GSPRIMPOINT plain[R2_WORLD_BATCH_TRIANGLES * 3u];
    u32 point_count;
    GSTEXTURE *texture;
} R2WorldTriangleBatch;

static R2WorldTriangleBatch world_triangle_batch;

static void flush_world_triangle_batch(R2Ps2Platform *platform)
{
    R2WorldTriangleBatch *batch = &world_triangle_batch;
    if (batch->point_count == 0u) return;
    if (batch->texture != NULL)
        gsKit_prim_list_triangle_goraud_texture_stq_3d(
            platform->gs, batch->texture, batch->point_count, batch->textured);
    else
        gsKit_prim_list_triangle_gouraud_3d(
            platform->gs, batch->point_count, batch->plain);
    gsKit_queue_exec(platform->gs);
    /* gsKit emits REF transfers to this shared batch storage. A GIF DMA wait
       alone is not a sufficient ownership boundary: the GS can still be
       consuming referenced primitive data when the EE starts overwriting the
       next batch. Keep the conservative FINISH barrier until batches own
       independent ring-buffer storage. */
    dmaKit_wait_fast();
    gsKit_queue_reset(platform->gs->Per_Queue);
    platform->queued_world_triangles = 0u;
    batch->point_count = 0u;
}

static int draw_vu1_meshlets(
    R2Ps2Platform *platform, const R2Ps2MeshResource *mesh,
    const R2Ps2MeshVertex *vertices, const R2ProjectedVertex *projected,
    GSTEXTURE *texture, int fog_enabled, u32 index_start, u32 index_count,
    const float texture_tiling[2], const float texture_offset[2],
    int double_sided)
{
    const int vu_fog_enabled = fog_enabled != 0;
    if (!r2_vu1_ready || mesh->meshlets == NULL || mesh->meshlet_count == 0u)
        return 0;
    const clock_t setup_start = clock();

    const int state_changed = texture != r2_vu1_active_texture ||
        vu_fog_enabled != r2_vu1_active_fog;
    if (state_changed)
    {
        r2_vu1_wait_submission();
        flush_world_triangle_batch(platform);
        if (texture != NULL)
        {
            /* Queue a zero-area primitive so gsKit emits the correct TEX0/TEX1
               state for this texture before path 1 consumes the meshlet GIF tags. */
            gsKit_prim_sprite_texture(platform->gs, texture,
                0.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 0.0f, 1,
                GS_SETREG_RGBAQ(128, 128, 128, 128, 0x00));
        }
        /* Submit clear/test/texture state through GIF path 3 only when it
           actually changes. Adjacent imported walls and beams can then keep
           path 1 active instead of serializing the GS once per child. */
        gsKit_queue_exec(platform->gs);
        dmaKit_wait_fast();
        gsKit_queue_reset(platform->gs->Per_Queue);
        r2_vu1_active_texture = texture;
        r2_vu1_active_fog = vu_fog_enabled;
    }

    clock_t prep_start = clock();
    r2_profile_vu_setup_ticks += prep_start - setup_start;
    packet2_t *packet = r2_vu1_packets[r2_vu1_context];
    u32 batch_jobs = 0u;
    packet2_reset(packet, 0);

    const u32 first_triangle = index_start / 3u;
    const u32 end_triangle = first_triangle + index_count / 3u;
    u32 meshlet_first_triangle = 0u;
    int found_clipped_triangle = 0;
    int clipped_list_overflow = 0;
    r2_vu1_clipped_index_count = 0u;
    for (u32 meshlet_index = 0; meshlet_index < mesh->meshlet_count; ++meshlet_index)
    {
        const R2Ps2Meshlet *meshlet = &mesh->meshlets[meshlet_index];
        if (meshlet->triangle_count == 0u || meshlet->triangle_count > 96u)
            return 0;
        const u32 meshlet_end_triangle = meshlet_first_triangle + meshlet->triangle_count;
        if (meshlet_first_triangle >= end_triangle)
            break;
        if (meshlet_end_triangle <= first_triangle)
        {
            meshlet_first_triangle = meshlet_end_triangle;
            continue;
        }
        const u32 local_first = first_triangle > meshlet_first_triangle
            ? first_triangle - meshlet_first_triangle : 0u;
        const u32 local_end = end_triangle < meshlet_end_triangle
            ? end_triangle - meshlet_first_triangle : meshlet->triangle_count;
        for (u32 triangle_base = local_first; triangle_base < local_end;
             triangle_base += 24u)
        {
        const u32 source_batch_triangles = local_end - triangle_base > 24u
            ? 24u : local_end - triangle_base;
        u128 *input = r2_vu1_inputs[r2_vu1_context][batch_jobs];
        /* No full-buffer clear: the VU reads only params.x, the complete GIF
           tag, three fully copied qwords per referenced vertex, and xyz of
           each active index record. The loader validates every local index.
           Unused parameter/index lanes and trailing records are not read,
           even when a smaller job reuses a slot from a larger one. */
        /* A job covers at most 24 of a meshlet's triangles. Compact its
           vertex table after visibility/backface rejection instead of
           copying all (up to 64) parent-meshlet vertices for every job. */
        u8 compact_for_local[64];
        u8 compact_source[64];
        u8 compact_indices[24u * 3u];
        memset(compact_for_local, 0xff, sizeof(compact_for_local));
        u32 compact_vertex_count = 0u;
        u32 batch_triangles = 0u;
        for (u32 triangle = 0; triangle < source_batch_triangles; ++triangle)
        {
            const u32 source_triangle = triangle_base + triangle;
            const u8 ia = meshlet->indices[source_triangle * 3u];
            const u8 ib = meshlet->indices[source_triangle * 3u + 1u];
            const u8 ic = meshlet->indices[source_triangle * 3u + 2u];
            const R2ProjectedVertex *a = &projected[meshlet->vertices[ia]];
            const R2ProjectedVertex *b = &projected[meshlet->vertices[ib]];
            const R2ProjectedVertex *c = &projected[meshlet->vertices[ic]];
            const u32 common_outcode = a->outcode & b->outcode & c->outcode;
            if (common_outcode != 0u) continue;
            const u32 combined_outcode = a->outcode | b->outcode | c->outcode;
            /* The GS scissor clips screen-edge overflow after rasterization,
               and gsKit keeps submitted XY inside the GS primitive coordinate
               range. Only depth-plane crossings require the EE to create new
               vertices; sending side-plane crossings through VU1 avoids the
               six-plane Sutherland-Hodgman fallback for ordinary room edges. */
            if ((combined_outcode & 0x03u) != 0u)
            {
                found_clipped_triangle = 1;
                if (r2_vu1_clipped_index_count < R2_VU1_MAX_CLIPPED_TRIANGLES)
                    r2_vu1_clipped_indices[r2_vu1_clipped_index_count++] =
                        (meshlet_first_triangle + source_triangle - first_triangle) * 3u;
                else
                    clipped_list_overflow = 1;
                continue;
            }
            const float area = (b->x - a->x) * (c->y - a->y) -
                (b->y - a->y) * (c->x - a->x);
            if (!double_sided && area >= 0.0f) continue;
            const u8 source_indices[3] = {ia, ib, ic};
            for (u32 corner = 0u; corner < 3u; ++corner)
            {
                const u8 local = source_indices[corner];
                if (compact_for_local[local] == 0xffu)
                {
                    compact_for_local[local] = (u8)compact_vertex_count;
                    compact_source[compact_vertex_count++] = local;
                }
                compact_indices[batch_triangles * 3u + corner] =
                    compact_for_local[local];
            }
            ++batch_triangles;
        }
        if (batch_triangles == 0u) continue;
        for (u32 local = 0u; local < compact_vertex_count; ++local)
        {
            const u16 global = meshlet->vertices[compact_source[local]];
            const R2ProjectedVertex *point = &projected[global];
            const R2Ps2MeshVertex *source = &vertices[global];
            u128 *packed = &input[2u + local * 3u];
            float *stq = (float *)&packed[0];
            stq[0] = (source->u * texture_tiling[0] + texture_offset[0]) * point->q;
            stq[1] = (source->v * texture_tiling[1] + texture_offset[1]) * point->q;
            stq[2] = point->q;
            stq[3] = 0.0f;
            u32 *rgbaq = (u32 *)&packed[1];
            rgbaq[0] = (u8)point->red; rgbaq[1] = (u8)point->green;
            rgbaq[2] = (u8)point->blue; rgbaq[3] = (u8)point->alpha;
            r2_pack_gs_xyz((uint32_t *)&packed[2], point->gs_x, point->gs_y,
                point->z, (u8)point->fog, vu_fog_enabled);
        }
        const u32 index_qword_offset = 2u + compact_vertex_count * 3u;
        for (u32 triangle = 0u; triangle < batch_triangles; ++triangle)
        {
            u32 *indices = (u32 *)&input[index_qword_offset + triangle];
            indices[0] = compact_indices[triangle * 3u];
            indices[1] = compact_indices[triangle * 3u + 1u];
            indices[2] = compact_indices[triangle * 3u + 2u];
        }
        u32 *params = (u32 *)&input[0];
        params[0] = batch_triangles;
        params[1] = index_qword_offset;
        const int texture_enabled = texture != NULL ? 1 : 0;
        u64 *giftag = (u64 *)&input[1];
        giftag[0] = VU_GS_GIFTAG(
            batch_triangles * 3u, 1, 1,
            VU_GS_PRIM(PRIM_TRIANGLE, PRIM_SHADE_GOURAUD,
                texture_enabled, vu_fog_enabled, 0, 0,
                PRIM_MAP_ST, platform->gs->PrimContext, PRIM_UNFIXED),
            0, 3);
        giftag[1] = vu_fog_enabled
            ? R2_DRAW_STQ_FOG_REGLIST : DRAW_STQ2_REGLIST;

        packet2_utils_vu_add_unpack_data(packet, 0, input,
            index_qword_offset + batch_triangles, 1);
        packet2_utils_vu_add_start_program(packet, 0);
        ++batch_jobs;
        ++r2_vu1_dispatched_meshlets;
        if (batch_jobs == R2_VU1_DMA_BATCH_JOBS)
        {
            packet2_utils_vu_add_end_tag(packet);
            const clock_t send_start = clock();
            r2_profile_vu_prep_ticks += send_start - prep_start;
            dma_channel_wait(DMA_CHANNEL_VIF1, 0);
            dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);
            prep_start = clock();
            r2_profile_vu_send_ticks += prep_start - send_start;
            r2_vu1_context ^= 1u;
            packet = r2_vu1_packets[r2_vu1_context];
            packet2_reset(packet, 0);
            batch_jobs = 0u;
        }
        }
        meshlet_first_triangle = meshlet_end_triangle;
    }
    if (batch_jobs != 0u)
    {
        packet2_utils_vu_add_end_tag(packet);
        const clock_t send_start = clock();
        r2_profile_vu_prep_ticks += send_start - prep_start;
        dma_channel_wait(DMA_CHANNEL_VIF1, 0);
        dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);
        r2_profile_vu_send_ticks += clock() - send_start;
        r2_vu1_context ^= 1u;
    }
    else
        r2_profile_vu_prep_ticks += clock() - prep_start;
    if (found_clipped_triangle) ++r2_vu1_clipped_objects;
    /* 1 means the VU path completely handled this index range. 2 means that
       one or more boundary triangles still need the EE clipping fallback.
       Keeping those states distinct avoids rescanning every triangle on the
       EE after VU1 has already classified a wholly on-screen object. */
    return clipped_list_overflow ? 3 : (found_clipped_triangle ? 2 : 1);
}

static void shade_projected_vertex(
    const R2Ps2Platform *platform, const R2Ps2MeshVertex *source,
    const R2Ps2SceneMaterial *material, float object_alpha, float inverse_fog_range,
    const float *local_light_direction, const u8 light_lut[3][256],
    const float object_local_light[3],
    const u8 *baked_color, const float *baked_multiplier,
    float color_scale, const float *object_color,
    const float *world_position, const float *world_normal, u32 object_layer_bit,
    R2ProjectedVertex *output)
{
    if (baked_color != NULL && material->lit)
    {
        float red = (float)baked_color[0] * baked_multiplier[0];
        float green = (float)baked_color[1] * baked_multiplier[1];
        float blue = (float)baked_color[2] * baked_multiplier[2];
        output->red = (u8)fmaxf(0.0f, fminf(255.0f, red));
        output->green = (u8)fmaxf(0.0f, fminf(255.0f, green));
        output->blue = (u8)fmaxf(0.0f, fminf(255.0f, blue));
    }
    else
    {
    float diffuse = source->nx * local_light_direction[0] +
        source->ny * local_light_direction[1] + source->nz * local_light_direction[2];
    if (diffuse < 0.0f) diffuse = 0.0f;
    if (diffuse > 1.0f) diffuse = 1.0f;
    const u32 light_index = (u32)(diffuse * 255.0f);
    output->red = light_lut[0][light_index];
    output->green = light_lut[1][light_index];
    output->blue = light_lut[2][light_index];
    if (material->lit == 2u)
    {
        float values[3] = {(float)output->red, (float)output->green, (float)output->blue};
        for (u32 channel = 0; channel < 3u; ++channel)
        {
            values[channel] += color_scale * object_local_light[channel] * object_color[channel];
            if (values[channel] > 255.0f) values[channel] = 255.0f;
        }
        output->red = (u8)values[0]; output->green = (u8)values[1]; output->blue = (u8)values[2];
    }
    else if (material->lit == 1u)
    for (u32 light_index = 0; light_index < platform->scene_local_light_count; ++light_index)
    {
        const R2Ps2LocalLight *light = &platform->scene_local_lights[light_index];
        if ((light->layer_mask & object_layer_bit) == 0u) continue;
        float tx = light->position[0] - world_position[0];
        float ty = light->position[1] - world_position[1];
        float tz = light->position[2] - world_position[2];
        const float distance = sqrtf(tx * tx + ty * ty + tz * tz);
        if (distance <= 0.00001f || distance >= light->range) continue;
        tx /= distance; ty /= distance; tz /= distance;
        float local_diffuse = world_normal[0] * tx + world_normal[1] * ty + world_normal[2] * tz;
        if (local_diffuse <= 0.0f) continue;
        float attenuation = 1.0f - distance / light->range;
        attenuation *= attenuation;
        if (light->type == 1u)
        {
            const float cone = light->direction[0] * -tx +
                light->direction[1] * -ty + light->direction[2] * -tz;
            if (cone < light->spot_cos) continue;
        }
        const float strength = sqrtf(fmaxf(0.0f,
            local_diffuse * attenuation * light->intensity));
        float values[3] = {(float)output->red, (float)output->green, (float)output->blue};
        for (u32 channel = 0; channel < 3u; ++channel)
        {
            values[channel] += color_scale * strength * light->color[channel] * object_color[channel];
            if (values[channel] > 255.0f) values[channel] = 255.0f;
        }
        output->red = (u8)values[0]; output->green = (u8)values[1]; output->blue = (u8)values[2];
    }
    }
    output->alpha = object_alpha;
    output->fog = 255.0f;
    if (material->receive_fog && platform->scene_fog.enabled)
    {
        /* Linear camera-depth fog avoids a square root for every vertex and
           matches the view-space fog convention used by fixed-function PS2
           renderers. Lateral distance should not make a flat wall foggier. */
        float amount = (output->depth - platform->scene_fog.start) * inverse_fog_range;
        output->fog = (1.0f - fmaxf(0.0f, fminf(1.0f, amount))) * 255.0f;
    }
}

static void sample_object_local_lighting(
    const R2Ps2Platform *platform, const float position[3],
    u32 object_layer_bit, float result[3])
{
    result[0] = result[1] = result[2] = 0.0f;
    for (u32 light_index = 0; light_index < platform->scene_local_light_count; ++light_index)
    {
        const R2Ps2LocalLight *light = &platform->scene_local_lights[light_index];
        if ((light->layer_mask & object_layer_bit) == 0u) continue;
        float tx = light->position[0] - position[0];
        float ty = light->position[1] - position[1];
        float tz = light->position[2] - position[2];
        const float distance = sqrtf(tx * tx + ty * ty + tz * tz);
        if (distance <= 0.00001f || distance >= light->range) continue;
        const float inverse_distance = 1.0f / distance;
        tx *= inverse_distance; ty *= inverse_distance; tz *= inverse_distance;
        float attenuation = 1.0f - distance / light->range;
        attenuation *= attenuation;
        if (light->type == 1u)
        {
            const float cone = light->direction[0] * -tx +
                light->direction[1] * -ty + light->direction[2] * -tz;
            if (cone < light->spot_cos) continue;
        }
        const float strength = sqrtf(fmaxf(0.0f, attenuation * light->intensity));
        for (u32 channel = 0; channel < 3u; ++channel)
            result[channel] += strength * light->color[channel];
    }
}

static void draw_mesh_triangle(
    R2Ps2Platform *platform,
    GSTEXTURE *texture,
    const R2Ps2MeshVertex *source_a,
    const R2Ps2MeshVertex *source_b,
    const R2Ps2MeshVertex *source_c,
    const R2Ps2SceneInstance *instance,
    const R2Ps2SceneMaterial *material,
    float phase,
    const float *local_light_direction,
    int force_double_sided,
    const R2ProjectedVertex *a,
    const R2ProjectedVertex *b,
    const R2ProjectedVertex *c,
    float au, float av, float bu, float bv, float cu, float cv)
{
    /* The native camera looks down -Z like the editor. Combined with the
       screen-Y flip, outward front faces have negative signed area. */
    const float area = (b->x - a->x) * (c->y - a->y) -
        (b->y - a->y) * (c->x - a->x);
    const int back_face = area >= 0.0f;
    if (!material->double_sided && !force_double_sided && back_face)
        return;

    const R2ProjectedVertex *points[3] = {a, b, c};
    (void)source_a; (void)source_b; (void)source_c;
    (void)instance; (void)phase; (void)local_light_direction;
    /* Mesh UVs are already normalized. The old call path multiplied each UV
       by the texture dimensions and divided it back here, performing six
       costly EE floating-point divisions for every visible triangle. */
    const float u[3] = {au, bu, cu};
    const float v[3] = {av, bv, cv};
    GSPRIMSTQPOINT vertices[3];
    const int fog_enabled = material->receive_fog && platform->scene_fog.enabled;
    memset(vertices, 0, sizeof(vertices));
    for (int index = 0; index < 3; ++index)
    {
        vertices[index].rgbaq.color.components.r = (u8)points[index]->red;
        vertices[index].rgbaq.color.components.g = (u8)points[index]->green;
        vertices[index].rgbaq.color.components.b = (u8)points[index]->blue;
        vertices[index].rgbaq.color.components.a = (u8)points[index]->alpha;
        vertices[index].rgbaq.color.q = points[index]->q;
        vertices[index].rgbaq.tag = GS_RGBAQ;
        vertices[index].stq.st.s = u[index] * points[index]->q;
        vertices[index].stq.st.t = v[index] * points[index]->q;
        vertices[index].stq.tag = GS_ST;
        vertices[index].xyz2.xyz.x = points[index]->gs_x;
        vertices[index].xyz2.xyz.y = points[index]->gs_y;
        vertices[index].xyz2.xyz.z = (u32)points[index]->z;
        vertices[index].xyz2.tag = GS_XYZ2;
        if (fog_enabled)
        {
            /* A+D XYZF2: retain all 24 depth bits; F occupies bits 56..63.
               GS fog affects RGB only, preserving the texture alpha test. */
            vertices[index].xyz2.xyz.xyz = GS_SETREG_XYZF2(
                vertices[index].xyz2.xyz.x, vertices[index].xyz2.xyz.y,
                (u32)points[index]->z & 0x00ffffffu,
                (u8)points[index]->fog);
            vertices[index].xyz2.tag = GS_XYZF2;
        }
    }
    /* gsKit also uses PrimAlphaEnable as TEX0.TCC for its textured primitive.
       Cutout therefore needs it enabled so texture alpha reaches ATEST; fully
       opaque texels use GS alpha 128 and remain visually opaque. */
    platform->gs->PrimAlphaEnable = material->surface_mode != 0u
        ? GS_SETTING_ON : GS_SETTING_OFF;
    platform->gs->PrimFogEnable = fog_enabled ? GS_SETTING_ON : GS_SETTING_OFF;
    R2WorldTriangleBatch *batch = &world_triangle_batch;
    if (batch->point_count > 0u && batch->texture != texture)
        flush_world_triangle_batch(platform);
    batch->texture = texture;
    if (texture != NULL)
    {
        for (int index = 0; index < 3; ++index)
            batch->textured[batch->point_count + index] = vertices[index];
    }
    else
    {
        for (int index = 0; index < 3; ++index)
        {
            batch->plain[batch->point_count + index].rgbaq = vertices[index].rgbaq;
            batch->plain[batch->point_count + index].xyz2 = vertices[index].xyz2;
        }
    }
    batch->point_count += 3u;
    if (batch->point_count >= R2_WORLD_BATCH_TRIANGLES * 3u)
        flush_world_triangle_batch(platform);
}

typedef struct R2TransparentOrder
{
    u32 index;
    float distance_squared;
} R2TransparentOrder;

static R2TransparentOrder transparent_order[4096];

static const float *sample_animation_state(
    const R2Ps2MeshResource *mesh, u32 state_index, float time,
    u32 vertex, float output[6])
{
    const R2Ps2AnimationState *state = &mesh->animation_states[state_index];
    float frame_position = fmaxf(0, fminf(time * state->fps, (float)(state->frame_count - 1u)));
    const u32 frame_a = (u32)frame_position;
    const u32 frame_b = frame_a + 1u < state->frame_count ? frame_a + 1u : frame_a;
    const float blend = frame_position - (float)frame_a;
    const u32 frame_stride = mesh->vertex_count * 6u;
    const float *a = mesh->animation_frames + state->float_offset + frame_a * frame_stride + vertex * 6u;
    const float *b = mesh->animation_frames + state->float_offset + frame_b * frame_stride + vertex * 6u;
    for (u32 value = 0; value < 6u; ++value)
        output[value] = a[value] + (b[value] - a[value]) * blend;
    return output;
}

static void update_mesh_animation(R2Ps2MeshResource *mesh, const R2Ps2Playback *p)
{
    if (mesh->animation_frames == NULL || p->state >= mesh->animation_state_count || mesh->animated_vertices == NULL)
        return;
    if (mesh->animation_bone_count > 0u && mesh->animation_skin != NULL)
    {
        /* Keep the original target-FPU division result for every byte weight,
           but compute it once rather than converting/dividing in the vertex
           loop. No reciprocal approximation or blend reordering is needed. */
        static float weight_lut[256];
        static int weight_lut_ready;
        if (!weight_lut_ready)
        {
            for (u32 weight = 0; weight < 256u; ++weight)
                weight_lut[weight] = (float)weight / 255.0f;
            weight_lut_ready = 1;
        }
        float matrices[128u * 12u];
        const u32 current_state = p->state;
        const u32 previous_state = p->previous < mesh->animation_state_count ? p->previous : p->state;
        const R2Ps2AnimationState *current = &mesh->animation_states[current_state];
        const R2Ps2AnimationState *previous = &mesh->animation_states[previous_state];
        float current_position = fmaxf(0, fminf(p->time * current->fps, (float)(current->frame_count - 1u)));
        float previous_position = fmaxf(0, fminf(p->previous_time * previous->fps, (float)(previous->frame_count - 1u)));
        const u32 current_a = (u32)current_position;
        const u32 current_b = current_a + 1u < current->frame_count ? current_a + 1u : current_a;
        const u32 previous_a = (u32)previous_position;
        const u32 previous_b = previous_a + 1u < previous->frame_count ? previous_a + 1u : previous_a;
        const float current_mix = current_position - (float)current_a;
        const float previous_mix = previous_position - (float)previous_a;
        const u32 frame_stride = mesh->animation_bone_count * 12u;
        for (u32 bone = 0; bone < mesh->animation_bone_count; ++bone)
        {
            const float *ca = mesh->animation_frames + current->float_offset + current_a * frame_stride + bone * 12u;
            const float *cb = mesh->animation_frames + current->float_offset + current_b * frame_stride + bone * 12u;
            const float *pa = mesh->animation_frames + previous->float_offset + previous_a * frame_stride + bone * 12u;
            const float *pb = mesh->animation_frames + previous->float_offset + previous_b * frame_stride + bone * 12u;
            for (u32 component = 0; component < 12u; ++component)
            {
                const float current_value = ca[component] + (cb[component] - ca[component]) * current_mix;
                const float previous_value = pa[component] + (pb[component] - pa[component]) * previous_mix;
                matrices[bone * 12u + component] = previous_value +
                    (current_value - previous_value) * p->blend;
            }
        }
        for (u32 vertex = 0; vertex < mesh->vertex_count; ++vertex)
        {
            const R2Ps2MeshVertex *source = &mesh->vertices[vertex];
            R2Ps2MeshVertex *output = &mesh->animated_vertices[vertex];
            output->u = source->u;
            output->v = source->v;
            float x = 0, y = 0, z = 0, nx = 0, ny = 0, nz = 0;
            const u8 *influences = mesh->animation_skin + vertex * 8u;
            if (influences[5] == 0u && influences[6] == 0u && influences[7] == 0u)
            {
                /* Most character vertices are rigidly attached to one bone.
                   Avoid the generic four-weight loop and all weight multiplies
                   for that common case. Exported weights sum to 255. */
                const float *m = matrices + influences[0] * 12u;
                output->x = source->x * m[0] + source->y * m[3] +
                    source->z * m[6] + m[9];
                output->y = source->x * m[1] + source->y * m[4] +
                    source->z * m[7] + m[10];
                output->z = source->x * m[2] + source->y * m[5] +
                    source->z * m[8] + m[11];
                output->nx = source->nx * m[0] + source->ny * m[3] + source->nz * m[6];
                output->ny = source->nx * m[1] + source->ny * m[4] + source->nz * m[7];
                output->nz = source->nx * m[2] + source->ny * m[5] + source->nz * m[8];
                continue;
            }
            for (u32 influence = 0; influence < 4u; ++influence)
            {
                const u32 byte_weight = influences[4u + influence];
                if (byte_weight == 0u) continue;
                const float weight = weight_lut[byte_weight];
                const float *m = matrices + influences[influence] * 12u;
                x += (source->x * m[0] + source->y * m[3] + source->z * m[6] + m[9]) * weight;
                y += (source->x * m[1] + source->y * m[4] + source->z * m[7] + m[10]) * weight;
                z += (source->x * m[2] + source->y * m[5] + source->z * m[8] + m[11]) * weight;
                nx += (source->nx * m[0] + source->ny * m[3] + source->nz * m[6]) * weight;
                ny += (source->nx * m[1] + source->ny * m[4] + source->nz * m[7]) * weight;
                nz += (source->nx * m[2] + source->ny * m[5] + source->nz * m[8]) * weight;
            }
            output->x = x; output->y = y; output->z = z;
            /* A rigid or near-rigid blend of rotation matrices already leaves
               the normal at unit length. Avoid a sqrt and three divides for
               those common vertices; only visibly shortened/lengthened blend
               normals require correction. */
            const float length_squared = nx * nx + ny * ny + nz * nz;
            if (length_squared > 0.0000001f &&
                (length_squared < 0.9025f || length_squared > 1.1025f))
            {
                const float inverse_length = 1.0f / sqrtf(length_squared);
                nx *= inverse_length; ny *= inverse_length; nz *= inverse_length;
            }
            output->nx = nx; output->ny = ny; output->nz = nz;
        }
        return;
    }
    for (u32 vertex = 0; vertex < mesh->vertex_count; ++vertex)
    {
        float current[6], previous[6];
        sample_animation_state(mesh, p->state, p->time, vertex, current);
        sample_animation_state(mesh, p->previous < mesh->animation_state_count ? p->previous : p->state, p->previous_time, vertex, previous);
        const float state_blend = p->blend;
        float value[6];
        for (int component = 0; component < 6; ++component)
            value[component] = previous[component] + (current[component] - previous[component]) * state_blend;
        R2Ps2MeshVertex *output = &mesh->animated_vertices[vertex];
        output->x = value[0]; output->y = value[1]; output->z = value[2];
        float nx = value[3], ny = value[4], nz = value[5];
        const float length = sqrtf(nx * nx + ny * ny + nz * nz);
        if (length > 0.00001f) { nx /= length; ny /= length; nz /= length; }
        output->nx = nx; output->ny = ny; output->nz = nz;
    }
}

void r2_ps2_draw_mesh_probe(R2Ps2Platform *platform, float phase)
{
    static u32 profile_frames;
    static clock_t profile_skin_ticks;
    static clock_t profile_vertex_ticks;
    static clock_t profile_triangle_ticks;
    static clock_t profile_total_ticks;
    const clock_t profile_total_start = clock();
    clock_t frame_skin_ticks = 0;
    clock_t frame_vertex_ticks = 0;
    clock_t frame_triangle_ticks = 0;
    if (platform == NULL)
        return;
    /* GS WMS/WMT mode 0 repeats normalized ST coordinates. Without this,
       material tiling stretches the outermost texel because gsKit leaves the
       context in clamp mode after UI rendering. */
    set_texture_wrap_mode(platform, 0u);
    // A successfully loaded UI-only scene must not draw the old probe fallback.
    if (platform->external_scene_loaded && platform->scene_instance_count == 0u)
        return;
    if (platform->scene_fog.enabled)
    {
        u64 *packet = gsKit_heap_alloc(platform->gs, 1, 16, GIF_AD);
        *packet++ = GIF_TAG_AD(1);
        *packet++ = GIF_AD;
        *packet++ = (u64)(u8)(platform->scene_fog.color[0] * 255.0f) |
            ((u64)(u8)(platform->scene_fog.color[1] * 255.0f) << 8) |
            ((u64)(u8)(platform->scene_fog.color[2] * 255.0f) << 16);
        *packet++ = GS_FOGCOL;
    }
    const float clip_vertical = tanf(
        platform->scene_camera.field_of_view * 0.0174532925f * 0.5f);
    const float clip_horizontal = clip_vertical *
        platform->scene_camera.aspect;
    /* Scene loading validates end > start. The range is constant for every
       object/vertex in this draw, so do not divide by it in the hot loop. */
    const float inverse_fog_range = platform->scene_fog.enabled
        ? 1.0f / (platform->scene_fog.end - platform->scene_fog.start) : 0.0f;


    R2Ps2SceneInstance fallback = {0, 0, {0,0,0}, {0,0,0}, {1,1,1}, {1,1,1,1}};
    R2Ps2SceneMaterial fallback_material = {1, 0, 0, 1, 0.5f};
    const u32 instance_count = platform->scene_instance_count == 0 ? 1 : platform->scene_instance_count;
    const u32 draw_count = platform->scene_draw_count != 0u
        ? platform->scene_draw_count : instance_count;
    u32 transparent_count = 0;
    for (u32 draw_index = 0; draw_index < draw_count; ++draw_index)
    {
        const R2Ps2SceneDraw *draw = platform->scene_draw_count != 0u
            ? &platform->scene_draws[draw_index] : NULL;
        const u32 object = draw != NULL ? draw->source_instance : draw_index;
        if (platform->scene_instance_count != 0 &&
            platform->scene_persistent_objects != NULL &&
            platform->scene_persistent_objects[object].enabled &&
            !platform->scene_persistent_objects[object].active)
            continue;
        const R2Ps2SceneInstance *instance = platform->scene_instance_count == 0
            ? &fallback : &platform->scene_instances[object];
        const R2Ps2SceneMaterial *material = draw != NULL ? &draw->material :
            platform->scene_instance_count == 0 ? &fallback_material : &platform->scene_materials[object];
        if (material->surface_mode != 2u)
            continue;
        const float dx = instance->position[0] - platform->scene_camera.position[0];
        const float dy = instance->position[1] - platform->scene_camera.position[1];
        const float dz = instance->position[2] - platform->scene_camera.position[2];
        R2TransparentOrder entry = {draw_index, dx * dx + dy * dy + dz * dz};
        u32 destination = transparent_count;
        while (destination > 0 &&
               transparent_order[destination - 1].distance_squared < entry.distance_squared)
        {
            transparent_order[destination] = transparent_order[destination - 1];
            --destination;
        }
        transparent_order[destination] = entry;
        ++transparent_count;
    }

    /* Draw opaque geometry first, alpha-tested cutouts second, and blended
       geometry last. Keeping cutouts in their own pass prevents a later
       opaque terrain draw from replacing surviving character pixels on the
       GS while retaining normal depth occlusion against the terrain. */
    for (u32 surface_pass = 0; surface_pass < 3; ++surface_pass)
    {
    if (surface_pass == 2)
    {
        gsKit_set_primalpha(platform->gs, GS_BLEND_BACK2FRONT, 0);
    }
    const u32 pass_count = surface_pass < 2 ? draw_count : transparent_count;
    for (u32 pass_index = 0; pass_index < pass_count; ++pass_index)
    {
        const u32 draw_index = surface_pass < 2
            ? pass_index : transparent_order[pass_index].index;
        const R2Ps2SceneDraw *draw = platform->scene_draw_count != 0u
            ? &platform->scene_draws[draw_index] : NULL;
        const u32 object = draw != NULL ? draw->source_instance : draw_index;
        if (platform->scene_instance_count != 0 &&
            platform->scene_persistent_objects != NULL &&
            platform->scene_persistent_objects[object].enabled &&
            !platform->scene_persistent_objects[object].active)
            continue;
        const R2Ps2SceneInstance *source_instance = platform->scene_instance_count == 0
            ? &fallback : &platform->scene_instances[object];
        R2Ps2SceneInstance draw_instance;
        const R2Ps2SceneInstance *instance = source_instance;
        if (draw != NULL)
        {
            draw_instance = *source_instance;
            memcpy(draw_instance.color, draw->color, sizeof(draw_instance.color));
            instance = &draw_instance;
        }
        const R2Ps2SceneMaterial *material = draw != NULL ? &draw->material :
            platform->scene_instance_count == 0 ? &fallback_material : &platform->scene_materials[object];
        const float texture_tiling[2] = {draw != NULL ? draw->texture_tiling[0] : 1.0f,
            draw != NULL ? draw->texture_tiling[1] : 1.0f};
        const float texture_offset[2] = {draw != NULL ? draw->texture_offset[0] : 0.0f,
            draw != NULL ? draw->texture_offset[1] : 0.0f};
        if ((surface_pass == 0 && material->surface_mode != 0u) ||
            (surface_pass == 1 && material->surface_mode != 1u) ||
            (surface_pass == 2 && material->surface_mode != 2u))
            continue;

        if (material->surface_mode == 1u)
        {
            float cutoff = material->alpha_cutoff;
            if (cutoff < 0.0f) cutoff = 0.0f;
            if (cutoff > 1.0f) cutoff = 1.0f;
            platform->gs->Test->ATST = 6;  /* greater than AREF */
            platform->gs->Test->AREF = (u8)(cutoff * 128.0f);
            platform->gs->Test->AFAIL = 0; /* discard framebuffer and depth */
            gsKit_set_test(platform->gs, GS_ATEST_ON);
            /* gsKit uses PrimAlphaEnable for TEX0.TCC as well as PRIM.ABE.
               Cutout needs TCC for alpha testing, but its surviving pixels
               must replace the framebuffer rather than inherit a previous
               fog/transparent source-over equation. */
            gsKit_set_primalpha(
                platform->gs,
                GS_SETREG_ALPHA(0, 2, 2, 2, 128),
                0);
        }
        else
        {
            gsKit_set_test(platform->gs, GS_ATEST_OFF);
        }
        R2Ps2MeshVertex *mesh_vertices = platform->mesh_vertices;
        u16 *mesh_indices = platform->mesh_indices;
        R2ProjectedVertex *projected = platform->mesh_projected;
        u32 mesh_vertex_count = platform->mesh_vertex_count;
        u32 mesh_index_count = platform->mesh_index_count;
        u32 mesh_index_start = 0u;
        GSTEXTURE *texture = platform->texture_ready ? &platform->texture : NULL;
        R2Ps2MeshResource *mesh_resource = NULL;
        int force_double_sided = 0;
        if (platform->external_scene_loaded)
        {
            const u32 mesh_slot = draw != NULL ? draw->mesh_slot : instance->mesh_slot;
            if (mesh_slot >= platform->scene_mesh_count ||
                !platform->scene_meshes[mesh_slot].ready) continue;
            R2Ps2MeshResource *mesh = &platform->scene_meshes[mesh_slot];
            mesh_resource = mesh;
            mesh_vertices = mesh->vertices;
            if ((platform->playback[object].flags & 8u) && mesh->animated_vertices != NULL)
            {
                const clock_t skin_start = clock();
                update_mesh_animation(mesh, &platform->playback[object]);
                frame_skin_ticks += clock() - skin_start;
                mesh_vertices = mesh->animated_vertices;
            }
            mesh_indices = mesh->indices;
            projected = mesh->projected;
            mesh_vertex_count = mesh->vertex_count;
            mesh_index_count = mesh->index_count;
            if (draw != NULL)
            {
                if (draw->index_start > mesh_index_count ||
                    draw->index_count > mesh_index_count - draw->index_start)
                    continue;
                mesh_index_start = draw->index_start;
                mesh_index_count = draw->index_count;
                mesh_indices += mesh_index_start;
            }
            force_double_sided = mesh->animation_bone_count > 0u;
            texture = NULL;
            const u32 texture_slot = draw != NULL ? draw->texture_slot : instance->texture_slot;
            if (texture_slot != 0xffffffffu &&
                texture_slot < platform->scene_texture_count &&
                platform->scene_textures[texture_slot].ready)
                texture = &platform->scene_textures[texture_slot].texture;
        }
        if (mesh_vertices == NULL || mesh_indices == NULL || projected == NULL) continue;

        /* R^-1 * worldLight lets each authored object-space normal be lit with
           one dot product. This used to rebuild the rotation and run six trig
           functions for every triangle corner. */
        const float degrees = 0.0174532925f;
        const float sx = sinf(instance->rotation[0] * degrees);
        const float cx = cosf(instance->rotation[0] * degrees);
        const float sy = sinf(instance->rotation[1] * degrees);
        const float cy = cosf(instance->rotation[1] * degrees);
        const float sz = sinf(instance->rotation[2] * degrees);
        const float cz = cosf(instance->rotation[2] * degrees);
        const float object_rotation[6] = {sx, cx, sy, cy, sz, cz};
        const float camera_yaw = -platform->scene_camera.rotation[1] * degrees;
        const float camera_pitch = -platform->scene_camera.rotation[0] * degrees;
        const float camera_roll = -platform->scene_camera.rotation[2] * degrees;
        const float camera_rotation[6] = {
            sinf(camera_yaw), cosf(camera_yaw),
            sinf(camera_pitch), cosf(camera_pitch),
            sinf(camera_roll), cosf(camera_roll)
        };
        const float focal = ((float)platform->gs->Height * 0.5f) /
            tanf(platform->scene_camera.field_of_view * degrees * 0.5f);
        const float *world_light = platform->scene_lighting.directional_direction;
        float local_light_direction[3];
        float light_x = cz * world_light[0] + sz * world_light[1];
        float light_y = -sz * world_light[0] + cz * world_light[1];
        float light_z = world_light[2];
        float temporary_light = cy * light_x - sy * light_z;
        light_z = sy * light_x + cy * light_z;
        light_x = temporary_light;
        temporary_light = cx * light_y + sx * light_z;
        light_z = -sx * light_y + cx * light_z;
        light_y = temporary_light;
        const float light_length = sqrtf(light_x * light_x +
            light_y * light_y + light_z * light_z);
        const float inverse_light_length = light_length > 0.00001f
            ? 1.0f / light_length : 1.0f;
        local_light_direction[0] = light_x * inverse_light_length;
        local_light_direction[1] = light_y * inverse_light_length;
        local_light_direction[2] = light_z * inverse_light_length;

        /* GS color is 8-bit, so calculate the nonlinear lighting curve once
           for each possible quantized diffuse value instead of running three
           square roots for every transformed vertex. */
        u8 light_lut[3][256];
        const float color_scale = texture != NULL ? 128.0f : 255.0f;
        const float baked_multiplier[3] = {
            color_scale * instance->color[0] * (1.0f / 255.0f),
            color_scale * instance->color[1] * (1.0f / 255.0f),
            color_scale * instance->color[2] * (1.0f / 255.0f)};
        const u8 *baked_colors = platform->scene_baked_colors != NULL &&
            platform->scene_baked_color_counts[object] == mesh_vertex_count
            ? platform->scene_baked_colors[object] : NULL;
        const int needs_world_lighting = baked_colors == NULL && material->lit == 1u &&
            platform->scene_local_light_count != 0u;
        const u32 object_layer_bit = 1u << platform->scene_instance_layers[object];
        float object_local_light[3] = {0.0f, 0.0f, 0.0f};
        if (baked_colors == NULL && material->lit == 2u)
            sample_object_local_lighting(platform, instance->position,
                object_layer_bit, object_local_light);
        /* Baked-lit vertices take their RGB directly from scene_baked_colors in
           shade_projected_vertex(). Building the 3x256 nonlinear light table
           for those objects was pure per-frame work (including 768 sqrtf
           calls per object) and the result was never read. */
        const int needs_light_lut = baked_colors == NULL || !material->lit;
        if (needs_light_lut)
        for (u32 light_index = 0; light_index < 256u; ++light_index)
        {
            const float diffuse = (float)light_index * (1.0f / 255.0f);
            const float directional = material->lit &&
                platform->scene_lighting.directional_enabled && baked_colors == NULL &&
                (platform->scene_directional_layer_mask & object_layer_bit) != 0u
                ? diffuse * platform->scene_lighting.directional_intensity : 0.0f;
            for (u32 channel = 0; channel < 3u; ++channel)
            {
                const float light = (material->lit
                    ? platform->scene_lighting.ambient[channel] : 1.0f) +
                    directional * platform->scene_lighting.directional_color[channel];
                float value = color_scale * sqrtf(fmaxf(0.0f, light)) *
                    instance->color[channel];
                if (value < 0.0f) value = 0.0f;
                if (value > 255.0f) value = 255.0f;
                light_lut[channel][light_index] = (u8)value;
            }
        }

        const clock_t vertex_start = clock();
        const float object_alpha = fmaxf(0.0f, fminf(1.0f, instance->color[3])) * 128.0f;
        const float relative_position[3] = {
            instance->position[0] - platform->scene_camera.position[0],
            instance->position[1] - platform->scene_camera.position[1],
            instance->position[2] - platform->scene_camera.position[2]};
        float camera_transform[12];
        r2_build_camera_transform(camera_transform, instance->scale,
            object_rotation, camera_rotation, relative_position);
        u32 shade_first = mesh_vertex_count;
        u32 shade_last = 0u;
        for (u32 corner = 0; corner < mesh_index_count; ++corner)
        {
            const u32 vertex = mesh_indices[corner];
            if (vertex < shade_first) shade_first = vertex;
            if (vertex > shade_last) shade_last = vertex;
        }
        /* A hierarchy child can select one range from a shared OBJ. Project
           only that range as well as shading it, otherwise every wall child
           would repeat the complete room's vertex transform. OBJ sections
           are emitted in compact ranges, avoiding a large mark table. */
        if (shade_first < mesh_vertex_count)
        for (u32 vertex = shade_first; vertex <= shade_last; ++vertex)
        {
            const R2Ps2MeshVertex *source_vertex = &mesh_vertices[vertex];
            projected[vertex] = project_mesh_vertex(
                platform, source_vertex, camera_transform, focal);
            projected[vertex].outcode = clip_outcode(platform, &projected[vertex],
                clip_horizontal, clip_vertical);
            projected[vertex].gs_x = (u16)gsKit_float_to_int_x(
                platform->gs, projected[vertex].x);
            projected[vertex].gs_y = (u16)gsKit_float_to_int_y(
                platform->gs, projected[vertex].y);
        }
        /* Exact section-level frustum rejection. If every referenced vertex
           lies outside the same clip plane, none of this child's triangles
           can be visible. This skips material shading, meshlet packing and
           triangle processing for off-screen walls and beams. */
        u32 section_common_outcode = 0x3fu;
        for (u32 corner = 0; corner < mesh_index_count; ++corner)
            section_common_outcode &= projected[mesh_indices[corner]].outcode;
        if (section_common_outcode != 0u)
        {
            frame_vertex_ticks += clock() - vertex_start;
            continue;
        }
        if (shade_first < mesh_vertex_count)
        for (u32 vertex = shade_first; vertex <= shade_last; ++vertex)
        {
            const R2Ps2MeshVertex *source_vertex = &mesh_vertices[vertex];
            float px = 0.0f, py = 0.0f, pz = 0.0f;
            float nx = source_vertex->nx, ny = source_vertex->ny, nz = source_vertex->nz;
            if (needs_world_lighting)
            {
                px = source_vertex->x * instance->scale[0];
                py = source_vertex->y * instance->scale[1];
                pz = source_vertex->z * instance->scale[2];
                float next_y = cx * py - sx * pz, next_z = sx * py + cx * pz;
                py = next_y; pz = next_z;
                next_y = cx * ny - sx * nz; next_z = sx * ny + cx * nz;
                ny = next_y; nz = next_z;
                float next_x = cy * px + sy * pz; next_z = -sy * px + cy * pz;
                px = next_x; pz = next_z;
                next_x = cy * nx + sy * nz; next_z = -sy * nx + cy * nz;
                nx = next_x; nz = next_z;
                next_x = cz * px - sz * py; next_y = sz * px + cz * py;
                px = next_x; py = next_y;
                next_x = cz * nx - sz * ny; next_y = sz * nx + cz * ny;
                nx = next_x; ny = next_y;
            }
            const float world_position[3] = {px + instance->position[0], py + instance->position[1], pz + instance->position[2]};
            const float world_normal[3] = {nx, ny, nz};
            shade_projected_vertex(platform, source_vertex,
                material, object_alpha, inverse_fog_range,
                local_light_direction, light_lut, object_local_light,
                baked_colors != NULL ? baked_colors + vertex * 3u : NULL,
                baked_multiplier, color_scale, instance->color,
                world_position, world_normal,
                object_layer_bit, &projected[vertex]);
        }
        frame_vertex_ticks += clock() - vertex_start;
        int vu_submitted = 0;
        /* The VU1 meshlet stream is authored for the complete index buffer.
           Partial material/submesh ranges can begin or end inside a meshlet;
           PCSX2 tolerated those partial packets, but real hardware can stall
           VIF1 on them. Keep full meshes accelerated and route subranges
           through the hardware-proven CPU triangle path. */
        if (mesh_resource != NULL && material->surface_mode == 0u &&
            mesh_index_start == 0u && mesh_index_count == mesh_resource->index_count)
        {
            ++r2_vu1_eligible_objects;
            vu_submitted = draw_vu1_meshlets(platform, mesh_resource, mesh_vertices,
                projected, texture,
                material->receive_fog && platform->scene_fog.enabled,
                mesh_index_start, mesh_index_count,
                texture_tiling, texture_offset,
                material->double_sided || force_double_sided);
        }
        if (vu_submitted >= 2)
            r2_vu1_wait_submission();
        const clock_t triangle_start = clock();
        /* A return value of one means every triangle was either submitted or
           trivially rejected by the meshlet pass. Only revisit the indices
           for the CPU fallback, or when VU1 found a clip-boundary triangle. */
        const u32 cpu_triangle_count = vu_submitted == 1 ? 0u :
            (vu_submitted == 2 ? r2_vu1_clipped_index_count : mesh_index_count / 3u);
        for (u32 cpu_triangle = 0; cpu_triangle < cpu_triangle_count; ++cpu_triangle)
        {
        const u32 index = vu_submitted == 2
            ? r2_vu1_clipped_indices[cpu_triangle] : cpu_triangle * 3u;
        const u16 ia = mesh_indices[index];
        const u16 ib = mesh_indices[index + 1];
        const u16 ic = mesh_indices[index + 2];
        R2Ps2MeshVertex *source_a = &mesh_vertices[ia];
        R2Ps2MeshVertex *source_b = &mesh_vertices[ib];
        R2Ps2MeshVertex *source_c = &mesh_vertices[ic];
        R2ProjectedVertex *projected_a = &projected[ia];
        R2ProjectedVertex *projected_b = &projected[ib];
        R2ProjectedVertex *projected_c = &projected[ic];
        const u32 outcode_a = projected_a->outcode;
        const u32 outcode_b = projected_b->outcode;
        const u32 outcode_c = projected_c->outcode;
        const u32 combined_outcode = outcode_a | outcode_b | outcode_c;
        if ((outcode_a & outcode_b & outcode_c) != 0u)
            continue;
        /* Fully visible triangles were already submitted in bulk by VU1.
           Keep only boundary triangles on the CPU clipping path. */
        if (vu_submitted != 0 && combined_outcode == 0u)
            continue;
        if (combined_outcode == 0u)
        {
            draw_mesh_triangle(platform, texture, source_a, source_b, source_c,
                instance, material, phase,
                local_light_direction, force_double_sided,
                projected_a, projected_b, projected_c,
                texture == NULL ? 0.0f : source_a->u * texture_tiling[0] + texture_offset[0],
                texture == NULL ? 0.0f : source_a->v * texture_tiling[1] + texture_offset[1],
                texture == NULL ? 0.0f : source_b->u * texture_tiling[0] + texture_offset[0],
                texture == NULL ? 0.0f : source_b->v * texture_tiling[1] + texture_offset[1],
                texture == NULL ? 0.0f : source_c->u * texture_tiling[0] + texture_offset[0],
                texture == NULL ? 0.0f : source_c->v * texture_tiling[1] + texture_offset[1]);
            continue;
        }

        /* Only triangles that actually intersect a clip plane pay for the
           temporary polygon copies required by Sutherland-Hodgman clipping. */
        R2Ps2MeshVertex input_vertices[3] = {*source_a, *source_b, *source_c};
        R2ProjectedVertex input_projected[3] = {
            *projected_a, *projected_b, *projected_c};
        R2Ps2MeshVertex clip_vertices_a[16];
        R2Ps2MeshVertex clip_vertices_b[16];
        R2ProjectedVertex clip_projected_a[16];
        R2ProjectedVertex clip_projected_b[16];
        memcpy(clip_vertices_a, input_vertices, sizeof(input_vertices));
        memcpy(clip_projected_a, input_projected, sizeof(input_projected));
        int clipped_count = 3;
        for (int plane = 0; plane < 6 && clipped_count >= 3; ++plane)
        {
            if ((combined_outcode & (1u << plane)) == 0u)
                continue;
            int inside_count = 0;
            for (int vertex = 0; vertex < clipped_count; ++vertex)
                inside_count += clip_plane_distance(platform,
                    &clip_projected_a[vertex], plane,
                    clip_horizontal, clip_vertical) >= 0.0f;
            if (inside_count == clipped_count)
                continue;
            if (inside_count == 0)
            {
                clipped_count = 0;
                break;
            }
            clipped_count = clip_polygon_plane(platform,
                clip_vertices_a, clip_projected_a, clipped_count,
                clip_vertices_b, clip_projected_b, plane,
                clip_horizontal, clip_vertical);
            memcpy(clip_vertices_a, clip_vertices_b,
                (size_t)clipped_count * sizeof(R2Ps2MeshVertex));
            memcpy(clip_projected_a, clip_projected_b,
                (size_t)clipped_count * sizeof(R2ProjectedVertex));
        }

        for (int triangle = 1; triangle + 1 < clipped_count; ++triangle)
        {
            R2Ps2MeshVertex *a = &clip_vertices_a[0];
            R2Ps2MeshVertex *b = &clip_vertices_a[triangle];
            R2Ps2MeshVertex *c = &clip_vertices_a[triangle + 1];
            R2ProjectedVertex *pa = &clip_projected_a[0];
            R2ProjectedVertex *pb = &clip_projected_a[triangle];
            R2ProjectedVertex *pc = &clip_projected_a[triangle + 1];
            /* Clipping creates new projected vertices after the unique-vertex
               packing pass, so pack only those rare generated vertices here. */
            pa->gs_x = (u16)gsKit_float_to_int_x(platform->gs, pa->x);
            pa->gs_y = (u16)gsKit_float_to_int_y(platform->gs, pa->y);
            pb->gs_x = (u16)gsKit_float_to_int_x(platform->gs, pb->x);
            pb->gs_y = (u16)gsKit_float_to_int_y(platform->gs, pb->y);
            pc->gs_x = (u16)gsKit_float_to_int_x(platform->gs, pc->x);
            pc->gs_y = (u16)gsKit_float_to_int_y(platform->gs, pc->y);
            draw_mesh_triangle(platform, texture, a, b, c, instance, material, phase,
                local_light_direction, force_double_sided,
                pa, pb, pc,
                texture == NULL ? 0.0f : a->u * texture_tiling[0] + texture_offset[0],
                texture == NULL ? 0.0f : a->v * texture_tiling[1] + texture_offset[1],
                texture == NULL ? 0.0f : b->u * texture_tiling[0] + texture_offset[0],
                texture == NULL ? 0.0f : b->v * texture_tiling[1] + texture_offset[1],
                texture == NULL ? 0.0f : c->u * texture_tiling[0] + texture_offset[0],
                texture == NULL ? 0.0f : c->v * texture_tiling[1] + texture_offset[1]);
        }
        }

        /* Preserve material/fog state until all deferred triangles for this
           instance have been encoded into the GS queue. */
        flush_world_triangle_batch(platform);
        frame_triangle_ticks += clock() - triangle_start;

        gsKit_set_test(platform->gs, GS_ATEST_OFF);
        platform->gs->PrimAlphaEnable = GS_SETTING_OFF;
        platform->gs->PrimFogEnable = GS_SETTING_OFF;
    }
    }
    profile_skin_ticks += frame_skin_ticks;
    profile_vertex_ticks += frame_vertex_ticks;
    profile_triangle_ticks += frame_triangle_ticks;
    profile_total_ticks += clock() - profile_total_start;
    if (++profile_frames >= 120u)
    {
        const float tick_to_ms = 1000.0f / ((float)CLOCKS_PER_SEC * (float)profile_frames);
        r2_profile_skin_ms = (float)profile_skin_ticks * tick_to_ms;
        r2_profile_vertex_ms = (float)profile_vertex_ticks * tick_to_ms;
        r2_profile_triangle_ms = (float)profile_triangle_ticks * tick_to_ms;
        r2_profile_render_ms = (float)profile_total_ticks * tick_to_ms;
        r2_profile_vu_prep_ms = (float)r2_profile_vu_prep_ticks * tick_to_ms;
        r2_profile_vu_send_ms = (float)r2_profile_vu_send_ticks * tick_to_ms;
        r2_profile_vu_setup_ms = (float)r2_profile_vu_setup_ticks * tick_to_ms;
        r2_profile_ready = 1;
        printf("R2PROFILE skin=%.2fms vertex=%.2fms triangle=%.2fms render=%.2fms\n",
            r2_profile_skin_ms, r2_profile_vertex_ms,
            r2_profile_triangle_ms, r2_profile_render_ms);
        printf("R2VUPROFILE prep=%.2fms send_wait=%.2fms setup=%.2fms\n",
            r2_profile_vu_prep_ms, r2_profile_vu_send_ms, r2_profile_vu_setup_ms);
        profile_frames = 0u;
        profile_skin_ticks = profile_vertex_ticks = 0;
        profile_triangle_ticks = profile_total_ticks = 0;
        r2_profile_vu_prep_ticks = r2_profile_vu_send_ticks = r2_profile_vu_setup_ticks = 0;
    }
}

void r2_ps2_end_frame(R2Ps2Platform *platform)
{
    /* World batches execute before this point. Submit the deferred UI to the
       same draw buffer before presenting it; flipping first placed UI alone
       into the next back buffer in heavy scenes. */
    const clock_t sync_start = clock();
    gsKit_queue_exec(platform->gs);
    gsKit_sync_flip(platform->gs);
    r2_profile_sync_ms = (float)(clock() - sync_start) *
        (1000.0f / (float)CLOCKS_PER_SEC);
    gsKit_queue_reset(platform->gs->Per_Queue);
}
