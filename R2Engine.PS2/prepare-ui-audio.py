"""Generate short PS2 ADPCM variation assets beside cooked WAV sound sheets."""
import importlib.util
from pathlib import Path
import sys

root = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('r2_package_iso', root / 'package-iso.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

audio_root = Path(sys.argv[1]).resolve()
scratch = audio_root / '.r2-ui-work'
for wav in audio_root.rglob('*.wav'):
    if scratch in wav.parents:
        continue
    variants = module.make_ui_variants(wav, scratch / wav.stem)
    for index, variant in enumerate(variants):
        target = Path(str(wav) + f'.ui{index}.adp')
        target.write_bytes(variant.read_bytes())
if scratch.exists():
    import shutil
    shutil.rmtree(scratch)
