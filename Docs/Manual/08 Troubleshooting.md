# Troubleshooting

## A scene opens to a blank or loading screen

Read the Console and PS2 build output first. Missing textures, unsupported imports, invalid startup scenes, and scene-format errors should be fixed before changing unrelated settings. Re-export after correcting the problem.

## An object is black

Check its material, texture assignment, surface mode, normals, and whether the model is meant to be viewed from inside. Enable Double Sided for appropriate interior geometry. Also confirm that the scene contains suitable lighting.

## The player catches on flat ground

Inspect nearby colliders. Look for overlapping boxes, low ceiling colliders, thin ledges, or a collider extending farther than its visible object. Use collision layers when a surface should affect the camera but not the player.

## Animation looks wrong

Check the controller's default state, transition conditions, Loop setting, playback speed, Root Motion, and Play On Start. Confirm that clips use the intended skeleton.

## Console performance stutters

Reduce expensive skinned meshes, large textures, active realtime lights, excessive audio quality, and unnecessary draw calls. Change one thing at a time and test again on hardware.

## PCSX2 does not launch

Open Hub Settings → External Tools. Choose the PCSX2 executable and press Test. A blank field uses automatic detection.
