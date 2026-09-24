# Interactables

Add **Interactable** to an NPC or another scene object. This version opens an authored Canvas, suitable for a simple conversation, sign, or object-information panel. It does not implement dialogue trees, text paging, quest logic, or door movement.

## Example NPC conversation

1. Create a Canvas named `NPC Dialogue`. Disable Starts Visible and Toggle With Menu Input. Enable Close With Cancel Input. `Pause Gameplay` remains optional: an active interaction always locks player and orbit-camera controls, while that checkbox decides whether the rest of the scene simulation also pauses.
2. Add a panel and UIText for the dialogue. Optionally add a Close Menu button.
3. On the NPC, choose Add Component → Interactable. This adds a trigger Box Collider if needed. Drag the dialogue Canvas onto Open Canvas.
4. Set Prompt to `Talk` and keep Input Action `Interact`. Edit Box Collider Center and Size to define exactly where the prompt is available.
5. Enter one dialogue page per line in **Dialogue Pages**. The first UIText owned by the target Canvas displays those pages. Leave the field empty to use that UIText's authored text as a single page.
6. Save the scene and rebuild. Approach the NPC and press **E** (keyboard) or **Cross** (default gamepad mapping). Press it again for each next page. Pressing it on the final page closes the conversation and restores player control. Circle closes early when enabled; Escape closes it on desktop.

Animator trigger zones and Interactable components can coexist on the same NPC. Talking and proximity animation triggers remain separate features.

`Interact` has an E/Cross fallback binding for existing projects. To override it, create a Button action named Interact in Project Settings, or assign another configured Button action in the component. PS2 needs a gamepad button binding. The prompt shows the binding hint and authored text. Cross still jumps outside interaction range; the opening press is consumed near an eligible interactable so it doesn't also jump or activate the first dialogue button.

Entering the Box Collider displays the prompt; staying inside does not execute anything. Pressing the action opens the Canvas and starts an interaction session. Player movement, jumping, turning, and orbit-camera input remain locked while the root dialogue Canvas or one of its nested submenu Canvases is visible. Closing the final Canvas ends the session and restores gameplay automatically. Exiting before interacting hides the prompt. The closest collider wins if trigger volumes overlap. This is not a line-of-sight test. Pausing/interactive menus and an already-open interaction Canvas suppress further interactions. Holding the button does not repeatedly activate it. Canvas names must be unique and references must be updated if renamed.

PS2: up to 64 Interactables and 8 dialogue pages per Interactable per scene. Each page fits 63 UTF-8 bytes. Trigger boxes follow rendered instances; renderer-less interaction points use their cooked positions. Runtime resizing/moving of renderer-less boxes is not supported. Prompts fit 63 UTF-8 bytes including the button hint; the existing PS2 bitmap font's character support still applies. Scenes with interactions use R2SC v27. Scenes without them retain v24/v25. Rebuild the game after editing these components.

Tests cover serialization, export offsets and input binding, range selection, prompt creation, dialogue opening, consumed input, held input and cancellation. Console appearance and play feel still require a PCSX2/hardware test.
