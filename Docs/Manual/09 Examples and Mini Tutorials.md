# Examples and Mini Tutorials

These small tutorials add one useful feature at a time. They are designed to fit into almost any game rather than requiring a specific sample project.

## Good first examples

- Add a Talking NPC creates an interaction area, prompt, and simple conversation.
- Make a Door Between Scenes lets the player approach a door, press Interact, and arrive at a chosen destination.
- Make a Pause Menu creates a Canvas that pauses gameplay and can return to the game.
- Add a Save Point gives the player a place to save supported game state.

Build each feature in a small test scene first. Once it behaves correctly in editor Play mode, copy the setup into the real level and test it in a PS2 build.

## A useful testing habit

Change one thing at a time. If a door does not work, first confirm that its trigger sees the player. Then confirm the Interact input. Finally confirm the destination scene and spawn name. Breaking a feature into small checks is much faster than changing everything at once.
