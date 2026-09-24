# Gameplay, Saving, and Scenes

R2Engine includes reusable components for common small-game behavior.

## Interactions

Interactable lets the player approach an object and press the configured Interact button. It can open an authored Canvas for dialogue, signs, prompts, or object information. The interaction area comes from a trigger collider.

Scene-loading interactions can move the player into another scene or reload a scene at a named destination. This is useful for doors, entrances, and travel points. Keep destination names unique and test both directions.

## Triggers and scripts

Trigger colliders detect entry and exit without acting as solid walls. Animator Trigger Zone can start an animation when the player enters. Custom scripts can respond to input, timers, collisions, and scene events. Build errors should identify script features that are not yet supported by the PS2 translator.

## Saving

Save points and persistent objects store supported game state. Loading a save restores saved positions and values. Reloading a scene without loading a save starts from the scene's authored state.

Project Settings contains the PS2 save title, identifier, and memory-card icon. Configure these before sharing a build so the save appears as valid named data in the PS2 Browser.

## Safety objects

A kill floor can respawn the player at the current scene's original spawn if they fall out of the level. Place it far enough below the playable area that intentional lower floors remain usable.
