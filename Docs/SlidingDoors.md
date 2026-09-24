# Sliding doors

Create a ready-made door from **Hierarchy right-click → Sliding Doorway**.
Move the selected parent to ground level where you want the doorway. It contains
two jambs, a header and a teal panel with a solid Box Collider. No script setup is
needed. You can save the parent as a prefab using the normal prefab workflow.

In play mode, approach the panel and press **E** / **Cross** when Open appears.
The panel lifts, its collider follows, and you can walk through the opening.
The opening press is consumed before player movement/jump. Holding the action
does not restart opening. The door remains open until scene reload.

Select the **Door panel** child to adjust Sliding Door settings:

- Lift: world-up travel distance, default 3.3 units.
- Opening Speed: world units per second, default 2.
- Interaction Range: distance from player origin to the closed panel center,
  default 3 units. This is proximity, not a line-of-sight test.

Input uses the project's Interact button binding, with existing E/Cross fallback.
The nearest closed door wins. Existing dialogue/sign interactions take precedence
where ranges overlap. Pausing or interactive menus suppress door interaction;
gameplay pause also freezes opening motion. Non-pausing dialogue does not freeze
an already-opening door. Scene reload closes doors; save/checkpoint persistence
for door state is not implemented.

This is a built-in gameplay component on desktop and PS2, not a claim that
arbitrary door C# scripts are now translated. Closing, locks/keys, sounds,
automatic opening, crushing prevention and saving open state are future work.
Keep the doorway's parents static. Lift is world-up even if the parent rotates.
The moving panel must have no children or other behavior components on PS2;
use the static parent for the frame. Do not add Rigidbody or Script to the panel.

## PS2 format and validation

Door scenes use R2SC v44. After the existing Interactable block, a count and up
to 64 32-byte door records precede the v43 playback/script tail. Each record is:
source renderer index, Interact button index, lift, speed, range, closed XYZ.
Runtime progress and opening state are not serialized. Door-free scenes retain
their existing version. Native loading rejects invalid indices, duplicate targets,
non-box targets and malformed numeric settings. Export additionally rejects
unsupported panel components and moving parent behaviors.

Automated tests cover creation, serialization/reload, range, prompt, consumed
input, pause, clamped opening, actual desktop player collision blocked/clear,
v44 layout, invalid settings, and native reading of an exported door scene.
Editor and PS2 builds pass. PCSX2/hardware playtesting is still pending.
The component is linked into R2Engine.Runtime and updated by both editor play
mode and the standalone player. Player publish (the shared step used by game
builds, including PS2) is verified as well.
