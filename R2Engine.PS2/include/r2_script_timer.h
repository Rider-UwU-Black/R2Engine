#ifndef R2_SCRIPT_TIMER_H
#define R2_SCRIPT_TIMER_H
#include <stdint.h>
#include "r2_script_motion.h"

/* R2SC v30: 36 bytes per instance. Operation 0 preserves v29 motion.
   1:< 2:<= 3:> 4:>= 5:== 6:!=. State is private to the scene instance. */
typedef struct R2ScriptTimer
{
    float elapsed, threshold;
    uint32_t operation;
    R2ScriptMotion otherwise;
} R2ScriptTimer;

static inline int r2_script_is_trigger(uint32_t operation)
{ return operation >= 9u && operation <= 11u; }

static inline int r2_script_condition_read(R2ScriptTimer *out, const void *bytes, int allow_input)
{
    memcpy(out, bytes, sizeof(*out));
    if (!isfinite(out->elapsed) || !isfinite(out->threshold) || out->operation > (allow_input >= 12 ? 19u : allow_input >= 11 ? 18u : allow_input >= 10 ? 17u : allow_input >= 9 ? 16u : allow_input >= 8 ? 15u : allow_input >= 7 ? 14u : allow_input >= 6 ? 13u : allow_input >= 5 ? 12u : allow_input >= 4 ? 11u : allow_input >= 3 ? 9u : allow_input >= 2 ? 8u : allow_input ? 7u : 6u)) return 0;
    if (out->operation == 19u && (out->elapsed != 0 || out->threshold < 0 || out->threshold > 15 || out->threshold != (float)(uint32_t)out->threshold)) return 0;
    if (out->operation == 18u && (out->elapsed != 0 || out->threshold != 0)) return 0;
    if (out->operation == 16u && (out->elapsed != 0 || out->threshold < 256 || out->threshold > 1023 || out->threshold != (float)(uint32_t)out->threshold)) return 0;
    if (out->operation == 15u && (out->elapsed != 0 || out->threshold < 0 || out->threshold > 15 || out->threshold != (float)(uint32_t)out->threshold)) return 0;
    if ((out->operation == 14u || out->operation == 17u) && (out->elapsed != 0 || out->threshold < 0 || out->threshold > 255 || out->threshold != (float)(uint32_t)out->threshold)) return 0;
    if (out->operation == 13u && (out->elapsed < 0 || out->threshold <= 0 || !isfinite(out->threshold * 2.0f))) return 0;
    if (out->operation == 12u && out->elapsed != 0 && out->elapsed != 1) return 0;
    if (out->operation >= 7u && out->operation <= 12u && ((out->operation != 12u && out->elapsed != 0) || out->threshold < 0 || out->threshold > 15 ||
        out->threshold != (float)(uint32_t)out->threshold)) return 0;
    for (int axis=0; axis<3; ++axis)
        if (!isfinite(out->otherwise.position[axis]) || !isfinite(out->otherwise.rotation[axis])) return 0;
    if (r2_script_is_trigger(out->operation) && out->threshold != 0 && out->threshold != 1) return 0;
    if ((out->operation >= 8u && out->operation <= 10u) || out->operation == 15u)
        for (int axis=0; axis<3; ++axis)
            if (out->otherwise.position[axis] != 0 || out->otherwise.rotation[axis] != 0) return 0;
    if (out->operation == 11u)
        for (int axis=0; axis<3; ++axis) if (out->otherwise.position[axis] != 0) return 0;
    return 1;
}

/* v35 bool state changes before this frame's branch, matching C# statement order. */
static inline void r2_script_toggle_tick(R2ScriptTimer *state, const R2ScriptMotion *on,
    float position[3], float rotation[3], float dt, int pressed)
{
    if (pressed) state->elapsed = state->elapsed == 0 ? 1.0f : 0.0f;
    r2_script_motion_tick(state->elapsed != 0 ? on : &state->otherwise, position, rotation, dt);
}

/* v33 player entry. Elapsed is reused as the 0/1 overlap latch, threshold is
   the cooked mutual collision-layer permission. Scene reload resets the latch. */
static inline void r2_script_trigger_tick(R2ScriptTimer *condition, const R2ScriptMotion *step,
    float position[3], float rotation[3], int inside)
{
    if (condition->threshold == 0) { condition->elapsed = 0; return; }
    int entering = inside && condition->elapsed == 0;
    int exiting = !inside && condition->elapsed != 0;
    condition->elapsed = inside ? 1.0f : 0.0f;
    if ((entering && condition->operation != 10u) || (exiting && condition->operation == 10u))
        r2_script_motion_tick(step, position, rotation, 1.0f);
    else if (exiting && condition->operation == 11u)
        r2_script_motion_tick(&condition->otherwise, position, rotation, 1.0f);
}

/* Sample once per gameplay frame, shared without consuming presses. First input
   after scene load, reconnect or pause is a baseline, not a fresh press. */
typedef struct R2ScriptInputEdges { uint16_t previous; int ready; } R2ScriptInputEdges;
/* Read releases BEFORE r2_script_pressed advances the shared frame snapshot. */
static inline uint16_t r2_script_released(const R2ScriptInputEdges *state, uint16_t buttons,
    int connected, int paused)
{
    return connected && !paused && state->ready ? (uint16_t)(state->previous & (uint16_t)~buttons) : 0;
}
static inline uint16_t r2_script_pressed(R2ScriptInputEdges *state, uint16_t buttons,
    int connected, int paused)
{
    if (!connected || paused) { state->previous = buttons; state->ready = 0; return 0; }
    uint16_t pressed = state->ready ? (uint16_t)(buttons & (uint16_t)~state->previous) : 0;
    state->previous = buttons; state->ready = 1;
    return pressed;
}

static inline void r2_script_press_tick(const R2ScriptMotion *step,
    float position[3], float rotation[3], int pressed)
{
    if (pressed) r2_script_motion_tick(step, position, rotation, 1.0f);
}

/* Independent if statements execute in source order, including shared buttons. */
static inline uint16_t r2_script_pair_edges(const R2ScriptTimer *state, int branch,
    uint16_t pressed, uint16_t released)
{
    return state->operation == 16u && ((uint32_t)state->threshold & (1u << (8 + branch))) ? released : pressed;
}

static inline void r2_script_dual_press_tick(const R2ScriptTimer *state, const R2ScriptMotion *first,
    float position[3], float rotation[3], int first_pressed, int second_pressed)
{
    r2_script_press_tick(first, position, rotation, first_pressed);
    r2_script_press_tick(&state->otherwise, position, rotation, second_pressed);
}

static inline void r2_script_dual_held_tick(const R2ScriptTimer *state, const R2ScriptMotion *first,
    float position[3], float rotation[3], float dt, int first_down, int second_down)
{
    if (first_down) r2_script_motion_tick(first, position, rotation, dt);
    if (second_down) r2_script_motion_tick(&state->otherwise, position, rotation, dt);
}

static inline void r2_script_start_apply(R2ScriptTimer *state, float position[3], float rotation[3])
{
    if (state && (state->operation == 18u || state->operation == 19u) && state->elapsed == 0)
    {
        r2_script_motion_tick(&state->otherwise, position, rotation, 1.0f);
        state->elapsed = 1;
    }
}

/* Startup increments occupy otherwise, so they must never become an off branch. */
static inline void r2_script_start_held_tick(const R2ScriptMotion *rate,
    float position[3], float rotation[3], float dt, int held)
{
    if (held) r2_script_motion_tick(rate, position, rotation, dt);
}

static inline int r2_script_timer_read(R2ScriptTimer *out, const void *bytes)
{ return r2_script_condition_read(out, bytes, 0); }

/* v31 operation 7: held button, no timer advancement or input consumption. */
static inline void r2_script_input_tick(const R2ScriptTimer *condition, const R2ScriptMotion *held,
    float position[3], float rotation[3], float dt, int button_down)
{
    r2_script_motion_tick(button_down ? held : &condition->otherwise, position, rotation, dt);
}

static inline void r2_script_timer_tick(R2ScriptTimer *timer, const R2ScriptMotion *when_true,
    float position[3], float rotation[3], float dt)
{
    if (!timer || !timer->operation || timer->operation == 18u)
    { r2_script_motion_tick(when_true, position, rotation, dt); return; }
    if (!isfinite(dt) || dt <= 0) return;
    float elapsed = timer->elapsed + dt;
    if (!isfinite(elapsed)) return; /* Fail closed on overflow rather than poison state. */
    if (timer->operation == 13u) elapsed = fmodf(elapsed, timer->threshold * 2.0f);
    timer->elapsed = elapsed; /* C# timer advancement/wrap precedes the comparison. */
    int condition;
    switch (timer->operation)
    {
        case 1: condition = elapsed < timer->threshold; break;
        case 2: condition = elapsed <= timer->threshold; break;
        case 3: condition = elapsed > timer->threshold; break;
        case 4: condition = elapsed >= timer->threshold; break;
        case 5: condition = elapsed == timer->threshold; break;
        case 6: condition = elapsed != timer->threshold; break;
        case 13: condition = elapsed < timer->threshold; break;
        default: return;
    }
    r2_script_motion_tick(condition ? when_true : &timer->otherwise, position, rotation, dt);
}
#endif
