# PS2 Animator playback

Rebuild the game after upgrading: scenes use R2SC v24 and baked animations now use R2AN v5. The runtime still accepts v2–v4 animations.

- Each object has its own animation clock. Identical mesh/controller combinations share baked data, not playback state.
- A controller starts in its Default State, regardless of the state's name.
- Play On Start off leaves the object in its bind pose until playback starts.
- As in editor Play mode, controller states supply Loop and Speed. With a direct external skeletal animation and no controller, the Animator supplies those settings.
- Non-looping clips stop and hold their final pose. Zero and negative speed are supported.
- Gameplay-pausing canvases pause playback. Non-pausing canvases do not.
- All conditions are exported: **IsTrue, IsFalse, Greater, Less, Equals, Triggered, Finished**. The first passing edge from the current state wins in authored order, at most once per update, with its Blend Duration. Only a winning trigger is consumed. A self-edge restarts the clip.
- Float, Integer, Boolean and Trigger parameters have independent values per instance; controller defaults initialize on scene load. Metadata is shared by mesh/controller variants. Values are allocated only for animated objects using controllers.
- PlayerController's configured Speed, VerticalSpeed, Moving, MovingBackward, Sprinting, Grounded and Jump parameter names are cooked as bindings. Parameter-based player controllers use these transitions instead of the old state-name selector. Controllers without parameters retain the legacy selector. PS2 movement is camera-relative, so MovingBackward is false. Speed uses normalized movement input times the sprint multiplier; desktop uses measured horizontal velocity, so acceleration/collision timing is not identical. Inputs feed the following animation update.
- Finished transitions do not wait on a parameter and looping clips never become Finished. Reverse playback reaching time zero does not satisfy Finished, matching the editor. As in the editor, a passing parameter transition can start a stopped Animator; Play On Start off alone does not disable its transition graph.

## Test parameter transitions on an NPC

1. Keep Idle as Default State. Add another state with a visibly different clip.
2. Add a Boolean parameter named `Go`, with its default value true.
3. Connect Idle to the other state with condition IsTrue, parameter Go and Blend Duration 0.2.
4. Save the controller and scene; compare editor Play with a freshly rebuilt PS2 game. Both should leave Idle automatically.
5. Change Go's default to false and rebuild: the NPC should stay Idle (assuming no other passing transition).

No source scenes/controllers are modified by this feature or its automated fixtures. NPC parameter defaults work without scripts. Arbitrary C# gameplay scripts are **not** compiled into the PS2 native runtime: custom live NPC updates still need native integration via `r2_ps2_animator_set_parameter(platform, instanceIndex, name, type, value)`. Types are 0=float, 1=integer, 2=bool, 3=trigger; set/reset a trigger with 1/0. This API returns zero for a missing instance/name or mismatched type. Parameter state is not currently saved in checkpoints.

Limits: 32 parameters (unique names, at most 63 UTF-8 bytes) and 128 transitions per controller. Missing states, missing/wrong-type parameters and non-finite values fail cooking explicitly. Native comparisons use float numeric values, as the editor's transition evaluator does.

## Test a one-shot animation

In the NPC's controller, add the one-shot and Idle states, assign their clips, set the one-shot as Default State, and disable its Loop setting. Add a transition from it to Idle, choose Finished, and set Blend Duration (for example 0.15 seconds). Leave Idle looping and the NPC's Play On Start enabled. Save the controller and scene, then rebuild the PS2 game. No scripts or parameters are required.

Transitions blend baked vertex poses on PS2 rather than the editor's bone rotations, so intermediate poses can differ slightly. Test the result on the character.

The existing baked-animation approach remains: 12 samples/second, at most 12 seconds per clip, now up to 32 states. Unsupported or overlong controller clips produce a cook error rather than silently disappearing/truncating. Controller variants on one model produce separate baked packages and therefore cost additional memory. Use a hardware test to check frame rate and memory for crowded scenes.
