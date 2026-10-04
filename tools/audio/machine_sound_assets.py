"""Reproducible original machine sounds; no recordings or network inputs."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
RATE = 24000
DURATION = 240
OUTPUT = ROOT / 'assets/gearwright/sounds/machines'
MANIFEST = Path(__file__).with_name('machine-sounds.manifest.json')
ACCEPTED_LOOPS = ('airflow', 'bellows', 'water-pipe', 'water-outlet', 'water-sprinkler', 'water-irrigator')
ACCEPTED_EVENTS = ('sender-work', 'receiver-work', 'router-turn', 'pressure-creak1', 'pressure-creak2', 'pressure-creak3',
                   'router-prepare', 'outlet-arrival')
ACCEPTED_VARIED = ('sender-work', 'receiver-work', 'router-turn', 'router-prepare')
CONTACTS = ('pump-valve', 'ratchet-pawl', 'ratchet-engage')
LOOPS = ACCEPTED_LOOPS + ('flywheel', 'pump-mechanism', 'transmission-bearing')
EVENTS = ACCEPTED_EVENTS + CONTACTS
WORK_SECONDS = {'sender-work': 1.6, 'receiver-work': 1.6, 'router-turn': 1., 'router-prepare': .4,
                'outlet-arrival': .9, 'pump-valve': .14, 'ratchet-pawl': .10, 'ratchet-engage': .40}
VARIED_WORK = ACCEPTED_VARIED + CONTACTS
WORK_VARIATIONS = 4
EVENT_ASSETS = EVENTS + tuple(f'{name}-{variant}' for name in VARIED_WORK for variant in range(2, WORK_VARIATIONS + 1))
ACCEPTED_SEED_ORDER = ACCEPTED_LOOPS + ACCEPTED_EVENTS + tuple(
    f'{name}-{variant}' for name in ACCEPTED_VARIED for variant in range(2, WORK_VARIATIONS + 1))


def event_kind(name: str) -> str:
    return name.rsplit('-', 1)[0] if name not in EVENTS else name


def vorbis_encoders() -> list[str]:
    import av
    available = []
    for name in ("libvorbis", "vorbis"):
        if name not in av.codecs_available:
            continue
        try:
            av.Codec(name, "w")
        except av.codec.codec.UnknownCodecError:
            continue
        available.append(name)
    return available


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def builder_digest() -> str:
    # Source checkouts may use CRLF or LF; both represent the same generator.
    return hashlib.sha256(Path(__file__).read_text(encoding='utf-8').encode('utf-8')).hexdigest()


def noise(seconds: float, rng: np.random.Generator, low: float, high: float) -> np.ndarray:
    count = round(seconds * RATE)
    frequencies = np.fft.rfftfreq(count, 1 / RATE)
    response = (1 - np.exp(-(frequencies / low) ** 2)) / np.sqrt(1 + (frequencies / high) ** 8)
    values = np.fft.irfft(np.fft.rfft(rng.normal(size=count)) * response, n=count)
    return values / max(float(np.sqrt(np.mean(values ** 2))), 1e-9)


def drift(count: int, rng: np.random.Generator, knots: int, low: float, high: float) -> np.ndarray:
    values = rng.uniform(low, high, knots)
    return np.interp(np.arange(count), np.linspace(0, count, knots + 1), np.r_[values, values[0]])


def soften(values: np.ndarray, attack: float = .012, release: float = .05) -> np.ndarray:
    for seconds, front in ((attack, True), (release, False)):
        count = min(round(seconds * RATE), len(values) // 2)
        ramp = np.sin(np.linspace(0, np.pi / 2, count)) ** 2
        if front:
            values[:count] *= ramp
        else:
            values[-count:] *= ramp[::-1]
    return values


def add(values: np.ndarray, event: np.ndarray, offset: int) -> None:
    # Wrapping events rather than truncating them preserves long-loop continuity.
    end = offset + len(event)
    if end <= len(values):
        values[offset:end] += event
    else:
        size = len(values) - offset
        values[offset:] += event[:size]
        values[:len(event) - size] += event[size:]


def contact(material: str, rng: np.random.Generator, strength: float = 1, seconds: float = .085) -> np.ndarray:
    """Damped, inharmonic modes excited by contact, rather than a noise bed."""
    t = np.arange(round(seconds * RATE)) / RATE
    modes = {
        'iron': ((286, .035, .65), (647, .022, .42), (1137, .014, .20), (1883, .009, .07)),
        'brass': ((417, .032, .60), (923, .022, .36), (1561, .013, .13), (2473, .008, .04)),
        'wood': ((143, .028, .85), (317, .016, .38), (563, .010, .12)),
        'leather': ((78, .026, 1), (157, .013, .30), (271, .009, .08)),
    }[material]
    result = np.zeros(len(t))
    size = rng.uniform(.93, 1.07)
    for frequency, decay, gain in modes:
        result += gain * np.sin(2 * np.pi * frequency * size * t) * np.exp(-t / (decay * rng.uniform(.85, 1.15)))
    return soften(result * strength, .0025, .012)


def gear_contacts(values: np.ndarray, rng: np.random.Generator, start: float, end: float,
                  teeth: int, gain: float, eased: bool = True) -> None:
    # Teeth are spaced in angular/tray displacement. Invert the model's smooth
    # stroke so contacts accelerate and decelerate with the actual mechanism.
    positions = (np.arange(teeth) + rng.uniform(.25, .7)) / teeth
    if eased:
        curve = np.linspace(0, 1, 2001)
        positions = np.interp(positions, curve * curve * (3 - 2 * curve), curve)
    for index, position in enumerate(positions):
        time = np.clip(start + (end - start) * position + rng.uniform(-.003, .003), start, end - .025)
        pulse = contact('iron', rng, gain * rng.uniform(.65, 1))
        pulse += contact('brass', rng, gain * rng.uniform(.25, .45))
        add(values, pulse, round(time * RATE))
        # Backlash and a loose frame give occasional secondary contacts.
        if index % 3 == 1:
            add(values, contact('wood', rng, gain * .20, .055), round((time + .017) * RATE))


def flex(values: np.ndarray, rng: np.random.Generator, start: float, end: float, gain: float) -> None:
    """Short stick-slip chirrs: changing resonances with gaps between releases."""
    offset = start + rng.uniform(.015, .03)
    while offset + .05 < end:
        duration = rng.uniform(.035, .055)
        t = np.arange(round(duration * RATE)) / RATE
        frequency = rng.uniform(230, 390)
        phase = 2 * np.pi * (frequency * t + rng.uniform(-.28, .28) * frequency * t ** 2 / duration)
        chirr = (np.sin(phase) + .30 * np.sin(phase * 2.37) + .08 * np.sin(phase * 4.13))
        chirr *= np.exp(-t / rng.uniform(.009, .017)) * gain * rng.uniform(.5, 1)
        add(values, soften(chirr, .004, .012), round(offset * RATE))
        offset += rng.uniform(.065, .14)


def air_breath(seconds: float, rng: np.random.Generator, gain: float = 1) -> np.ndarray:
    # Broad, low pressure resonances move through the breath. No constant
    # high-frequency hiss or narrow sustained whistle.
    count = round(seconds * RATE)
    phase = np.linspace(0, 1, count)
    body = noise(seconds, rng, rng.uniform(70, 110), rng.uniform(390, 550))
    edge = noise(seconds, rng, 180, rng.uniform(650, 900))
    sweep = np.sin(np.pi * phase) ** rng.uniform(.8, 1.6)
    envelope = np.sin(np.pi * phase) ** rng.uniform(1.1, 2.2)
    texture = body * (1 - .35 * sweep) + edge * .23 * sweep
    texture *= drift(count, rng, max(5, round(seconds * 9)), .7, 1.05)
    return soften(texture * envelope * gain, .04, .07)


def make_loop(name: str, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    if name in ('flywheel', 'pump-mechanism', 'transmission-bearing'):
        values = np.zeros(DURATION * RATE)
        # Bearings and wooden frames settle in changing, damped clusters. There
        # is no sustained oscillator or broadband rubbing underneath them.
        offset = 0.
        while offset < DURATION:
            duration = rng.uniform(.24, .65)
            flex(values, rng, offset, min(offset + duration, DURATION),
                 .009 if name == 'flywheel' else .019)
            pulse = contact('wood', rng, rng.uniform(.018, .045), .11)
            pulse += contact('brass', rng, rng.uniform(.007, .018), .11)
            add(values, pulse, round(offset * RATE))
            if name == 'flywheel':
                # A large stone rim and eight broad oak spokes move a little
                # low, diffuse air. Runtime gain follows their live rotation.
                add(values, air_breath(duration, rng, rng.uniform(.025, .055)), round(offset * RATE))
            elif name == 'pump-mechanism':
                add(values, contact('leather', rng, rng.uniform(.025, .065), .15),
                    round((offset + duration * .6) * RATE) % len(values))
            offset += rng.uniform(.22, .8) if name != 'flywheel' else rng.uniform(.35, 1.1)
        values -= values.mean()
        # Keep all contacts rounded and leave gaps; source normalization is
        # separate from the deliberately lower in-game bearing gain.
        gain = min(.08 / max(np.sqrt(np.mean(values ** 2)), 1e-9), .42 / max(np.max(np.abs(values)), 1e-9))
        return values * gain
    if name in ('airflow', 'bellows'):
        values = noise(DURATION, rng, 90, 420) * .022
        # Nonperiodic overlapping breaths evolve in duration, timbre and
        # pressure for all four minutes; this is source content, not pitch jitter.
        offset = 0.
        while offset < DURATION:
            seconds = rng.uniform(1.2, 3.8) if name == 'airflow' else rng.uniform(.45, 1.2)
            add(values, air_breath(seconds, rng, rng.uniform(.3, .75)), round(offset * RATE))
            if name == 'bellows':
                add(values, contact('wood', rng, rng.uniform(.035, .07)), round((offset + seconds * .65) * RATE) % len(values))
                flex(values, rng, offset, min(offset + seconds * .4, DURATION), .015)
            offset += seconds * rng.uniform(.58, .93)
        values -= values.mean()
        target = .1 if name == 'airflow' else .11
        ceiling = .48 if name == 'airflow' else .55
        values *= target / np.sqrt(np.mean(values ** 2))
        # Gentle pressure saturation contains rare turbulent peaks without
        # pulling the entire track back toward the inaudible first pass.
        return ceiling * np.tanh(values / ceiling)
    bands = {
        'water-pipe': (95, 1800), 'water-outlet': (280, 3300),
        'water-sprinkler': (380, 3700), 'water-irrigator': (180, 2400),
    }
    low, high = bands[name]
    values = noise(DURATION, rng, low, high) * .035
    # Different slow and fast movements give four minutes of actual evolving content.
    values *= drift(len(values), rng, 97, .55, 1.15)
    values *= drift(len(values), rng, 1701, .8, 1.08)
    if name.startswith('water-'):
        density = {'water-pipe': 3, 'water-outlet': 11, 'water-sprinkler': 22, 'water-irrigator': 7}[name]
        for _ in range(DURATION * density):
            seconds = rng.uniform(.018, .09)
            t = np.arange(round(seconds * RATE)) / RATE
            frequency = rng.uniform(220, 1450)
            phase = 2 * np.pi * (frequency * t - .15 * frequency / seconds * t ** 2)
            bubble = np.sin(phase) * np.exp(-t / (seconds * .24))
            bubble += .3 * noise(seconds, rng, 170, 2600) * np.exp(-t / (seconds * .2))
            add(values, soften(bubble * rng.uniform(.12, .5), .004, .008), int(rng.integers(len(values))))
    values -= values.mean()
    gain = min(.12 / np.sqrt(np.mean(values ** 2)), .7 / np.max(np.abs(values)))
    return values * gain


def make_event(name: str, seed: int) -> np.ndarray:
    name = event_kind(name)
    rng = np.random.default_rng(seed)
    seconds = 1.8 if name.startswith('pressure-creak') else WORK_SECONDS[name]
    values = np.zeros(round(seconds * RATE))
    if name == 'pump-valve':
        add(values, contact('brass', rng, .19, .10), round(.008 * RATE))
        add(values, contact('leather', rng, .16, .10), round(.019 * RATE))
        add(values, contact('wood', rng, .05, .065), round(.046 * RATE))
    elif name == 'ratchet-pawl':
        # Three pawls are 120 degrees apart on a 15-tooth ring: all drop at
        # the same tooth phase. Tiny local delays make one rounded cluster.
        for delay in (.004, .011, .018):
            add(values, contact('brass', rng, rng.uniform(.07, .12), .075), round(delay * RATE))
        add(values, contact('wood', rng, .07, .07), round(.022 * RATE))
    elif name == 'ratchet-engage':
        add(values, contact('wood', rng, .28, .12), round(.025 * RATE))
        add(values, contact('brass', rng, .19, .12), round(.034 * RATE))
        add(values, contact('wood', rng, .10, .10), round(.11 * RATE))
        flex(values, rng, .15, .36, .035)
    elif name.startswith('pressure-creak'):
        flex(values, rng, .04, .62, .20)
        flex(values, rng, .85, 1.62, .15)
        add(values, contact('iron', rng, .14, .10), round(.65 * RATE))
        add(values, contact('wood', rng, .13, .10), round(1.65 * RATE))
    elif name == 'sender-work':
        # Approved rack lift: frames 0..150, air launch 180..240,
        # then empty tray return 250..360. Full cycle is 1.6 seconds.
        # 6.2-unit rack travel / (pi * .4 module) is roughly five contacts.
        gear_contacts(values, rng, .02, 150 / 360 * seconds, 5, .24)
        flex(values, rng, .12, .58, .035)
        add(values, contact('wood', rng, .22), round(.66 * RATE))
        add(values, air_breath(60 / 360 * seconds, rng, .24), round(180 / 360 * seconds * RATE))
        gear_contacts(values, rng, 250 / 360 * seconds, 1.50, 5, .15)
        add(values, contact('wood', rng, .18, .06), round(1.535 * RATE))
    elif name == 'receiver-work':
        # Cam opens the weighted gate through frame 110. Cargo arrives at
        # 115..145, falls at 145..190, and leather rollers feed it from 190.
        gear_contacts(values, rng, .01, 1.50, 14, .09, False)
        flex(values, rng, .03, 110 / 360 * seconds, .07)
        add(values, contact('iron', rng, .12), round(.48 * RATE))
        add(values, air_breath(30 / 360 * seconds, rng, .12), round(115 / 360 * seconds * RATE))
        add(values, contact('wood', rng, .26), round(190 / 360 * seconds * RATE))
        for time in (.90, 1.08, 1.25, 1.43):
            add(values, contact('leather', rng, rng.uniform(.12, .19), .095), round(time * RATE))
        flex(values, rng, 235 / 360 * seconds, 1.50, .065)
        add(values, contact('wood', rng, .20, .06), round(1.535 * RATE))
    elif name == 'router-turn':
        # One-second transit: catch, gate down, indexed carriage turn,
        # gate up, air launch. Same boundaries as PneumaticRouterMotion.
        catch = (.5 + 1.95 / 16) / 2
        close, launch = catch + .1, 1 - catch
        opening = launch - .1
        add(values, air_breath(catch, rng, .11), 0)
        add(values, contact('wood', rng, .20), round((catch - .065) * RATE))
        flex(values, rng, catch, close, .08)
        add(values, contact('iron', rng, .13, .05), round((close - .045) * RATE))
        gear_contacts(values, rng, close, opening - .025, 4, .16)
        add(values, contact('brass', rng, .15, .05), round((opening - .035) * RATE))
        flex(values, rng, opening, launch, .07)
        add(values, air_breath(1 - launch, rng, .16), round(launch * RATE))
    elif name == 'router-prepare':
        add(values, contact('iron', rng, .10, .045), round(.025 * RATE))
        gear_contacts(values, rng, .1, .275, 4, .13)
        add(values, contact('brass', rng, .12, .05), round(.265 * RATE))
        flex(values, rng, .31, .4, .06)
    else:
        # An ordinary tube has no powered gate or tray: parcel slides into
        # the outlet chest and settles, rather than sounding like a receiver.
        add(values, air_breath(.55, rng, .06), 0)
        for offset, strength in ((.49, .25), (.63, .12), (.74, .045)):
            add(values, contact('wood', rng, strength, .11), round(offset * RATE))
    values = soften(values, .025, .06)
    values *= min(.135 / max(np.sqrt(np.mean(values ** 2)), 1e-9), .55 / max(np.max(np.abs(values)), 1e-9))
    return values


def ogg_info(path: Path) -> dict:
    data = path.read_bytes()
    offset, final_granule, packet = 0, 0, bytearray()
    identification = None
    while offset < len(data):
        if data[offset:offset + 4] != b'OggS' or offset + 27 > len(data):
            raise ValueError(f'Invalid Ogg page: {path.name}')
        segments = data[offset + 26]
        sizes = data[offset + 27:offset + 27 + segments]
        body = offset + 27 + segments
        end = body + sum(sizes)
        if end > len(data):
            raise ValueError(f'Truncated Ogg page: {path.name}')
        granule = struct.unpack_from('<q', data, offset + 6)[0]
        if granule >= 0:
            final_granule = granule
        for size in sizes:
            packet.extend(data[body:body + size])
            body += size
            if size < 255:
                if identification is None:
                    identification = bytes(packet)
                packet.clear()
        offset = end
    if identification is None or identification[:7] != b'\x01vorbis':
        raise ValueError(f'Expected Vorbis identification packet: {path.name}')
    channels = identification[11]
    rate = struct.unpack_from('<I', identification, 12)[0]
    return {'sampleRate': rate, 'channels': channels, 'seconds': round(final_granule / rate, 4)}


def encode(path: Path, values: np.ndarray, codec: str) -> None:
    import av
    with av.open(str(path), mode='w', format='ogg') as container:
        stream = container.add_stream(codec, rate=RATE)
        stream.layout = 'mono'
        stream.bit_rate = 64000
        stream.metadata['title'] = 'Gearwright ' + path.stem
        if codec == 'vorbis':
            stream.codec_context.options = {'strict': 'experimental'}
        for offset in range(0, len(values), 4096):
            frame = av.AudioFrame.from_ndarray(values[offset:offset + 4096].astype(np.float32).reshape(1, -1), format='fltp', layout='mono')
            frame.sample_rate = RATE
            frame.pts = offset
            for packet in stream.encode(frame):
                container.mux(packet)
        for packet in stream.encode(None):
            container.mux(packet)


def waveform_profile(values: np.ndarray) -> dict:
    frequencies = np.fft.rfftfreq(len(values), 1 / RATE)
    power = np.abs(np.fft.rfft(values)) ** 2
    middle = power[(frequencies >= 200) & (frequencies <= 1800)]
    block = RATE // 20
    windows = values[:len(values) // block * block].reshape(-1, block)
    envelope = np.sqrt(np.mean(windows ** 2, axis=1))
    return {
        'energyAbove1800Hz': round(float(power[frequencies > 1800].sum() / max(power.sum(), 1e-12)), 6),
        'midbandFlatness': round(float(np.exp(np.mean(np.log(middle + 1e-12))) / max(middle.mean(), 1e-12)), 6),
        'envelopeVariation': round(float(envelope.std() / max(envelope.mean(), 1e-12)), 6),
    }


def check_character(name: str, profile: dict) -> None:
    if name == 'airflow' and (profile['energyAbove1800Hz'] > .01 or profile['envelopeVariation'] < .3):
        raise ValueError('Airflow needs quiet high frequencies and evolving breath pressure, rather than steady static')
    if event_kind(name) in VARIED_WORK and profile['midbandFlatness'] > .35:
        raise ValueError(f'Mechanism contact resonances are obscured by broadband rubbing: {name}')


def decoded_checks(path: Path, loop: bool) -> dict:
    import av
    with av.open(str(path)) as container:
        values = np.concatenate([frame.to_ndarray().reshape(-1) for frame in container.decode(audio=0)])
    if not np.isfinite(values).all() or np.max(np.abs(values)) >= .95:
        raise ValueError(f'Invalid or clipped decoded sound: {path.name}')
    if loop:
        seam = float(abs(values[0] - values[-1]))
        normal = float(np.quantile(np.abs(np.diff(values)), .999))
        if seam > normal:
            raise ValueError(f'Loop seam exceeds normal adjacent sample movement: {path.name}')
    profile = waveform_profile(values)
    check_character(path.stem, profile)
    return {'decodedPeak': round(float(np.max(np.abs(values))), 6),
            'decodedRms': round(float(np.sqrt(np.mean(values ** 2))), 6), 'character': profile}


def verify() -> None:
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    if manifest['schemaVersion'] != 1 or manifest['builderSha256'] != builder_digest():
        raise ValueError('Machine audio generator changed; rebuild the sound assets')
    expected = {name + '.ogg' for name in LOOPS + EVENT_ASSETS}
    if set(manifest['files']) != expected:
        raise ValueError('Machine audio manifest does not contain the expected assets')
    for name, entry in manifest['files'].items():
        path = OUTPUT / name
        info = ogg_info(path)
        if digest(path) != entry['sha256'] or info != entry['format']:
            raise ValueError(f'Machine sound differs from its verified manifest: {name}')
        if info['channels'] != 1 or info['sampleRate'] != RATE:
            raise ValueError(f'Machine sound must be mono at the declared rate: {name}')
        if name[:-4] in LOOPS and info['seconds'] < 180:
            raise ValueError(f'Constant sound content is shorter than three minutes: {name}')
        base = event_kind(name[:-4])
        if base in WORK_SECONDS and abs(info['seconds'] - WORK_SECONDS[base]) > .01:
            raise ValueError(f'Work sound does not match its mechanism duration: {name}')
        check_character(name[:-4], entry['character'])
    print(f'Verified {len(LOOPS)} four-minute original loops and {len(EVENT_ASSETS)} original work/contact recordings, including four variations per moving mechanism.')


def build() -> None:
    codecs = vorbis_encoders()
    if not codecs:
        raise RuntimeError('PyAV has no available Vorbis encoder; no audio assets were written')
    OUTPUT.mkdir(parents=True, exist_ok=True)
    manifest = {'schemaVersion': 1, 'builderSha256': builder_digest(),
                'provenance': 'Original deterministic synthesis. No game or third-party recordings were used.',
                'files': {}}
    for index, name in enumerate(LOOPS + EVENT_ASSETS):
        loop = name in LOOPS
        # Preserve the accepted pneumatic/hydraulic timbres when extending
        # the catalogue: their synthesis seed must not shift with new assets.
        seed = 8100 + ACCEPTED_SEED_ORDER.index(name) if name in ACCEPTED_SEED_ORDER else 8200 + index
        values = make_loop(name, seed) if loop else make_event(name, seed)
        path = OUTPUT / (name + '.ogg')
        encode(path, values, codecs[0])
        stats = decoded_checks(path, loop)
        constant = loop or event_kind(name) in ('pump-valve', 'ratchet-pawl')
        manifest['files'][path.name] = {'kind': 'constant' if constant else 'informational',
            'sha256': digest(path), 'format': ogg_info(path), **stats}
        print(f'{path.name}: {manifest["files"][path.name]["format"]["seconds"]:.2f}s, peak {stats["decodedPeak"]:.3f}, '
              f'flatness {stats["character"]["midbandFlatness"]:.3f}, envelope variation {stats["character"]["envelopeVariation"]:.3f}')
    MANIFEST.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    verify()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect-codecs", action="store_true")
    parser.add_argument('--build', action='store_true')
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    if args.inspect_codecs:
        print(json.dumps({"vorbisEncoders": vorbis_encoders()}))
    elif args.verify:
        verify()
    elif args.build:
        build()
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
