# Animator Trigger Zones

Use an empty object with **Animator Trigger Zone**. Adding the component automatically adds a Box Collider if needed and enables Is Trigger. No Mesh Renderer is required.

## Example NPC setup

1. Set the NPC's controller default to Idle and enable Play On Start.
2. Add a non-looping Wave state using your wave animation clip.
3. Add a **Trigger** parameter named `Wave` (not a Boolean).
4. Connect Idle → Wave with condition Triggered, parameter Wave.
5. Connect Wave → Idle with condition Finished.
6. Create an empty object near the NPC. Add Animator Trigger Zone, drag the NPC from the Hierarchy into Target Animator, and enter `Wave` as Trigger Name.
7. Adjust Box Collider Size and Center to cover the approach area. Helper Gizmos must be on to see collider outlines while editing. Save and rebuild.

Only a colliding object with PlayerController fires this component. A player initially inside counts as an entry. Staying inside does not keep setting the trigger; exiting and re-entering does. Animator triggers remain pending until a matching transition consumes them, so re-entering during Wave can queue another wave if only Idle consumes the trigger.

Editor and Windows use the existing collision-entry events and collision layers. PS2 exports invisible boxes separately from renderable objects and latches entry per zone. PS2 supports 64 zones, the active native player, stationary world-axis-aligned boxes, and a same-scene uniquely named skeletal Animator target. Zone bounds are baked at export, so moving a zone during gameplay is not supported on PS2 yet. PS2 overlap uses the existing character-trigger approximation; test the boundary on console. Rename or duplicate target objects carefully: the target field is a name reference.

Scenes with zones use R2SC v25; scenes without them remain v24. Rebuild the PS2 game after adding zones. The exporter rejects missing targets, missing trigger colliders, and missing/wrong-type trigger parameters. This feature does not alter the source scene or assign an animation clip automatically.
