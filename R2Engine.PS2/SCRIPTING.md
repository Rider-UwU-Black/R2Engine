# PS2 custom scripts: motion, timers, conditions and held input

The PS2 does not run the desktop C# runtime. The editor checks a restricted C#
script without executing it and lowers supported `Update` motion into native
per-instance position/rotation rates in R2SC v29. Timer scripts use R2SC v30.
Existing spin-only scenes still use v29; scenes without translated scripts keep
their previous package version. Held-input scripts use R2SC v31.

## Try it

1. Restart the rebuilt editor. Create a fresh cube with just Transform and Mesh Renderer.
2. Add a Script component using `Assets/Scripts/Ps2Spin.cs`.
3. Set its public `Speed` field to 45 (degrees per second), or -90 to reverse it.
4. Test in desktop Play, then rebuild the PS2 ISO with the new native ELF.
5. Check the cube rotates at the same rate, pauses with a gameplay-pausing menu,
   and starts from its original rotation when the scene is reloaded.

For the timer test, use `Assets/Scripts/Ps2DelayedMove.cs` instead (one script per
object). With Delay=2 and Speed=1, the cube should wait two gameplay seconds and
then move along positive X at one unit per second. Try different Delay values on
two cubes, pause before the delay expires, and reload the scene to restart both.
Test editor Play and the freshly rebuilt PS2 ISO. Keep the original spin cube as
a regression check in the same scene.

No existing scene or character is changed automatically. Use a root object with
no children, collider, player controller, animator, or other components for now.

## Supported syntax

One public top-level class deriving directly from `ScriptBehaviour`; optional
public float fields with constant initial values; one overridden block-bodied
`Update(float deltaTime)` method. Inspector values are baked separately for each
instance. Fields cannot be modified by the script in this slice.

```csharp
using System.Numerics;
using R2Engine.Editor.Scene;

public class Drift : ScriptBehaviour
{
    public float Speed = 2f;
    public override void Update(float deltaTime)
    {
        Transform.Position += Vector3.UnitX * Speed * deltaTime;
        Transform.Rotation += new Vector3(0f, 45f, 0f) * deltaTime;
    }
}
```

At most one `+=` statement per Position/Rotation; `deltaTime` must be the last
factor. Vector3 UnitX/Y/Z, Zero/One, and three-float constructors are supported,
optionally multiplied by float constants or a public float field. No loops,
arbitrary conditions, calls, input, general mutable state, Start, collision/trigger
callbacks, spawning, namespaces, or general C# translation yet.

## Held-input form

Attach `Assets/Scripts/Ps2ButtonSpin.cs` to another root, childless cube with no
collider/other components. Hold Sprint to rotate, release to stop. Desktop defaults
use Shift; the default PS2 binding is R1 (controller index 1). Your Project Settings
binding takes precedence. Restart the updated editor and rebuild the ISO first.
Keep the spin and delayed-move cubes to check all three paths together.

```csharp
public override void Update(float deltaTime)
{
    if (RuntimeInput.IsActionDown("Sprint"))
    {
        Transform.Rotation += Vector3.UnitY * Speed * deltaTime;
    }
    else
    {
        // Optional motion when released; omit else to stop.
    }
}
```

The whole Update is one if/optional else, with no private timer field. The action
name must be a string literal. Lookup is case-insensitive and the last project
binding wins, matching RuntimeInput; implicit Interact defaults to controller index
0 unless overridden. Only Button actions bound to controller indices 0-15 are
accepted on PS2. Unknown names, keyboard-only bindings, and axis actions warn and
skip the script. Binding changes require rebuilding the ISO.

This checks held state, not a one-shot press, and does not consume input: Sprint
can still control the player while rotating the cube. Pausing gameplay also pauses
script motion. A disconnected pad counts as released, including any else branch.
Input+timer combinations, &&/||, negation, release events and nested branches
are not supported yet. One-shot presses use the separate form below.

v31 reuses the 36-byte condition record: operation 7 means held input, its threshold
slot is an exact integer controller index, and elapsed must be zero. The native
runtime uses the same button-index mapping as player controls. Old v30 packages
still reject operation 7. No extra persistent input history is required.

## One-shot press form

Use `Assets/Scripts/Ps2PressTurn.cs` on another collider-free cube. Press Sprint
(default PS2 R1) to turn 90 degrees once. Holding must not repeat; release and press
again for another turn. The editable Degrees field sets the angle per press.

```csharp
public override void Update(float deltaTime)
{
    if (RuntimeInput.WasActionPressed("Sprint"))
    {
        Transform.Rotation += Vector3.UnitY * Degrees;
    }
}
```

This form supports fixed Position/Rotation += vector steps, WITHOUT deltaTime.
Use no else branch or private timer. Binding rules match held input. Source with
deltaTime, else, or combined conditions is rejected rather than partially translated.
R2SC v32 adds opcode 8 in the same condition record; motion values now mean steps
for that opcode only, and the else-motion record must be zero.

The PS2 samples script press edges once per gameplay update, independently of
player/menu input history; multiple scripts see the same press. After scene load,
pause/resume or controller reconnect, the first sample is a baseline: release a
held button and press again. This deliberately suppresses accidental activation
from menu selections or reconnects. Held-input scripts are unchanged. A same-scene
checkpoint load is not a full scene reload and does not reset script history.

Automated compiler, v32 layout, native edge/step, pause/reconnect/load-baseline,
and held-input regression tests pass. The user confirmed the one-shot sample works
in-game. Menu/reload/reconnect edge cases still need explicit playtest confirmation.

## Player trigger-entry form

The first callback slice is `OnTriggerEnter(GameObject other)`, filtered to the
single active Player Controller. It applies fixed rotation steps to the scripted
object. It does not support arbitrary collision events or modifying other objects.

Test with `Assets/Scripts/Ps2TriggerTurn.cs`:

1. Make a root, childless cube with a Mesh Renderer and one Script component.
2. Add one Box Collider, enable **Is Trigger**, keep Center at zero, and try Size
   (3, 3, 3). The cube should not block movement.
3. Assign Ps2TriggerTurn. Its Degrees default is 45 so a plain cube visibly turns.
4. Keep exactly one active rendered player with one non-trigger Capsule or Box
   Collider. Collider layers/masks must permit each other.
5. Restart the updated editor and rebuild the ISO. Walk into the trigger: one turn.
   Stand inside: no repeats. Leave fully, then enter again: another turn.

```csharp
public override void OnTriggerEnter(GameObject other)
{
    if (other.GetComponent<PlayerController>() == null) return;
    Transform.Rotation += Vector3.UnitY * Degrees;
}
```

The player filter above is required. Use public float constants/settings; no
private timers, additional methods, general ifs, deltaTime, trigger movement,
OnTriggerStay, other-object actions, or NPC callbacks yet. Exit callbacks may be
used alone or paired as described below. The owner may
have only Transform, Mesh Renderer, Script and one trigger Box Collider.
Unsupported target setups produce warnings, not partial callback translation.

PS2 detection reuses the existing player-versus-axis-aligned-trigger overlap
routine used for scene triggers (an approximate player volume, not arbitrary
collider-pair physics). Rotation changes the visual cube, not the axis-aligned
trigger volume. Entry is evaluated after player movement; pausing gameplay stops
evaluation. Scene reload resets occupancy, so spawning inside causes one entry.
Same-scene checkpoint loads do not reset occupancy; subsequent overlap determines
whether the player has left or re-entered. Script state is not saved to checkpoints.

R2SC v33 adds condition opcode 9. Its threshold is the cooked mutual-layer
permission (0/1); elapsed is the runtime overlap latch, initialized to zero. Fixed
rotation uses the motion record; center/size reuse the existing per-instance scene
trigger-volume record. The legacy filename-based scene loader remains separate.
Compiler, target/mask, v33 package and native entry-state tests pass. The user
confirmed the in-game sample works as described: entry fires once, staying inside
does not repeat, and leaving/re-entering fires again.

## Trigger exit and paired entry/exit

Use `Assets/Scripts/Ps2TriggerEnterExit.cs` on a fresh trigger cube with the same
Box Collider setup above. Use only one Script component. It turns +45 degrees on
entry and -45 on exit; staying inside or outside does not repeat either action.
Public EnterDegrees and ExitDegrees can be edited independently.

Both callbacks must start with the same player filter and contain only supported
fixed rotation increments. You may use OnTriggerExit alone, or pair it with
OnTriggerEnter. Update plus callbacks, extra methods, and general state changes
remain unsupported. This sample changes rotation, not object activation or lights.

```csharp
public override void OnTriggerExit(GameObject other)
{
    if (other.GetComponent<PlayerController>() == null) return;
    Transform.Rotation += Vector3.UnitY * ExitDegrees;
}
```

R2SC v34 uses operation 10 for exit-only (main motion record is the exit step) and
11 for paired callbacks (main record is entry, otherwise record is exit). One
shared overlap latch detects both edges. Scene reload restores transforms and
resets the latch without dispatching an exit; spawning inside still counts as an
entry. Mask-disabled triggers fire neither callback. Pausing freezes evaluation.
Volumes/detection and player restrictions match the entry slice.

Paired/exit-only compiler and package tests, native repeated-cycle/stay suppression,
reload/mask/malformed-record tests and entry regressions pass. The user confirmed
the paired sample works after removing an obsolete field from a previous script.
Pause and scene-reload checks remain open. Inspector reassignment now clears old
overrides when changing script assets; same-asset drops and renames preserve them.

## Timer/condition form

In addition to public float settings, declare exactly one private float timer
(default zero, or a finite float constant initializer). The entire Update must
be a timer increment followed by one `if` with an optional `else`:

```csharp
private float _elapsed;
// Public Delay and Speed fields omitted here; see Ps2DelayedMove.cs.
public override void Update(float deltaTime)
{
    _elapsed += deltaTime;
    if (_elapsed >= Delay)
    {
        Transform.Position += Vector3.UnitX * Speed * deltaTime;
    }
    else
    {
        Transform.Rotation += Vector3.UnitY * 45f * deltaTime;
    }
}
```

Comparisons: `<`, `<=`, `>`, `>=`, `==`, `!=`. The timer must be on the left;
the right operand is a float constant or public float setting. Each branch follows
the motion rules above, and may be empty. No nested if, else-if, &&/||, early return,
timer reset, second timer, or using elapsed time as movement speed yet. Use `>=`
for delays: exact float equality can be skipped by frame-sized time increments.

The timer advances before the comparison, matching the source statement order.
The selected branch gets that frame's full deltaTime, including the frame that
crosses the threshold. Timers are separate per object, pause with gameplay-pausing
menus, and reload their initial values with the scene. Invalid/nonpositive dt is
ignored; numeric overflow fails closed instead of corrupting transforms.

The v30 tail retains v29's 8-byte playback records and 24-byte motion records,
then adds a 36-byte timer record per instance (initial time, threshold, comparison
code, else-position rate, else-rotation rate). Code 0 means unconditional v29 motion.
Condition memory is allocated for v30+ scenes and freed/replaced on scene changes.
Loading a checkpoint in the same scene does not reload the scene or restart its
script timers. Script timer state is not currently serialized in checkpoints.

Unsupported scripts are **not executed on PS2**. Export emits warnings naming the
scene/object/script and writes `R2Data/ps2-script-report.json`. Builds still
complete so existing projects with desktop-only diagnostics remain usable.
The old `TriggerSceneLoader.cs` filename adapter is retained and explicitly
reported as a legacy approximation, not successful C# translation. It cooks the
configured SceneName, not the full source or all its fields.

Validation: compiler rejection tests, actual package layout tests, native motion
tests with sanitizers, editor build and PS2 cross-build. The user confirmed the
Ps2Spin in-game test works (emulator versus hardware was not specified). Timer
compiler rejection, comparison-boundary, instance-isolation, reload-state, and
v24/v29/v30 package tests pass. The user confirmed delayed movement, pausing, and
scene reload restoring the original position and restarting the delay before
movement resumes. Changed settings, independent timers, and else branches still
need explicit in-game confirmation. Held-input compiler/binding and v31 package
tests, native held/released/malformed-record tests, and timer compatibility tests
pass. The user confirmed held-input behavior works in-game.

## Button toggle (v35)

`Assets/Scripts/Ps2ToggleSpin.cs` tests one private bool per object. Attach it to
a root, childless cube with a Mesh Renderer and no collider. Rebuild the PS2 ISO.
Tap Sprint (R1 by default) to start spinning; release it and the cube keeps spinning.
Tap again to stop. Holding the button does not repeatedly toggle. Speed defaults
to 90 degrees/second and is editable in the Inspector.

The supported Update shape is exactly a press-controlled `state = !state;`
followed by `if (state)` motion, with optional else motion. One private bool with
an optional constant initializer is allowed, not combined with a timer. Each
branch follows the existing deltaTime-scaled position/rotation rules. This is
not general-purpose bool expression or arbitrary C# support.

R2SC v35 keeps the 36-byte condition record: opcode 12, initial bool encoded as
0/1 in elapsed, controller index in threshold, and off-branch rates in otherwise.
The flip precedes this frame's movement. State is independent per instance,
pauses with gameplay, and resets to its initializer on scene reload. Checkpoints
do not serialize it. Load/resume/reconnect input baselines suppress false presses.
R1 remains available to the player and other scripts; this script does not consume it.
Compiler/package tests and sanitized native state/edge tests pass; in-game
the user confirmed the toggle test seems good in-game.

## Repeating timer (v36)

Attach `Assets/Scripts/Ps2LoopMove.cs` to a fresh root, childless cube with a Mesh
Renderer and no collider, then rebuild the ISO. It moves along X, changing
direction every two seconds. HalfSeconds must be positive; ForwardSpeed and
BackwardSpeed default to 2 and -2. Pause should freeze motion; scene reload
restores its original transform and timer. User confirmed the requested test works.

The supported Update order is `timer += deltaTime;`, then
`timer %= HalfSeconds * 2f;`, then `if (timer < HalfSeconds)` with normal
deltaTime-scaled motion and an optional else branch. One nonnegative private
float timer is supported. Other reset forms, mixed input/timers, and general
loops remain unsupported. Invalid periods are reported at export.

Opcode 13 reuses the condition record, with the initial timer and half-period
in the first two fields. Native remainder wraps the timer before selecting
the branch, matching C#. Each selected branch gets the full frame deltaTime:
this is frame-stepped velocity, not exact endpoint interpolation, so uneven
frame times or unequal speeds can cause positional drift. It is a script
state test, not collision-aware patrol or moving-platform support. Timer state
pauses with gameplay, resets on scene reload, and is not checkpoint-serialized.

## Two independent press actions (v37)

Attach `Assets/Scripts/Ps2TwoButtonTurn.cs` to a fresh root, childless cube with
a Mesh Renderer and no collider. Rebuild the ISO. Sprint (default R1) turns it
+45 degrees; Jump (default Cross) turns it -45 degrees. Each press acts once;
holding does not repeat. Both fresh presses in the same frame apply both steps
in source order, cancelling with the sample's default angles. Pressing Cross
can also make the player jump; scripts do not consume the shared input.

Exactly two independent `if (RuntimeInput.WasActionPressed("Name"))` statements
are supported in Update, with fixed position/rotation increments. No else,
deltaTime factors, mixed held/press conditions, or private state in this form.
Both actions resolve through project bindings. Even if both names map to one
button, both statements run. Unsupported actions fail export with a warning.

Opcode 14 uses the existing condition layout: elapsed is zero, threshold packs
the first button index in bits 0-3 and second in bits 4-7. The main motion stores
the first step, otherwise stores the second. No extra input history is needed;
existing pause/load/reconnect edge baselines apply. Native and compiler/package
tests pass. User confirmed the two-button sample works as expected.

## Release action (v38)

`Assets/Scripts/Ps2ReleaseTurn.cs` turns a fresh collider-free cube by 45 degrees
when Sprint (R1 by default) is released. Pressing/holding does nothing. Repeat
the hold/release cycle to turn again. Rebuild the ISO with the new editor/runtime.
User confirmed the release sample looks good in-game.

The new desktop API `RuntimeInput.WasActionReleased("Name")` checks keyboard
bindings and the mapped controller button. Like WasActionPressed, any mapped
key/button edge counts; it does not require every binding to be released.
Controller disconnect clears its history instead of generating a release.

PS2 translation supports a single release if with fixed position/rotation
increments, no else or deltaTime factor. Release-driven toggles remain unsupported;
paired release/press scripts are supported in v39. Opcode 15 retains the condition
layout: elapsed zero, threshold button index 0-15, otherwise zero. Releases
are sampled from the same pre-update input history as presses, once per frame,
without consuming the event. Pause, disconnect and scene-load baselines suppress
stray releases. Native edge tests, desktop input tests and package tests pass.

## Paired press/release (v39)

Attach `Assets/Scripts/Ps2PressReleaseTurn.cs` to a fresh collider-free cube,
then rebuild the ISO. Press Sprint (R1) for +45 degrees, hold to stay turned,
and release for -45 degrees. User confirmed this test works perfectly.

Two independent if statements can now each use WasActionPressed or
WasActionReleased, in either order, on the same or different actions. Steps
are fixed position/rotation increments, executed in source order. No else,
private state, or held-input branches in this form. Opcode 14 still represents
two presses. Opcode 16 stores mixed/two-release pairs: button indices occupy
bits 0-7 of threshold; bits 8 and 9 select release edges for the first and
second statements. The record layout is unchanged; elapsed remains zero.

These are edge-triggered increments, not a guaranteed neutral-position control.
Releases during PS2 pause/disconnect are suppressed, so the cube can remain
turned if the matching release is missed. Likewise, a button already held at
scene load has no press edge, but a later release does. Reload resets the
transform; automatic neutral-state reconciliation is outside this sample.
Compiler/package and native repeated-cycle/edge-selection tests pass.

## Two held actions (v40)

Attach `Assets/Scripts/Ps2TwoButtonMove.cs` to a fresh root, childless Mesh Renderer
cube with no collider, then rebuild the ISO. Hold Sprint (default R1) to slide
along +X at 2 units/second. Hold Jump (default Cross) to slide along -X. Both
held cancel at the default equal/opposite speeds; release both to stop. Cross
also reaches the player controller. Pause should freeze motion; scene reload
restores the original transform. This is not collision-aware movement or a
platform that carries the player. User confirmed this test works perfectly.

Two independent IsActionDown if statements are supported, without else or
private state. Each branch uses the standard deltaTime-scaled position/rotation
motion form. Mixed held/edge pairs remain unsupported. Both true branches run
in source order; they are not an if/else priority pair. Same-button bindings
also run both statements. Unequal speed overrides need not cancel.

Opcode 17 keeps the existing condition record: zero elapsed, packed button
indices in threshold bits 0-7, and the second branch's rates in otherwise.
It uses current connected-pad state, no edge latch or accumulated timer. Old
package versions reject this opcode. Compiler/package and native rate/input
combination tests pass.

## Start lifecycle (v41)

Attach `Assets/Scripts/Ps2StartSpin.cs` to a fresh root, childless cube with a
Mesh Renderer and no collider, then rebuild the ISO. Start tilts it 45 degrees
around Z once; Update spins around Y at 30 degrees/second. Inspector settings
are InitialTilt and Speed. Pause/resume should freeze/resume only the spin.
Scene reload restores the authored transform and applies one fresh tilt, not
an additional tilt on the previous runtime transform. User confirmed tilted spinning;
explicit pause/reload confirmation is still pending.

Supported forms: nonempty Start alone, or Start plus unconditional Update.
Start uses fixed Transform.Position/Rotation += increments. Update uses the
usual deltaTime-scaled rates. No private state, assignments to fields, input,
timer logic, trigger callbacks, or other lifecycle combinations in this slice.

Opcode 18 retains the condition record. Elapsed/threshold are initially zero;
otherwise stores Start increments, the main motion stores Update rates (zero
for Start-only). After scene data is accepted, the native loader applies Start
before the first gameplay Update/render and marks its one-time latch. Update
does not advance that latch or reapply Start. Reload creates fresh state;
same-scene checkpoint loading does not rerun Start. This lowering is restricted
to isolated decorative objects: it does not model arbitrary inter-script Start
ordering or delayed activation. Compiler/package and native lifecycle tests pass.

## Start plus held Update (v42)

`Assets/Scripts/Ps2StartButtonSpin.cs` uses the same one-time 45-degree Z tilt,
but spins around Y only while Sprint (default R1) is held. Attach it to a fresh
root, childless Mesh Renderer cube with no collider and rebuild the ISO.
Release should stop spinning without undoing or repeating the tilt. Test
multiple holds, pause/resume, and scene reload. User confirmed the sample works.

This extends the Start lifecycle slice to one IsActionDown Update branch with
no else motion. Unconditional Start/Update still uses v41. Start plus timers,
toggles, edge actions, two-input branches, or nonzero else motion remains
unsupported. Startup still cannot initialize private fields in this subset.

Opcode 19 retains the record layout: elapsed starts at zero (becomes the startup
latch), threshold is button index 0-15, otherwise contains startup increments,
and main motion contains the held rates. The loader applies startup once;
the gameplay path never treats startup increments as an off-input branch.
Pause freezes Update, not scene initialization. Reload resets the transform and
latch; a same-scene checkpoint load does not rerun startup. Automated tests pass.

## Start plus timer (v43)

Attach `Assets/Scripts/Ps2StartDelayedSpin.cs` to a fresh root, childless cube
with a Mesh Renderer and no collider, then rebuild the ISO. The cube starts
tilted 45 degrees, waits Delay (default two seconds), then spins at Speed
(default 30 degrees/second). Pause freezes the wait/spin; scene reload restores
the original transform, applies one fresh tilt, and restarts the wait.
User confirmed the startup/delay test works as intended.

Start can now accompany the supported timer Update forms, including ordinary
comparisons with else motion and repeating timers. Start remains fixed
position/rotation increments using public float settings; it cannot read/write
the private timer. Timer initialization comes from its field initializer.
Other Start combinations retain their previous restrictions.

For these combinations, v43 appends a separate 24-byte startup record per
instance after the 36-byte condition block. Tail order is playback (8), motion
(24), condition (36), startup (24): 92 bytes per instance total. Startup uses
position XYZ and rotation XYZ increments; nonscripted/legacy objects get zero
records. Existing v41/v42 startup representations remain valid and unchanged.
The native loader validates all startup floats and exact tail length before
applying startup once on accepted scene data. No extra persistent startup array
is allocated. Timer state and else rates remain independent of startup.
Compiler/package tests, native tests against an actual exported v43 fixture,
and PS2 cross-build pass. Checkpoints do not serialize the script timer or rerun
startup for a same-scene load.

## Start plus toggle (v43)

Attach `Assets/Scripts/Ps2StartToggleSpin.cs` to a fresh root, childless Mesh
Renderer cube with no collider and rebuild the ISO. It starts tilted 45 degrees
and stopped. Tap Sprint (default R1) to spin; tap again to stop. Holding does
not repeatedly toggle, and changing the toggle does not reapply the tilt.
Scene reload restores the authored transform, applies one tilt and resets the
bool to its initializer (false in the sample). In-game confirmation is pending.

The compiler now allows the previously supported bool-toggle Update alongside
fixed-transform Start, including the toggle's optional off-motion branch.
Private bool reads/writes inside Start remain unsupported: its initializer
defines its initial value. Startup transforms stay separate from operation 12's
bool, binding and branch records using the existing v43 tail. The current PS2
runtime already supports this combination; no runtime or format change was
needed. Compiler/package and native startup/toggle isolation tests pass.
