# UI sprite sheets

Import your sprite sheet into the project's Assets. Select a Canvas and use **Add UI Element → Image**, then drag the sheet into its **Texture** field.

Set **Sprite Region (X Y W H)** to the icon's rectangle in original image pixels, measured from the top-left. For example, X=32, Y=0, W=32, H=32 selects the top-right cell of a 64×64 sheet. The inspector previews the selected crop. **Set Size To Sprite Region** sets the UI element's display size to the cropped pixel dimensions; you can resize it afterward.

Duplicate the Image and choose another region to reuse the same atlas. **Use Whole Image**, or width/height zero, restores the old full-texture behavior. Regions persist in scenes and prefabs and render in Scene view, Game preview, Windows and PS2. Cropped images also support nine-slice borders, measured inside the selected region.

PS2 packages store normalized crop coordinates so texture resizing during cooking preserves the selection. Multiple regions share one texture slot. Leave a little padding between sprites when using bilinear filtering to reduce neighboring-sprite bleeding, or use nearest filtering for pixel art. This feature crops UI Images; button-state sprite fields still select whole textures.
