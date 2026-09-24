# Editor and Project Basics

Open a project from the Hub. The editor remembers its window arrangement, so you can organize it around the way you work.

## The main windows

- Hierarchy lists every object in the open scene. Parent objects can organize related children.
- Scene is where you position and inspect the world.
- Game shows what the active camera sees.
- Inspector shows settings for the selected object or project file.
- Project shows files inside the project. Folders here behave like normal project folders and can be renamed or reorganized.
- Console reports imports, script compilation, build progress, warnings, and errors.
- Performance shows scene costs such as draw calls, triangles, textures, lights, and skinned meshes.

## Objects and components

A GameObject is a thing in the scene. Its Transform stores position, rotation, and scale. Components give it abilities: a Mesh Renderer makes it visible, a Collider makes it solid, an Audio Source plays sound, and scripts add gameplay behavior.

Select an object and use Add Component in the Inspector. You can enable or disable the whole object with its checkbox. Mark environment objects Static when they will not move and should participate in baked lighting.

## Saving safely

Save the scene regularly. A scene stores object setup, while imported files stay in Assets. Moving files through the Project window updates references more safely than moving them behind the editor in File Explorer.
