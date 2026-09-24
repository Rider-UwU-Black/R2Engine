# PS2 memory-card presentation

Project Settings > Build contains:

- **PS2 Memory Card Title**: one line, up to 32 full-width Shift-JIS characters. Ordinary Latin letters are converted automatically. Blank uses Product Name. Unsupported characters fail export with an explanation.
- **PS2 Memory Card Icon**: a native PS2 3D icon (`.icn`, `.ico`, or `.icon`). Put it under Assets and choose it from the dropdown, drag it onto the field, or enter its path. This is not a Windows ICO, PNG, or arbitrary game model. Blank uses an original static teal gem.

Build and save in-game once to update the memory-card browser entry. Rebuilding alone does not modify a memory card. No card formatting or deletion of existing progress is required.

The exporter validates native icon geometry/animation lengths and its 128x128 raw or RLE texture. Both HostFS and final disc builds include `R2Data/Save/icon.sys` and `save.icn`. The runtime compares existing presentation files before writing, installs the icon first and metadata last, and reports failure before replacing checkpoint data if presentation cannot be installed.

New saves and presentation files live under `/R2-<Save ID>`. Slot names, checksummed payload and recovery-copy behavior are unchanged. `identity.bin` is included in HostFS and final ISO builds; missing or invalid identity disables saving/loading rather than silently writing the shared directory. Titles and icon changes do not change save identity.

Migration playtest: keep the generated ID, enable legacy import, rebuild, load the old slot, and save. Restart the console and load again. Then disable legacy import and rebuild to verify that the new copy stands alone. Keep the original card entry until this is confirmed. A fresh game with import disabled must not see old shared saves.

Checkpoint payloads also preserve every cooked Sliding Door's current lift progress and whether it was opening. Loading a save made while a door is moving restores the intermediate height and resumes it; a fully open door remains open. The loader also accepts the older checkpoint size from before door state was added. Door matching currently uses its cooked scene index plus authored closed position, so reorganizing a released scene can intentionally invalidate that door record rather than applying it to the wrong door.

Translated scripted objects can opt into checkpoints by adding `PersistentObject` and assigning a unique `Save ID`. `Save Transform` preserves the object's translated position/rotation/scale. `Save Boolean` additionally preserves the private state of bool-toggle scripts. `Save Integer` preserves one-shot or repeating timer phase with millisecond precision. The exporter rejects Boolean/Integer options on incompatible script forms; input edge latches and trigger-overlap latches are intentionally reconstructed after loading rather than persisted. A repeating timer phase is normalized to its current cycle when restored.

Automated coverage: `dotnet run --project Tests/Ps2SavePresentation`. Browser appearance and card-write behavior still need PCSX2 and hardware validation.

Format references: [PS2SDK memory-card sample](https://github.com/ps2dev/ps2sdk/blob/master/ee/rpc/memorycard/samples/mc_example.c), [PS2IconSys format implementation](https://github.com/ticky/ps2iconsys/blob/develop/include/ps2_ps2icon.hpp). The fallback geometry is original engine-generated content.
- **PS2 Save ID**: generated and saved once when the editor first opens a project without an ID. Accepts 1–20 uppercase letters, digits, underscores or hyphens. Keep it stable across updates; changing it selects a different save folder. Duplicated projects inherit the ID, so assign a different one before releasing a different game.
- **Import Legacy R2ENGINE Saves**: off by default. Enable only for the project that owns the old shared saves. Load reads the legacy slot only if both the new `.SAV` and `.TMP` are absent. Corruption or other read errors do not trigger legacy fallback. After loading, save normally to create a copy in the new folder. The old folder is never deleted or modified.
