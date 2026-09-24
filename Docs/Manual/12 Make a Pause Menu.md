# Make a Pause Menu

Use a Canvas for a pause menu that can stop gameplay without unloading the scene.

## Build the menu

1. Add a Canvas and name it Pause Menu.
2. Disable Starts Visible.
3. Enable Toggle With Menu Input.
4. Enable Pause Gameplay.
5. Add a background panel, title text, and the buttons you need.

A Resume button should close the current Canvas. Other buttons can open nested Canvases for options, controls, or confirmation prompts.

## Add menu sounds

Assign UI sound collections for movement, selection, confirmation, opening, and closing. A collection may contain several variations; R2Engine can choose among them so repeated menu actions do not sound identical.

## Test the behavior

Press Play and open the menu using the configured Menu input. Confirm that the player, camera, animations, and gameplay pause as intended. Close it with Resume or Cancel and confirm that control returns immediately.

Keep pause-menu textures small, because they count toward the active scene's PS2 texture budget.
