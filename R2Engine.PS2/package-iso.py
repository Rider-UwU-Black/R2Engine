"""Package cooked R2 assets with flat ISO9660 names; no HostFS dependency."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import wave

def make_ui_variants(source, output):
    try:
        with wave.open(str(source), 'rb') as reader:
            channels, width, rate, frames = (reader.getnchannels(), reader.getsampwidth(),
                                              reader.getframerate(), reader.getnframes())
            if channels not in (1, 2) or width not in (1, 2) or rate <= 0 or frames / rate > 30.0:
                return []
            raw = reader.readframes(frames)
    except (wave.Error, EOFError):
        return []
    samples = []
    for frame in range(frames):
        total = 0
        for channel in range(channels):
            offset = (frame * channels + channel) * width
            total += ((raw[offset] - 128) << 8) if width == 1 else struct.unpack_from('<h', raw, offset)[0]
        samples.append(int(total / channels))
    target_rate = min(rate, 22050)
    if target_rate != rate:
        count = max(1, len(samples) * target_rate // rate)
        samples = [samples[min(len(samples) - 1, index * rate // target_rate)] for index in range(count)]
        rate = target_rate
    peak = max((abs(sample) for sample in samples), default=1)
    threshold, quiet_needed, padding = max(512, int(peak * .025)), max(1, rate // 12), max(1, rate // 100)
    ranges, start, quiet = [], None, 0
    for index, sample in enumerate(samples):
        if abs(sample) >= threshold:
            if start is None: start = max(0, index - padding)
            quiet = 0
        elif start is not None:
            quiet += 1
            if quiet >= quiet_needed:
                end = min(len(samples), index - quiet + padding)
                if end - start >= rate // 50: ranges.append((start, end))
                start, quiet = None, 0
    if start is not None: ranges.append((start, len(samples)))
    if not ranges: ranges = [(0, len(samples))]
    encoder = shutil.which('adpenc')
    if not encoder:
        sdk = os.environ.get('PS2SDK', '')
        candidate = Path(sdk) / 'bin' / 'adpenc'
        encoder = str(candidate) if candidate.is_file() else None
    if not encoder:
        candidate = Path.home() / '.local' / 'ps2dev' / 'ps2sdk' / 'bin' / 'adpenc'
        encoder = str(candidate) if candidate.is_file() else None
    if not encoder: raise RuntimeError('PS2SDK adpenc was not found for UI sound export.')
    generated = []
    output.mkdir(parents=True, exist_ok=True)
    for variant, (start, end) in enumerate(ranges[:16]):
        wav_path, adp_path = output / f'ui{variant}.wav', output / f'ui{variant}.adp'
        with wave.open(str(wav_path), 'wb') as writer:
            writer.setnchannels(1); writer.setsampwidth(2); writer.setframerate(rate)
            writer.writeframes(struct.pack('<' + 'h' * (end - start), *samples[start:end]))
        subprocess.run([encoder, str(wav_path), str(adp_path)], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        generated.append(adp_path)
    return generated

def package(build, scene):
    tool = shutil.which('genisoimage') or shutil.which('mkisofs')
    if not tool:
        raise RuntimeError('Install genisoimage in the PSBBN WSL distribution before Final export.')
    root = Path(__file__).resolve().parent
    build = Path(build).resolve()
    scene = Path(scene).stem
    data = build / 'R2Data'
    startup = data / 'Scenes' / (scene + '.r2scene')
    if not startup.is_file():
        raise RuntimeError('Missing cooked startup scene: ' + str(startup))
    assets = {}
    def add(key, path):
        key = key.replace('\\', '/').lower()
        if len(key.encode('utf-8')) > 255:
            raise RuntimeError('Disc asset path exceeds 255 bytes: ' + key)
        if key in assets and assets[key] != path:
            raise RuntimeError('Duplicate disc asset path: ' + key)
        assets[key] = path
    add('probe.r2scene', startup)
    for name in ('icon.sys', 'save.icn', 'identity.bin'):
        presentation = data / 'Save' / name
        if not presentation.is_file():
            raise RuntimeError('Missing PS2 save presentation; rebuild in the editor: ' + str(presentation))
        add('Save/' + name, presentation)
    for path in sorted((data / 'Scenes').glob('*.r2scene')):
        if path == startup and path.name.lower() == 'probe.r2scene': continue
        add(path.name, path)
    for directory, extensions in [('Meshes', {'.r2mesh', '.r2anim'}), ('Textures', {'.r2tex'})]:
        for path in sorted((data / directory).rglob('*')):
            if path.is_file() and path.suffix.lower() in extensions:
                add(path.relative_to(data / directory).as_posix(), path)
    for path in sorted((build / 'Assets' / 'Audio').rglob('*')):
        if path.is_file(): add(path.relative_to(build).as_posix(), path)
    if len(assets) > 2048: raise RuntimeError('Disc asset map exceeds 2048 entries.')
    output = build / 'PS2-Final'
    output.mkdir(parents=True, exist_ok=True)
    iso = output / ('R2GM_000.01.' + scene + '.iso')
    manifest = []
    with tempfile.TemporaryDirectory(prefix='iso-', dir=output) as temporary:
        stage = Path(temporary)
        shutil.copyfile(root / 'bin/r2engine-ps2-final.elf', stage / 'R2GM_000.01')
        shutil.copyfile(root / 'bin/assets/audsrv.irx', stage / 'AUDSRV.IRX')
        (stage / 'SYSTEM.CNF').write_bytes(b'BOOT2 = cdrom0:\\R2GM_000.01;1\r\nVER = 1.00\r\nVMODE = NTSC\r\n')
        converted = stage / 'ui-audio'
        for key, source in list(assets.items()):
            if source.suffix.lower() != '.wav': continue
            variants = make_ui_variants(source, converted / hashlib.sha1(key.encode()).hexdigest())
            for index, variant in enumerate(variants):
                add(key + f'.ui{index}.adp', variant)
        with (stage / 'PATHS.BIN').open('wb') as mapping:
            mapping.write(b'R2DM' + struct.pack('<I', len(assets)))
            for index, (key, source) in enumerate(sorted(assets.items())):
                name = f'A{index:07d}.BIN'
                shutil.copyfile(source, stage / name)
                mapping.write(struct.pack('<256s16s', key.encode('utf-8'), name.encode('ascii')))
                manifest.append({'asset': key, 'disc': name, 'bytes': source.stat().st_size,
                                 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
        candidate = stage / 'output.iso'
        # Output must not live inside the input directory while mastering it.
        content = stage / 'disc'
        content.mkdir()
        for path in list(stage.iterdir()):
            if path.is_file(): path.rename(content / path.name)
        subprocess.run([tool, '-iso-level', '1', '-V', 'R2ENGINE', '-o', str(candidate), str(content)], check=True)
        # Verify every file is readable from the ISO, not merely the staging tree.
        reader = shutil.which('isoinfo')
        if not reader: raise RuntimeError('isoinfo is required to verify Final exports.')
        for path in content.iterdir():
            extracted = subprocess.check_output([reader, '-i', str(candidate), '-x', '/' + path.name + ';1'])
            if hashlib.sha256(extracted).digest() != hashlib.sha256(path.read_bytes()).digest():
                raise RuntimeError('ISO verification failed: ' + path.name)
        candidate.replace(iso)
    (output / 'asset-map.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    (output / 'README.txt').write_text(
        'R2Engine PS2 homebrew ISO\n\n'
        'Boot the ISO as a disc image in PCSX2 (not the HostFS ELF) first.\n'
        'Hardware requires a compatible homebrew loader/modification. This is not a retail-signed disc.\n'
        'OPL compatibility depends on your setup; this small ISO is mastered as ISO9660, not DVD-Video.\n'
        'All cooked scenes, meshes, animation, textures, and exported audio are included.\n'
        'Save data still uses memory card slot 1. Back up the card before first hardware testing.\n'
        'NTSC video. Packaging verification does not certify hardware compatibility or performance.\n', encoding='utf-8')
    print(f'Verified {len(assets)} mapped assets. ISO: {iso}', flush=True)

if __name__ == '__main__':
    try: package(sys.argv[1], sys.argv[2])
    except Exception as error:
        print('PS2 Final export failed: ' + str(error), file=sys.stderr)
        sys.exit(1)
