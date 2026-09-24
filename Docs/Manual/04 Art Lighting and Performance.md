# Art, Lighting, and Performance

PS2 hardware rewards simple, deliberate art. A scene can still look distinctive without using enormous textures or extremely dense models.

## Models and materials

Use separate material slots when different parts of a model need different surfaces. Double-sided rendering is useful for rooms modeled from the inside, but leave it off when normal back-face culling works.

Texture Tiling repeats a texture across a surface. Offset slides it without changing the model's UV map. Use these controls for floors and walls instead of creating unnecessarily large images.

## Texture choices

Large source images can be imported, but the PS2 build must convert and reduce them. Pick the smallest size that still looks good in the game camera. A 4K image is rarely useful for a small background object.

## Lighting

Use Realtime for lights that must change or affect moving characters. Use Baked for unmoving environment lighting. Use Mixed when a light should bake onto static scenery and still illuminate selected realtime layers.

## Watch the Performance window

Pay attention to draw calls, visible objects, triangles, texture VRAM, active lights, and skinned meshes. Test on PCSX2 and real hardware regularly. A character mesh with too many skinned vertices can cost far more than several simple room objects.
