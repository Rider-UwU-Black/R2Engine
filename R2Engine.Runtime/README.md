# R2Engine Runtime Boundary

`R2Engine.Runtime` contains contracts that runtime code may use without depending on
the editor, ImGui, Silk.NET, OpenGL, or OpenAL.

The current desktop editor and player configure `DesktopPlatformServices`, which
supplies managed files, timing, logging, input polling, and names the active desktop
render/audio backends. Core asset reads already use this boundary.

The runtime assembly now owns the platform-neutral scene graph and script-facing
foundation: scenes, game objects, transforms, cameras, lights, material data,
colliders, runtime input, scene-load requests, skeleton bones, component bases, and
script component lifecycle. Their existing `R2Engine.Editor.Scene` namespace is
temporarily retained so saved projects and user scripts remain source-compatible.

Physics is runtime-owned as well: mesh and primitive data, mesh-renderer components,
rigid bodies, collision/trigger solving, static triangle collision, and the built-in
character controller. Desktop import caches supply mesh/material assets through
`RuntimeAssetServices`; runtime components no longer depend on those caches directly.

Animation playback is runtime-owned: transform clips, Animator Controllers and
parameters, skeletal/humanoid animation data, retargeting, crossfades, and animation
events. Importers and configuration windows remain desktop tooling, while runtime
playback resolves their produced assets through the same provider boundary.

Scene rendering is selected through `IRuntimeRenderBackend` and
`IRuntimeSceneRenderer`. The standalone player submits runtime Scene, GameObject,
Camera, and output-size data through that neutral contract. OpenGL and its Silk.NET
types remain entirely inside the desktop host.

Audio components use `IRuntimeAudioBackend` and `IRuntimeAudioSystem`. Runtime-owned
`AudioSource` sends playback commands through the selected platform service and holds
no native handles. OpenAL source/buffer state, listener tracking, WAV decoding, and
spatial attenuation remain in the desktop host.

Migration order:

1. Continue turning the desktop host's shared source links into permanent backend-owned
   files as renderer and audio contracts mature. The standalone player already has no
   reference to the editor assembly.
2. Scene rendering and audio playback are operational backend contracts.
3. Keep OpenGL/OpenAL/Silk implementations in a desktop platform project.
4. Implement PS2 filesystem, timing, input, audio, and rendering backends without
   importing editor or desktop dependencies.
