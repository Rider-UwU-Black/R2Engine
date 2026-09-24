# Welcome to R2Engine

R2Engine is a small game engine for building 3D games that can run on Windows and PlayStation 2. You make scenes in the editor, attach ready-made components for common behavior, test the game, and export it when it feels right.

You do not need to understand the renderer or console hardware to begin. Start with a tiny room, a controllable character, one interaction, and a way to move between scenes.

## A good first-game plan

1. Create a project from the Hub.
2. Import one room or environment model.
3. Add a camera, light, and Player Controller.
4. Give floors and walls colliders.
5. Add a Canvas for a pause menu or interaction prompt.
6. Add an Interactable or scene-loading door.
7. Test in the editor, then PCSX2, then real hardware.

## How R2Engine projects are organized

The Assets folder contains content used by your game: models, textures, materials, audio, animations, scripts, fonts, and prefabs. Scenes contain the objects that make up each playable area. Project Settings contains choices that apply to the whole game, such as its startup scene, input actions, saving identity, and PS2 build options.

## Where to go next

Read Editor and Project Basics for a tour of the editor. Then follow Make a Small Playable Scene. The remaining manual pages explain one part of the workflow at a time.
