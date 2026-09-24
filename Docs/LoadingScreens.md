# Reusable loading screens

In **Project Settings**, click **Create Loading Screen Prefab**. This creates and assigns a new prefab without changing your scenes. Alternatively select a Canvas prefab in the asset browser and click **Use Selected Prefab**, or drop it into **Loading Screen Prefab**.

Instantiate the prefab temporarily in a scene to customize its panel, images, text and layout using the existing canvas tools. Use **Apply Prefab** when finished, then remove the editing instance from the scene. The runtime supplies its own hidden copy in every scene; no transition scripts need changing. Rebuild after editing the prefab.

Use exactly one active Canvas with only UI children. Panel, Image, Text and Button visuals are supported, including sprite textures and nine-slice borders. Buttons are passive decoration on the loading screen. An opaque full-screen panel is recommended. Gameplay, scripts and input do not run during loading. Starts Visible and Pause Gameplay on this prefab do not control its runtime visibility: transitions do.

The editor, Windows player and PS2 show the prefab before scene transitions. On PS2 both framebuffers are populated before blocking I/O and resource replacement. This is a **static** screen, not an animated or percentage-progress loader, and it does not make the load itself faster. Initial cold boot has no outgoing scene from which to display the prefab. Cross-scene checkpoint loads use the native scene-loading path too.

The loading screen shares each PS2 scene's budgets: at most 32 canvases, 64 UI elements and 16 unique textures total, plus available VRAM. Prefer small/reused textures. Builds report missing assets or invalid prefabs instead of silently dropping the screen.

Click **Disable Loading Screen** to return to direct scene transitions. Authored scene files are never modified by loading-screen injection.
