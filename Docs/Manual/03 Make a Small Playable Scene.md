# Make a Small Playable Scene

This walkthrough creates the basic shell of a small 3D game area.

## 1. Import the environment

Drag a supported model into the Project window, then place it in the scene. Select its Mesh Renderer to assign materials and textures. Models may contain several material slots; edit each slot separately instead of combining unrelated surfaces.

## 2. Make the room solid

Add Box Colliders to simple floors, walls, furniture, and barriers. New box colliders fit the visible mesh automatically. Use collision layers when something should affect only the player, camera, triggers, or another group.

Avoid using one enormous collider for a detailed room. Several simple boxes are easier to understand and usually behave better.

## 3. Add the player

Place a character object above the floor and add Player Controller. Add a Capsule Collider sized around the character's body. The capsule controls where the character can walk; the visible model may extend slightly beyond it.

Assign an Animator Controller if the character has idle, walking, running, jumping, or other animations.

## 4. Add a camera and light

Add a Camera and configure the Player Controller or orbit-camera setup to follow the player. Add a Directional, Point, or Spot Light. Realtime lights update moving objects. Baked lights affect static surfaces during export. Mixed lights can do both.

## 5. Test

Press Play and check walking, jumping, walls, furniture, camera collision, lighting, and frame statistics. Fix obvious scene problems before exporting to PS2.
