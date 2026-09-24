# Nested menus

1. Create a Canvas named `SettingsCanvas` in the same scene as your main menu. Add your settings UI beneath it.
2. On SettingsCanvas, disable **Starts Visible** and **Toggle With Start / Escape**. Enable **Close With Cancel / Circle**. Configure **Pause Gameplay When Visible** independently.
3. Select your main menu's Settings button. Set **Action → OpenSubmenu**, then **Target Canvas → SettingsCanvas**.
4. Optionally add a Back button with **Action → GoBack**. It needs no target.

OpenSubmenu hides its parent and shows the child. Circle (or Escape on desktop) returns one level and restores focus to the opening button. GoBack also returns one level, even when automatic Cancel is disabled. Your main menu can keep both automatic input settings off and cannot be dismissed at the root.

Use further OpenSubmenu buttons for Settings → Audio → advanced options. History belongs to the current runtime scene and disappears when the scene changes. A maximum of 31 nested transitions is supported, matching the PS2's 32-canvas scene budget. Self-links, loops to an ancestor, and already-visible targets do not push history.

**OpenCanvas** retains its original behavior: show another canvas without navigation history. Use **OpenSubmenu** when you want a return path. This feature supplies navigation, not volume/settings functionality; configure those separately.
