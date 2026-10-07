"""Reproducible machine sounds: original mechanisms and installed-game irrigation textures."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import tempfile

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
    sources = (Path(__file__), Path(__file__).with_name('water_sound_sources.py'))
    return hashlib.sha256('\n'.join(path.read_text(encoding='utf-8') for path in sources).encode('utf-8')).hexdigest()


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


def add(values: np.ndarray, event: np.ndarray, offset: int, *, wrap: bool = False) -> None:
    # Only continuous beds may wrap. A late work contact must never appear
    # before its mechanism moves at the beginning of the recording.
    if wrap:
        offset %= len(values)
        size = min(len(event), len(values) - offset)
        values[offset:offset + size] += event[:size]
        if size < len(event):
            add(values, event[size:], 0, wrap=True)
    else:
        start, end = max(0, offset), min(len(values), offset + len(event))
        if end > start:
            values[start:end] += event[start - offset:end - offset]


def contact(material: str, rng: np.random.Generator, strength: float = 1, seconds: float = .085) -> np.ndarray:
    """Finite contact force, material modes, surface grain and a small rebound.

    Higher modes decay first; the body outlasts the contact. This avoids both
    identical electronic pips and a broad noise burst masking the material.
    """
    t = np.arange(round(seconds * RATE)) / RATE
    modes = {
        'iron': ((173, .032, .38), (286, .029, .56), (647, .021, .40), (1137, .016, .27),
                 (1883, .010, .19), (2761, .007, .12), (3917, .004, .06)),
        'brass': ((241, .025, .26), (417, .033, .52), (923, .024, .41), (1561, .018, .29),
                  (2473, .011, .20), (3529, .006, .11), (4733, .004, .05)),
        'wood': ((97, .034, .48), (143, .029, .72), (317, .019, .42), (563, .013, .29),
                 (1021, .008, .16), (1763, .004, .08)),
        'leather': ((61, .025, .66), (109, .021, .49), (197, .013, .26), (367, .008, .12),
                    (811, .004, .06)),
    }[material]
    result = np.zeros(len(t))
    size = rng.uniform(.93, 1.07)
    for frequency, decay, gain in modes:
        result += gain * rng.uniform(.8, 1.2) * np.sin(2 * np.pi * frequency * size * rng.uniform(.985, 1.015) * t) \
            * np.exp(-t / (decay * rng.uniform(.85, 1.15)))
    # A rounded finite force excites the object instead of an ideal impulse.
    force = np.hanning(max(5, round(RATE * rng.uniform(.0005, .0009))))
    force /= force.sum()
    result = np.convolve(result, force, mode='full')[:len(t)]
    grain = noise(seconds, rng, 380 if material != 'leather' else 90,
                  3200 if material in ('iron', 'brass') else 1500)
    result += grain * np.exp(-t / .006) * (.17 if material == 'wood' else .09)
    rebound = round(rng.uniform(.006, .012) * RATE)
    result[rebound:] += result[:-rebound].copy() * rng.uniform(.06, .14)
    return soften(result * strength, .0012, .012)


def surface_motion(seconds: float, rng: np.random.Generator, material: str, gain: float) -> np.ndarray:
    """Quiet, irregular loaded motion underneath separately timed contacts."""
    values = np.zeros(round(seconds * RATE))
    offset = rng.uniform(.005, .015)
    while offset < seconds:
        add(values, contact(material, rng, rng.uniform(.035, .09), .04), round(offset * RATE))
        offset += rng.uniform(.012, .038)
    envelope = np.sin(np.linspace(0, np.pi, len(values))) ** .7
    return soften(values * envelope * gain, .012, .025)


def frame_stop(values: np.ndarray, rng: np.random.Generator, time: float, gain: float) -> None:
    """Loaded wood frame, fitting contact, then a softer settling rebound."""
    add(values, contact('wood', rng, gain, .09), round(time * RATE))
    add(values, contact('iron', rng, gain * .22, .055), round((time + .007) * RATE))
    add(values, contact('wood', rng, gain * .18, .045), round((time + .033) * RATE))


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
        # Rolling tooth faces end in a distinct contact. They remain well
        # below the contact, leaving space between teeth and frame stops.
        add(values, surface_motion(.038, rng, 'brass', gain * .5), round((time - .016) * RATE))
        # Backlash and a loose frame give occasional secondary contacts.
        if index % 3 == 1:
            add(values, contact('wood', rng, gain * .20, .055), round((time + .017) * RATE))


def flex(values: np.ndarray, rng: np.random.Generator, start: float, end: float, gain: float) -> None:
    """Irregular loaded gate releases; grain and changing body modes, with gaps."""
    offset = start + rng.uniform(.015, .03)
    while offset + .05 < end:
        duration = rng.uniform(.035, .055)
        t = np.arange(round(duration * RATE)) / RATE
        frequency = rng.uniform(150, 280)
        phase = 2 * np.pi * (frequency * t + rng.uniform(-.28, .28) * frequency * t ** 2 / duration)
        chirr = (np.sin(phase) + .38 * np.sin(phase * 2.37) + .17 * np.sin(phase * 4.13))
        chirr *= np.exp(-t / rng.uniform(.009, .017)) * gain * rng.uniform(.5, 1)
        chirr += noise(duration, rng, 380, 2200) * gain * .14 * np.exp(-t / .009)
        add(values, soften(chirr, .004, .012), round(offset * RATE))
        offset += rng.uniform(.065, .14)


def air_breath(seconds: float, rng: np.random.Generator, gain: float = 1) -> np.ndarray:
    # Broad, low pressure resonances move through the breath. No constant
    # high-frequency hiss or narrow sustained whistle.
    count = round(seconds * RATE)
    phase = np.linspace(0, 1, count)
    body = noise(seconds, rng, rng.uniform(55, 90), rng.uniform(300, 430))
    throat = noise(seconds, rng, 150, rng.uniform(550, 750))
    edge = noise(seconds, rng, 380, rng.uniform(1050, 1450))
    sweep = np.sin(np.pi * phase) ** rng.uniform(.8, 1.6)
    envelope = np.sin(np.pi * phase) ** rng.uniform(1.1, 2.2)
    opening = np.sin(np.pi * phase ** .7) ** 2
    texture = body * (1 - .42 * sweep) + throat * .32 * sweep + edge * .065 * opening
    texture *= drift(count, rng, max(5, round(seconds * 13)), .62, 1.08)
    return soften(texture * envelope * gain, .04, .07)


def water_drop(seconds: float, rng: np.random.Generator, enclosed: bool) -> np.ndarray:
    """A small impact, several collapsing cavities and a scattered wet tail."""
    t = np.arange(round(seconds * RATE)) / RATE
    frequency = rng.uniform(280, 1050) if enclosed else rng.uniform(700, 2600)
    phase = 2 * np.pi * frequency * (t + rng.uniform(.08, .30) * t ** 2 / seconds)
    result = np.sin(phase) * np.exp(-t / (seconds * .12)) * (.24 if enclosed else .12)
    result += np.sin(phase * 1.73) * np.exp(-t / (seconds * .08)) * .07
    result += noise(seconds, rng, 160 if enclosed else 500, 1800 if enclosed else 4300) \
        * np.exp(-t / (seconds * .18)) * .24
    return soften(result, .0015, .009)


def liquid_grain(seconds: float, rng: np.random.Generator) -> np.ndarray:
    """Diffuse wet motion in a filled pipe, without a pitched cavity chirp."""
    t = np.arange(round(seconds * RATE)) / RATE
    result = noise(seconds, rng, 360, rng.uniform(1800, 2700))
    result *= np.exp(-t / (seconds * .28))
    return soften(result * .22, .002, .008)


def finish(values: np.ndarray, target: float, ceiling: float) -> np.ndarray:
    values -= values.mean()
    values *= target / max(float(np.sqrt(np.mean(values ** 2))), 1e-9)
    # Sparse details must not force the whole bed down to an inaudible level.
    # This soft knee leaves normal samples untouched and contains rare peaks.
    knee = ceiling * .68
    magnitude = np.abs(values)
    over = magnitude > knee
    values[over] = np.sign(values[over]) * (knee + (ceiling - knee) *
        np.tanh((magnitude[over] - knee) / (ceiling - knee)))
    return values


def dry_ratchet_contact(seconds: float, rng: np.random.Generator, heavy: bool = False) -> np.ndarray:
    """Short, heavily damped seating contact; no oscillator or ringing tail."""
    t = np.arange(round(seconds * RATE)) / RATE
    body = noise(seconds, rng, 160 if heavy else 500, 1050 if heavy else 2300)
    body *= np.exp(-t / (.019 if heavy else .0045))
    edge = noise(seconds, rng, 1100, 3100) * np.exp(-t / .0025)
    return soften(body + edge * .23, .0018, .01)


def irrigator_layers(rng: np.random.Generator, sources: list[np.ndarray]) -> tuple[np.ndarray, np.ndarray]:
    """An uninterrupted wet body with independent, rare, quieter pour accents."""
    watering, pouring = sources
    body, weights, accents = (np.zeros(DURATION * RATE) for _ in range(3))
    offset = 0.
    while offset < DURATION:
        seconds = rng.uniform(1.8, 3.4)
        count = min(round(seconds * RATE), len(watering) - 1)
        start = int(rng.integers(0, len(watering) - count))
        clip = watering[start:start + count].copy()
        clip /= max(1, float(np.sqrt(np.mean(clip ** 2))))
        envelope = np.sin(np.linspace(0, np.pi, count)) ** 2
        add(body, clip * envelope, round(offset * RATE), wrap=True)
        add(weights, envelope ** 2, round(offset * RATE), wrap=True)
        offset += seconds * rng.uniform(.42, .58)
    # Overlap compensation avoids a new swell/dip at every excerpt boundary.
    body /= np.sqrt(np.maximum(weights, 1e-9))
    body *= drift(len(body), rng, 61, .70, 1.05)
    body = finish(body, .043, .25)

    offset = rng.uniform(6, 12)
    while offset < DURATION:
        count = min(round(rng.uniform(.22, .48) * RATE), len(pouring) - 1)
        start = int(rng.integers(0, len(pouring) - count))
        clip = pouring[start:start + count].copy()
        clip *= np.sin(np.linspace(0, np.pi, count)) ** 1.5
        clip = soften(finish(clip, rng.uniform(.008, .012), .065), .025, .055)
        add(accents, clip, round(offset * RATE))
        offset += rng.uniform(7, 14)
    # Do not normalize this sparse layer across its long silent intervals:
    # that would undo the reduction in pour level when fewer pours are added.
    return body, accents


def make_irrigation(name: str, rng: np.random.Generator, sources: list[np.ndarray]) -> np.ndarray:
    """Compose wet detail from changing recorded segments, never pitched bubbles.

    Different excerpts, lengths, overlaps and impact groupings create four
    minutes of content. No short bed is repeated with just gain/pitch jitter.
    """
    if name == 'water-irrigator':
        body, accents = irrigator_layers(rng, sources)
        return body + accents
    values = np.zeros(DURATION * RATE)
    watering, pouring = sources

    def excerpt(source, seconds):
        count = min(round(seconds * RATE), len(source) - 1)
        start = int(rng.integers(0, len(source) - count))
        clip = source[start:start + count].copy()
        # A short pour impact must not dominate the entire quieter composition.
        clip /= max(1, float(np.sqrt(np.mean(clip ** 2))))
        return soften(clip, .012, .028)

    # Keep the accepted sprinkler composition and its random sequence intact.
    for lane in range(2):
        offset = rng.uniform(0, .4)
        while offset < DURATION:
            seconds = rng.uniform(.32, .95)
            clip = excerpt(watering, seconds)
            clip *= np.sin(np.linspace(0, np.pi, len(clip))) ** .8
            add(values, clip * rng.uniform(.10, .24), round(offset * RATE), wrap=True)
            offset += seconds * rng.uniform(.65, .95)
    # Smaller, overlapping wet impacts break up the stream without a hiss bed.
    offset = 0.
    while offset < DURATION:
        seconds = rng.uniform(.018, .06)
        clip = excerpt(pouring, seconds)
        clip *= np.sin(np.linspace(0, np.pi, len(clip))) ** 1.5
        add(values, clip * rng.uniform(.025, .08), round(offset * RATE), wrap=True)
        offset += rng.uniform(.025, .10)
    values *= drift(len(values), rng, 103, .55, 1.05)
    return finish(values, .050, .35)


def make_loop(name: str, seed: int, water_sources: list[np.ndarray] | None = None) -> np.ndarray:
    rng = np.random.default_rng(seed)
    if name in ('water-sprinkler', 'water-irrigator'):
        if water_sources is None:
            raise ValueError('Irrigation requires inspected installed game water sources')
        return make_irrigation(name, rng, water_sources)
    if name == 'transmission-bearing':
        values = np.zeros(DURATION * RATE)
        offset = 0.
        while offset < DURATION:
            seconds = rng.uniform(.18, .65)
            gesture = noise(seconds, rng, 120, rng.uniform(650, 1000))
            gesture *= np.sin(np.linspace(0, np.pi, len(gesture))) ** 1.7
            gesture *= drift(len(gesture), rng, 11, .15, 1)
            add(values, soften(gesture, .04, .07) * rng.uniform(.04, .1), round(offset * RATE), wrap=True)
            offset += seconds + rng.uniform(.06, .35)
        return finish(values, .028, .25)
    if name in ('flywheel', 'pump-mechanism'):
        values = np.zeros(DURATION * RATE)
        # Low bearing detail only. Runtime supplies the stroke/valve/pawl
        # contacts, so no fixed rhythm here competes with live motion.
        offset = 0.
        while offset < DURATION:
            duration = rng.uniform(.3, .9)
            if name == 'flywheel':
                gesture = air_breath(duration, rng, rng.uniform(.035, .075))
                gesture += surface_motion(duration, rng, 'wood', .5)
            else:
                gesture = surface_motion(duration, rng, 'leather', 1.0)
                gesture += surface_motion(duration, rng, 'iron', .20)
            add(values, gesture, round(offset * RATE), wrap=True)
            add(values, contact('wood', rng, rng.uniform(.012, .03), .12), round(offset * RATE), wrap=True)
            offset += duration * rng.uniform(.7, 1.4)
        return finish(values, .041 if name == 'flywheel' else .036, .42)
    if name in ('airflow', 'bellows'):
        values = noise(DURATION, rng, 65, 350) * .012
        # Nonperiodic overlapping breaths evolve in duration, timbre and
        # pressure for all four minutes; this is source content, not pitch jitter.
        offset = 0.
        while offset < DURATION:
            seconds = rng.uniform(1.2, 3.8) if name == 'airflow' else rng.uniform(.45, 1.2)
            add(values, air_breath(seconds, rng, rng.uniform(.3, .75)), round(offset * RATE), wrap=True)
            if name == 'bellows':
                add(values, surface_motion(seconds * .7, rng, 'leather', .65), round(offset * RATE), wrap=True)
                add(values, contact('wood', rng, rng.uniform(.045, .09), .12),
                    round((offset + seconds * .65) * RATE), wrap=True)
                add(values, contact('leather', rng, .09, .12),
                    round((offset + seconds * .7) * RATE), wrap=True)
            offset += seconds * rng.uniform(.58, .93)
        target = .1 if name == 'airflow' else .11
        ceiling = .48 if name == 'airflow' else .55
        return finish(values, target, ceiling)
    bands = {
        'water-pipe': (280, 2100), 'water-outlet': (210, 2200),
    }
    low, high = bands[name]
    values = noise(DURATION, rng, low, high) * .009
    values *= drift(len(values), rng, 193, .3, 1.1)
    # Each phrase has its own density, cavity size, splatter and decay. This
    # gives continuous liquid detail without a static bed and loud bubble pips.
    density = 65
    offset = 0.
    while offset < DURATION:
        duration = rng.uniform(.35, 1.8)
        phrase_gain = rng.uniform(.35, 1.)
        enclosed = name == 'water-pipe'
        flow = noise(duration, rng, low, high) * .014 * phrase_gain
        flow *= np.sin(np.linspace(0, np.pi, len(flow))) ** 1.3
        add(values, flow, round(offset * RATE), wrap=True)
        for _ in range(max(1, round(duration * density * rng.uniform(.5, 1.4)))):
            time = offset + rng.uniform(0, duration)
            drop_seconds = rng.uniform(.015, .045)
            drop = liquid_grain(drop_seconds, rng) if enclosed else water_drop(drop_seconds, rng, False)
            add(values, drop * phrase_gain * rng.uniform(.06, .28), round(time * RATE), wrap=True)
        offset += duration * rng.uniform(.6, 1.0)
    target = {'water-pipe': .050, 'water-outlet': .052}[name]
    return finish(values, target, .55)


def make_event(name: str, seed: int) -> np.ndarray:
    name = event_kind(name)
    rng = np.random.default_rng(seed)
    seconds = 1.8 if name.startswith('pressure-creak') else WORK_SECONDS[name]
    values = np.zeros(round(seconds * RATE))
    if name == 'pump-valve':
        add(values, contact('brass', rng, .19, .10), round(.008 * RATE))
        add(values, contact('leather', rng, .16, .10), round(.019 * RATE))
        add(values, contact('wood', rng, .05, .065), round(.046 * RATE))
        add(values, surface_motion(.075, rng, 'leather', .40), round(.03 * RATE))
    elif name == 'ratchet-pawl':
        # Three aligned pawls land as a single dry tick, not a ringing rattle.
        for delay, gain in ((.030, .18), (.0315, .10), (.033, .08)):
            add(values, dry_ratchet_contact(.025, rng) * gain, round(delay * RATE))
    elif name == 'ratchet-engage':
        add(values, dry_ratchet_contact(.14, rng, True) * .3, round(.023 * RATE))
        add(values, dry_ratchet_contact(.04, rng) * .09, round(.037 * RATE))
        add(values, dry_ratchet_contact(.07, rng, True) * .035, round(.093 * RATE))
    elif name.startswith('pressure-creak'):
        # Loaded wall/fitting strain in two uneven phrases with an audible
        # relaxation gap, rather than evenly spaced pitched chirps.
        for start, duration, strength in ((.04, .58, .19), (.85, .77, .15)):
            flex(values, rng, start, start + duration, strength)
            add(values, surface_motion(duration, rng, 'iron', .65), round(start * RATE))
            add(values, surface_motion(duration, rng, 'wood', .80), round(start * RATE))
        frame_stop(values, rng, .65, .11)
        frame_stop(values, rng, 1.65, .09)
    elif name == 'sender-work':
        # Approved rack lift: frames 0..150, air launch 180..240,
        # then empty tray return 250..360. Full cycle is 1.6 seconds.
        # 6.2-unit rack travel / (pi * .4 module) is roughly five contacts.
        gear_contacts(values, rng, .02, 150 / 360 * seconds, 5, .24)
        add(values, surface_motion(.56, rng, 'iron', .7), round(.045 * RATE))
        add(values, surface_motion(.56, rng, 'wood', .5), round(.045 * RATE))
        flex(values, rng, .12, .58, .035)
        frame_stop(values, rng, .66, .22)
        add(values, air_breath(60 / 360 * seconds, rng, .24), round(180 / 360 * seconds * RATE))
        gear_contacts(values, rng, 250 / 360 * seconds, 1.50, 5, .15)
        add(values, surface_motion(.34, rng, 'iron', .36), round(1.14 * RATE))
        frame_stop(values, rng, 1.535, .18)
    elif name == 'receiver-work':
        # Cam opens the weighted gate through frame 110. Cargo arrives at
        # 115..145, falls at 145..190, and leather rollers feed it from 190.
        gear_contacts(values, rng, .01, 1.50, 14, .09, False)
        flex(values, rng, .03, 110 / 360 * seconds, .07)
        add(values, surface_motion(.40, rng, 'iron', .50), round(.05 * RATE))
        add(values, contact('iron', rng, .12), round(.48 * RATE))
        add(values, air_breath(30 / 360 * seconds, rng, .12), round(115 / 360 * seconds * RATE))
        frame_stop(values, rng, 190 / 360 * seconds, .26)
        add(values, surface_motion(.65, rng, 'leather', 1.25), round(.86 * RATE))
        for time in (.90, 1.08, 1.25, 1.43):
            add(values, contact('leather', rng, rng.uniform(.12, .19), .12), round(time * RATE))
        flex(values, rng, 235 / 360 * seconds, 1.50, .065)
        frame_stop(values, rng, 1.535, .20)
    elif name == 'router-turn':
        # One-second transit: catch, gate down, indexed carriage turn,
        # gate up, air launch. Same boundaries as PneumaticRouterMotion.
        catch = (.5 + 1.95 / 16) / 2
        close, launch = catch + .1, 1 - catch
        opening = launch - .1
        add(values, air_breath(catch, rng, .11), 0)
        frame_stop(values, rng, catch - .065, .20)
        flex(values, rng, catch, close, .08)
        add(values, contact('iron', rng, .13, .05), round((close - .045) * RATE))
        gear_contacts(values, rng, close, opening - .025, 4, .16)
        add(values, surface_motion(opening - close, rng, 'brass', .60), round(close * RATE))
        add(values, contact('brass', rng, .15, .05), round((opening - .035) * RATE))
        flex(values, rng, opening, launch, .07)
        add(values, air_breath(1 - launch, rng, .16), round(launch * RATE))
    elif name == 'router-prepare':
        add(values, contact('iron', rng, .10, .045), round(.025 * RATE))
        gear_contacts(values, rng, .1, .275, 4, .13)
        add(values, surface_motion(.175, rng, 'brass', .55), round(.1 * RATE))
        add(values, contact('brass', rng, .12, .05), round(.265 * RATE))
        flex(values, rng, .31, .4, .06)
    else:
        # An ordinary tube has no powered gate or tray: parcel slides into
        # the outlet chest and settles, rather than sounding like a receiver.
        add(values, air_breath(.55, rng, .06), 0)
        add(values, surface_motion(.39, rng, 'wood', .60), round(.10 * RATE))
        for offset, strength in ((.49, .25), (.63, .12), (.74, .045)):
            frame_stop(values, rng, offset, strength)
    if name in ('ratchet-pawl', 'ratchet-engage'):
        return soften(finish(values, .045 if name == 'ratchet-pawl' else .040, .32), .003, .012)
    values = soften(values, .025, .06)
    target = .12 if name in ('pump-valve', 'ratchet-pawl') else .08
    return soften(finish(values, target, .55), .004, .012)


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
    # Put the preroll on an earlier page for short cues. A single EOS data
    # page makes FFmpeg infer the wrong initial granule and lose/extend the
    # final 256 samples depending on the last Vorbis block size.
    options = {'page_duration': '20000'} if len(values) <= RATE * 2 else {}
    with av.open(str(path), mode='w', format='ogg', options=options) as container:
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
        'energyAbove6000Hz': round(float(power[frequencies > 6000].sum() / max(power.sum(), 1e-12)), 6),
        'peak50msRms': round(float(envelope.max()), 6),
    }


def check_character(name: str, profile: dict) -> None:
    if name == 'airflow' and (profile['energyAbove1800Hz'] > .01 or profile['envelopeVariation'] < .3):
        raise ValueError('Airflow needs quiet high frequencies and evolving breath pressure, rather than steady static')
    if event_kind(name) in VARIED_WORK and event_kind(name) not in ('ratchet-pawl', 'ratchet-engage') and profile['midbandFlatness'] > .35:
        raise ValueError(f'Mechanism contact resonances are obscured by broadband rubbing: {name}')
    if profile['energyAbove6000Hz'] > .015:
        raise ValueError(f'Excessive bright hiss or sharp contact energy: {name}')
    if profile['peak50msRms'] > .30:
        raise ValueError(f'Excessive short-term loudness: {name}')


def decoded_checks(path: Path, loop: bool) -> dict:
    import av
    with av.open(str(path)) as container:
        values = np.concatenate([frame.to_ndarray().reshape(-1) for frame in container.decode(audio=0)])
    expected = round(ogg_info(path)['seconds'] * RATE)
    if len(values) != expected:
        raise ValueError(f'Decoded sample count differs from declared mechanism duration: {path.name}')
    if not np.isfinite(values).all() or np.max(np.abs(values)) >= .95:
        raise ValueError(f'Invalid or clipped decoded sound: {path.name}')
    if loop:
        seam = float(abs(values[0] - values[-1]))
        normal = float(np.quantile(np.abs(np.diff(values)), .999))
        if seam > normal:
            raise ValueError(f'Loop seam exceeds normal adjacent sample movement: {path.name}')
    elif max(abs(float(values[0])), abs(float(values[-1]))) > .002:
        raise ValueError(f'Work recording has a truncated or abrupt edge: {path.name}')
    profile = waveform_profile(values)
    check_character(path.stem, profile)
    return {'decodedPeak': round(float(np.max(np.abs(values))), 6),
            'decodedRms': round(float(np.sqrt(np.mean(values ** 2))), 6), 'character': profile}


def verify(*, decode: bool = False) -> None:
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
        if decode:
            decoded_checks(path, name[:-4] in LOOPS)
    print(f'Verified {len(LOOPS)} four-minute loops and {len(EVENT_ASSETS)} original work/contact recordings, including four variations per moving mechanism.')


def build(game_assets: Path) -> None:
    from water_sound_sources import load
    water_sources, water_provenance = load(game_assets)
    codecs = vorbis_encoders()
    if not codecs:
        raise RuntimeError('PyAV has no available Vorbis encoder; no audio assets were written')
    OUTPUT.mkdir(parents=True, exist_ok=True)
    manifest = {'schemaVersion': 1, 'builderSha256': builder_digest(),
                'provenance': 'Original deterministic mechanism synthesis; irrigation uses composed excerpts of installed Vintage Story water recordings. No third-party recordings were used.',
                'irrigationSources': water_provenance,
                'files': {}}
    # Complete encoding and signal checks before replacing any runtime file.
    # An unavailable encoder or failed audit leaves the installed sources intact.
    staging_root = ROOT / 'generated/machine-sound-build'
    staging_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=staging_root) as temporary:
        staging = Path(temporary)
        for index, name in enumerate(LOOPS + EVENT_ASSETS):
            loop = name in LOOPS
            # Keep catalogue seeds stable when adding unrelated machinery.
            seed = 8100 + ACCEPTED_SEED_ORDER.index(name) if name in ACCEPTED_SEED_ORDER else 8200 + index
            values = make_loop(name, seed, water_sources) if loop else make_event(name, seed)
            path = staging / (name + '.ogg')
            encode(path, values, codecs[0])
            stats = decoded_checks(path, loop)
            constant = loop or event_kind(name) in ('pump-valve', 'ratchet-pawl')
            manifest['files'][path.name] = {'kind': 'constant' if constant else 'informational',
                'sha256': digest(path), 'format': ogg_info(path), **stats}
            print(f'{path.name}: {manifest["files"][path.name]["format"]["seconds"]:.2f}s, peak {stats["decodedPeak"]:.3f}, '
                  f'flatness {stats["character"]["midbandFlatness"]:.3f}, envelope variation {stats["character"]["envelopeVariation"]:.3f}',
                  flush=True)
        for name in manifest['files']:
            (staging / name).replace(OUTPUT / name)
        MANIFEST.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    verify()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inspect-codecs", action="store_true")
    parser.add_argument('--build', action='store_true')
    parser.add_argument('--game-assets', type=Path, help='Installed Vintage Story assets directory; required when building')
    parser.add_argument('--verify', action='store_true')
    parser.add_argument('--decode', action='store_true', help='Decode and audit every asset during verification')
    args = parser.parse_args()
    if args.inspect_codecs:
        print(json.dumps({"vorbisEncoders": vorbis_encoders()}))
    elif args.verify:
        verify(decode=args.decode)
    elif args.build:
        if args.game_assets is None:
            parser.error('--build requires --game-assets')
        build(args.game_assets)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
