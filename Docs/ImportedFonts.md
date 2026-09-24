# Imported UI Fonts

R2Engine converts desktop `.ttf` and `.otf` files into a game-ready `.r2font` asset and a PS2-friendly texture atlas. The source font is not needed at runtime.

## Import and assign

1. Drag a `.ttf` or `.otf` file from File Explorer into the Project window. It is converted into `Assets/Fonts`. Alternatively, open the Fonts folder, right-click empty space, and choose Import.
2. Select a GameObject with **UI Text**.
3. Drag the resulting `.r2font` asset onto its **Font** field.
4. Adjust **Font Size**, save, and rebuild.

The generated `.font.png` atlas is intentionally hidden in the Project window. It remains a normal build dependency and is copied/cooked automatically. Selecting no font keeps the original built-in bitmap font, so existing scenes remain compatible.

The initial atlas contains printable ASCII characters 32–126, preserving uppercase and lowercase. Unsupported characters use `?`. Font assets re-bake automatically when their linked source file changes.

PS2 font atlases count toward the existing limit of 16 scene textures. The current first version uses fixed-width atlas cells; proportional spacing, wrapping, alignment, outlines, and expanded Unicode ranges are future text-layout features.
