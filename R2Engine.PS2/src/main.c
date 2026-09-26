#include "r2_ps2_platform.h"

#include <kernel.h>
#include <libpad.h>
#include <stdio.h>
#include <time.h>

int main(int argc, char **argv)
{
    (void)argc;
    (void)argv;

    R2Ps2Platform *platform = r2_ps2_create();
    if (platform == NULL)
    {
        printf("R2Engine PS2 backend probe failed to initialize.\n");
        SleepThread();
        return 1;
    }

    if (!r2_ps2_load_scene_file(platform, "host:assets/probe.r2scene"))
    {
        printf("Cooked scene failed to load; refusing to hide the error with the old embedded benchmark.\n");
        r2_ps2_destroy(platform);
        SleepThread();
        return 1;
    }

    float phase = 0.0f;
    clock_t previous_frame = clock();
    R2Ps2Input input;
#ifdef R2_DEVELOPMENT_BUILD
    unsigned int quit_combo_frames = 0u;
    const u16 quit_combo = PAD_L1 | PAD_L2 | PAD_R1 | PAD_R2 | PAD_START | PAD_SELECT;
#endif
    for (;;)
    {
        const clock_t frame_now = clock();
        double measured_seconds = (double)(frame_now - previous_frame) /
            (double)CLOCKS_PER_SEC;
        previous_frame = frame_now;
        /* The first sample can be zero and debugger/HostFS stalls can be very
           large. Keep real frame pacing while preventing either case from
           exploding physics or skipping animation states. */
        if (measured_seconds <= 0.0)
            measured_seconds = 1.0 / 60.0;
        if (measured_seconds < 1.0 / 240.0)
            measured_seconds = 1.0 / 240.0;
        if (measured_seconds > 0.1)
            measured_seconds = 0.1;
        const float delta_seconds = (float)measured_seconds;

        const clock_t input_start = clock();
        r2_ps2_poll_input(platform, &input);
#ifdef R2_DEVELOPMENT_BUILD
        if (input.connected && (input.buttons & quit_combo) == quit_combo)
        {
            if (++quit_combo_frames >= 20u)
            {
                r2_ps2_destroy(platform);
                FlushCache(0);
                FlushCache(2);
                /* Match the console's common IGR handoff. ExecOSD forces the
                   generic browser boot path and bypasses the launcher's
                   installed exit handling; Exit lets that loader regain
                   control when it supplied an exit target. */
                Exit(0);
            }
        }
        else
        {
            quit_combo_frames = 0u;
        }
#endif
        r2_ps2_update_ui(platform, &input);
        const clock_t gameplay_start = clock();
        r2_ps2_update_gameplay(platform, &input, delta_seconds);
        const clock_t audio_start = clock();
        r2_ps2_update_audio(platform);
        const clock_t update_end = clock();
        const float ticks_to_ms = 1000.0f / (float)CLOCKS_PER_SEC;
        r2_ps2_profile_frame((float)measured_seconds * 1000.0f,
            (float)(gameplay_start - input_start) * ticks_to_ms,
            (float)(audio_start - gameplay_start) * ticks_to_ms,
            (float)(update_end - audio_start) * ticks_to_ms);
        r2_ps2_begin_frame(platform);
        r2_ps2_draw_mesh_probe(platform, phase);
        r2_ps2_draw_ui(platform);
        r2_ps2_end_frame(platform);
        phase += 2.7f * delta_seconds;
    }

    r2_ps2_destroy(platform);
    return 0;
}
