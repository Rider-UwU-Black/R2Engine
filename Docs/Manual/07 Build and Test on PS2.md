# Build and Test on PS2

Set a Startup Scene in Project Settings before building. Save the open scene so the exporter receives the latest changes.

## Test in stages

1. Use editor Play mode for quick interaction and layout checks.
2. Build and launch in PCSX2 to test the cooked PS2 version.
3. Use the network PS2 build for fast testing on real hardware.
4. Create a final self-contained ISO when the game is ready to archive or share.

The Hub Settings page stores the PCSX2 executable location for this computer. The project's network build folder remains in Project Settings because it belongs to that project and console setup.

## Before a console test

- Check that every required file is inside the project.
- Review warnings and errors in the Console.
- Make sure the startup scene loads in PCSX2.
- Check the Performance window for texture VRAM, lights, triangles, and skinned meshes.
- Save and close the editor if Windows reports that the executable is locked during an engine rebuild.

## Real hardware matters

PCSX2 is convenient, but it cannot replace hardware testing. Check controls, saves, audio streaming, loading, aspect ratio, collision, and frame pacing on the console. Use the development quit shortcut to return to the PS2 launcher during repeated tests.
