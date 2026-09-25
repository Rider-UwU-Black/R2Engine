# R2Engine Quick Start

This guide takes you from the portable download to your first saved R2Engine scene. You do not need Visual Studio or the .NET SDK to open the Hub, use the editor, or test a scene inside the editor.

## 1. Download and extract R2Engine

1. Download the newest portable ZIP from the [R2Engine Releases page](https://github.com/Rider-UwU-Black/R2Engine/releases).
2. Right-click the downloaded ZIP, choose **Properties**, check **Unblock** if that option is present, and select **Apply**. Do this before extracting so Windows does not carry the internet security marker into the Editor and Player executables.
3. Extract the entire ZIP to a normal folder such as `Documents\R2Engine`.
4. Do not run R2Engine from inside the ZIP preview.
5. Open the extracted folder and run `R2Engine.Hub.exe`.

R2Engine is currently an unsigned early release. Windows may show a security warning for a newly downloaded executable. Confirm that the file came from the official R2Engine repository before choosing to run it.

The portable build keeps its Hub preferences and project list in its own `UserData` folder. Your games remain in the project location you choose.

## 2. Create a project

1. Select **New Project** in the Hub.
2. Give the project a short name.
3. Choose where the project should live. The default is an `R2Engine Projects` folder inside your Documents folder.
4. Create the project and allow the Hub to open it in the editor.

Each project is separate from the engine. Its `Assets` folder contains game content, while `Scenes` contains the playable areas you create.

## 3. Learn the editor layout

- **Hierarchy** lists the objects in the current scene.
- **Scene** is where you arrange the world.
- **Game** shows what the active camera sees.
- **Inspector** edits the selected object and its components.
- **Project** displays the files belonging to the game.
- **Console** reports imports, warnings, build progress, and errors.

Select an object in the Hierarchy to inspect it. Use the transform tools above the Scene view to move, rotate, and scale it.

## 4. Make and save a first scene

1. Choose **File → New Scene**.
2. Add a simple object from the **GameObject** menu.
3. Select it and adjust its position in the Scene view.
4. Choose **File → Save Scene As**.
5. Save the scene inside the project's `Scenes` folder.

Press `Ctrl+S` regularly while working.

For a complete beginner project with a camera, lighting, collision, and player controls, continue with [Make a Small Playable Scene](Docs/Manual/03%20Make%20a%20Small%20Playable%20Scene.md).

## 5. Test the scene

Select the **Play** button at the top of the editor, or press `Ctrl+P`. Select it again to stop.

Changes made while the game is playing are temporary and are discarded when Play Mode stops. Stop the game before making changes you want to keep.

## 6. Import your own content

Use the Project window to organize files in `Assets`. R2Engine projects can contain models, textures, materials, audio, animation, fonts, prefabs, and C# gameplay scripts.

Moving or renaming imported files through the Project window is safer than reorganizing them behind the editor in File Explorer because the editor can update references.

## 7. Optional PS2 setup

You can learn the editor and test scenes on Windows without installing any PS2 tools.

When you are ready to test a cooked PS2 build:

1. Install PCSX2.
2. Open **Hub Settings → External Tools**.
3. Select the PCSX2 executable.
4. Follow [Build and Test on PS2](Docs/Manual/07%20Build%20and%20Test%20on%20PS2.md).

Native PS2 compilation requires the separate PS2DEV toolchain. Real hardware also requires an appropriate homebrew launch method.

## If something goes wrong

- Make sure the ZIP was fully extracted before running the Hub.
- Keep the portable folder somewhere your Windows account can write to.
- Check the editor Console for the first warning or error.
- Reopen the project through the Hub rather than launching the Editor executable directly.
- Read [Troubleshooting](Docs/Manual/08%20Troubleshooting.md) for common editor and build issues.

For the full guide, browse the [R2Engine Manual](Docs/Manual).
