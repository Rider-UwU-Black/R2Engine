# Animation, UI, and Audio

## Animation

An Animator Controller organizes animation clips into states such as Idle, Walk, Run, Jump, and Sit. Transitions decide when the character moves between states. Player Controller can update common parameters such as Speed, Moving, Sprinting, Grounded, and Jump.

Root Motion is set per animation clip. Leave it off when Player Controller should move the character. Reverse Playback allows a clip to be reused backward when that suits the motion.

Use the Animator graph's pan and zoom controls to organize larger controllers. Test transitions in editor Play mode and again in a PS2 build.

## User interface

Canvas objects contain panels, text, images, and buttons. Use a Canvas for menus, dialogue, prompts, loading screens, and HUD elements. A Canvas can pause gameplay, close with Cancel, open another Canvas, or play interface sounds.

Use small textures and reuse them where possible because UI images share the scene's PS2 texture budget.

## Audio

Audio Sources play music, ambience, effects, or spatial sounds. Import settings control sample rate and bit depth. Lower settings reduce file size and streaming cost but also reduce clarity.

Ambient music is a good streaming candidate. Short interface and action sounds should stay small and responsive. An Audio Listener override can follow the player root instead of the camera when that produces more natural spatial audio.
