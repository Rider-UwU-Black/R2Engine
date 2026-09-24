# Add a Talking NPC

This example creates a simple conversation that opens when the player approaches an NPC and presses Interact.

## Create the dialogue Canvas

1. Add a Canvas and give it a clear name such as NPC Dialogue.
2. Disable Starts Visible so it is hidden during normal play.
3. Add a panel and UIText for the conversation.
4. Enable Close With Cancel Input if the player should be able to leave early.
5. Enable Pause Gameplay if the rest of the scene should stop during the conversation.

## Make the NPC interactive

1. Select the NPC or another object near it.
2. Add the Interactable component.
3. Drag the dialogue Canvas into Open Canvas.
4. Set the prompt to something short, such as Talk.
5. Keep the Input Action set to Interact unless the project uses another button.
6. Edit the trigger Box Collider so it covers the area where the prompt should appear.

Enter one dialogue page per line in Dialogue Pages. If that field is empty, the authored UIText is used as a single page.

Press Play, approach the NPC, and use Interact. Check that movement is locked while the conversation is open and restored when it closes.
