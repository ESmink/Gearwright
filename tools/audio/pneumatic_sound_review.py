"""Build local pneumatic audio candidates from inspected vanilla samples.

Outputs stay in generated/pneumatic-sound-review/current. Nothing is copied
into runtime assets. NumPy handles the synthesis; PyAV decodes the installed
game's recordings. The output format is uncompressed mono PCM for review.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import wave

import av
import numpy as np


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "generated/pneumatic-sound-review/current"
RATE = 48000
SOURCES = (
    "survival:sounds/block/bellowslarge/bellowlarge-in1.ogg",
    "survival:sounds/block/bellowslarge/bellowlarge-in2.ogg",
    "survival:sounds/block/bellowslarge/bellowlarge-in3.ogg",
    "survival:sounds/block/bellowslarge/bellowlarge-out1.ogg",
    "survival:sounds/block/bellowslarge/bellowlarge-out2.ogg",
    "survival:sounds/block/bellowslarge/bellowlarge-out3.ogg",
    "survival:sounds/effect/bellows.ogg",
    "survival:sounds/effect/gearbox_turn.ogg",
    "survival:sounds/effect/gears.ogg",
    "survival:sounds/effect/planetary_gears.ogg",
    "survival:sounds/block/chute.ogg",
    "survival:sounds/block/metaldoor.ogg",
    "survival:sounds/block/metaldoor-place.ogg",
    "survival:sounds/block/creak/woodcreak_1.ogg",
)


def source_path(game: Path, reference: str) -> Path:
    domain, relative = reference.split(":", 1)
    assets = (game / "assets").resolve()
    path = (assets / domain / relative).resolve()
    if not path.is_relative_to(assets):
        raise ValueError("Source must stay inside the installed assets directory")
    return path


def decode(path: Path) -> tuple[np.ndarray, dict]:
    with av.open(str(path)) as container:
        stream = container.streams.audio[0]
        info = {
            "codec": stream.codec_context.name,
            "sourceRate": stream.codec_context.sample_rate,
            "sourceChannels": stream.codec_context.channels,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        }
        resampler = av.AudioResampler(format="fltp", layout="mono", rate=RATE)
        parts = []
        count = 0
        for frame in container.decode(audio=0):
            for converted in resampler.resample(frame):
                values = converted.to_ndarray().reshape(-1).astype(np.float64)
                parts.append(values)
                count += len(values)
            if count > RATE * 120:
                raise ValueError("Review sources must be shorter than two minutes")
        for converted in resampler.resample(None):
            parts.append(converted.to_ndarray().reshape(-1).astype(np.float64))
    audio = np.concatenate(parts)
    if not np.isfinite(audio).all() or np.max(np.abs(audio)) < 1e-6:
        raise ValueError("Source is silent or has invalid samples")
    return audio, info


def db(value: float) -> float:
    return round(20 * math.log10(max(float(value), 1e-12)), 3)


def measures(audio: np.ndarray) -> dict:
    spectrum = np.abs(np.fft.rfft(audio)) ** 2
    frequencies = np.fft.rfftfreq(len(audio), 1 / RATE)
    block = RATE // 100
    frames = audio[: len(audio) // block * block].reshape(-1, block)
    rms = np.sqrt(np.mean(frames ** 2, axis=1))
    active = np.flatnonzero(rms > max(float(rms.max()) * 0.04, 0.0001))
    energy_windows = np.convolve(rms ** 2, np.ones(min(10, len(rms))), mode="valid")
    return {
        "seconds": round(len(audio) / RATE, 4),
        "peakDbfs": db(np.max(np.abs(audio))),
        "rmsDbfs": db(np.sqrt(np.mean(audio ** 2))),
        "dcOffset": round(float(audio.mean()), 7),
        "spectralCentroidHz": round(float(np.sum(frequencies * spectrum) / spectrum.sum()), 1),
        "activeStartSeconds": round(float(active[0]) / 100, 3) if len(active) else 0,
        "activeEndSeconds": round(float(active[-1] + 1) / 100, 3) if len(active) else 0,
        "strongest100msStartSeconds": round(float(np.argmax(energy_windows)) / 100, 3),
    }


def inspect(game: Path) -> tuple[dict[str, np.ndarray], dict]:
    samples, audit = {}, {}
    for reference in SOURCES:
        audio, info = decode(source_path(game, reference))
        info.update(measures(audio))
        samples[reference] = audio
        audit[reference] = info
        print(f"{reference}: {info['seconds']:.3f}s, peak {info['peakDbfs']:.1f} dBFS, "
              f"RMS {info['rmsDbfs']:.1f} dBFS, centroid {info['spectralCentroidHz']:.0f} Hz; "
              f"active {info['activeStartSeconds']:.2f}-{info['activeEndSeconds']:.2f}s, "
              f"strongest 100ms at {info['strongest100msStartSeconds']:.2f}s")
    return samples, audit


def blank(seconds: float) -> np.ndarray:
    return np.zeros(round(seconds * RATE), dtype=np.float64)


def fade(audio: np.ndarray, attack: float = 0.006, release: float = 0.035) -> np.ndarray:
    audio = audio.copy()
    for seconds, start in ((attack, True), (release, False)):
        size = min(round(seconds * RATE), len(audio) // 2)
        ramp = np.sin(np.linspace(0, np.pi / 2, size)) ** 2
        if size:
            if start:
                audio[:size] *= ramp
            else:
                audio[-size:] *= ramp[::-1]
    return audio


def crop(audio: np.ndarray, start: float, end: float) -> np.ndarray:
    result = audio[round(start * RATE):round(end * RATE)].copy()
    if len(result) < RATE * 0.01:
        raise ValueError("Selected source region is too short")
    return result - result.mean()


def sample(audio: np.ndarray, start: float, end: float, speed: float = 1,
           gain: float = 0.15, lowpass: float = 7000) -> np.ndarray:
    result = crop(audio, start, end)
    result = np.interp(np.arange(0, len(result) - 1, speed), np.arange(len(result)), result)
    frequencies = np.fft.rfftfreq(len(result), 1 / RATE)
    response = 1 / np.sqrt(1 + (frequencies / lowpass) ** 8)
    response *= 1 - np.exp(-(frequencies / 65) ** 2)
    result = np.fft.irfft(np.fft.rfft(result) * response, n=len(result))
    result *= gain / max(float(np.sqrt(np.mean(result ** 2))), 1e-8)
    return fade(result)


def mix(destination: np.ndarray, source: np.ndarray, seconds: float, gain: float = 1) -> None:
    offset = round(seconds * RATE)
    size = min(len(source), len(destination) - offset)
    if offset < 0 or size <= 0:
        raise ValueError("Sound event is outside its review timeline")
    destination[offset:offset + size] += source[:size] * gain


def air(seconds: float, seed: int, body: float = 1, gain: float = 0.035) -> np.ndarray:
    """Periodic filtered turbulence with tube modes and slow pressure changes."""
    count = round(seconds * RATE)
    rng = np.random.default_rng(seed)
    frequencies = np.fft.rfftfreq(count, 1 / RATE)
    shape = (1 - np.exp(-(frequencies / 180) ** 2)) / np.sqrt(1 + (frequencies / 3400) ** 6)
    for frequency, width, strength in ((420, 85, 0.6), (970, 180, 0.3), (1730, 220, 0.18)):
        shape *= 1 + body * strength * np.exp(-0.5 * ((frequencies - frequency) / width) ** 2)
    spectrum = np.fft.rfft(rng.normal(size=count)) * shape
    result = np.fft.irfft(spectrum, n=count)
    result *= gain / np.sqrt(np.mean(result ** 2))
    t = np.arange(count) / RATE
    # Whole periods keep the modulation continuous at the loop boundary.
    modulation = 1 + 0.065 * np.sin(2 * np.pi * 3 * t / seconds) + 0.035 * np.sin(2 * np.pi * 11 * t / seconds)
    return result * modulation


def puff(seconds: float, seed: int, gain: float, attack: float = 0.012) -> np.ndarray:
    t = np.arange(round(seconds * RATE)) / RATE
    envelope = (1 - np.exp(-t / attack)) * np.exp(-t / (seconds * 0.28))
    return fade(air(seconds, seed, gain=gain) * envelope, attack, 0.055)


def write_wav(path: Path, audio: np.ndarray, loop: bool = False) -> dict:
    if not np.isfinite(audio).all() or np.max(np.abs(audio)) >= 0.96:
        raise ValueError(f"Invalid or overloaded mix: {path.name}")
    pcm = np.rint(audio * 32767).astype("<i2")
    with wave.open(str(path), "wb") as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(RATE)
        stream.writeframes(pcm.tobytes())
    with wave.open(str(path), "rb") as stream:
        if stream.getnframes() != len(audio) or stream.getframerate() != RATE:
            raise ValueError("WAV round-trip changed the duration or sample rate")
        restored = np.frombuffer(stream.readframes(stream.getnframes()), dtype="<i2").astype(np.float64) / 32767
    if np.max(np.abs(restored - audio)) > 1 / 32767:
        raise ValueError("WAV round-trip exceeded PCM quantization error")
    info = measures(restored)
    info["loop"] = loop
    info["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
    if loop:
        boundary = abs(float(restored[0] - restored[-1]))
        typical_limit = float(np.quantile(np.abs(np.diff(restored)), 0.999))
        if boundary > typical_limit:
            raise ValueError("Loop boundary exceeds normal sample-to-sample movement")
        info["boundaryStep"] = round(boundary, 7)
        info["normalStepLimit"] = round(typical_limit, 7)
    elif pcm[0] != 0 or pcm[-1] != 0:
        raise ValueError("One-shot must begin and end at zero")
    return info


def build(samples: dict[str, np.ndarray], audit: dict) -> None:
    def recording(name: str, start: float, end: float, **kwargs) -> np.ndarray:
        reference = next(reference for reference in SOURCES if reference.endswith(name + ".ogg"))
        used.setdefault(reference, []).append({"startSeconds": start, "endSeconds": end, **kwargs})
        return sample(samples[reference], start, end, **kwargs)

    used: dict[str, list] = {}
    # Regions below are selected from the source audit before composing.
    intake = recording("bellowlarge-in1", 0.01, 0.85, speed=1.4, gain=0.048, lowpass=4700)
    exhaust = recording("bellowlarge-out1", 0.05, 0.89, speed=1.55, gain=0.068, lowpass=5100)
    intake2 = recording("bellowlarge-in2", 0.02, 0.92, speed=1.5, gain=0.048, lowpass=4700)
    exhaust2 = recording("bellowlarge-out2", 0, 0.9, speed=1.65, gain=0.068, lowpass=5100)
    latch = recording("metaldoor", 0.19, 0.33, speed=1.08, gain=0.04, lowpass=4300)
    seat = recording("metaldoor-place", 0, 0.135, speed=0.9, gain=0.08, lowpass=3000)
    cargo = recording("chute", 0, 0.2, speed=0.9, gain=0.048, lowpass=3300)
    gears = recording("gearbox_turn", 2.22, 2.52, speed=1, gain=0.032, lowpass=4800)
    gear_teeth = recording("gears", 1.45, 1.72, gain=0.012, lowpass=3800)

    clips: dict[str, tuple[np.ndarray, bool]] = {}
    loops = {
        "airflow-low-loop": air(8, 101, gain=0.015),
        "airflow-full-loop": air(8, 102, body=1.2, gain=0.04),
    }
    for name, values in loops.items():
        clips[name] = values, True
    stroke = blank(1.3)
    mix(stroke, intake, 0.02)
    mix(stroke, exhaust, 0.69)
    mix(stroke, puff(0.32, 110, 0.043), 0.72)
    clips["bellows-stroke"] = fade(stroke), False

    launch = blank(0.65)
    mix(launch, latch, 0.005)
    mix(launch, puff(0.4, 111, 0.14), 0.045)
    clips["sender-launch"] = fade(launch), False
    travel = blank(0.8)
    t = np.arange(len(travel)) / RATE
    travel += air(0.8, 112, body=1.8, gain=0.085) * np.exp(-0.5 * ((t - 0.39) / 0.13) ** 2)
    mix(travel, cargo, 0.37, 0.4)
    clips["tube-package-pass"] = fade(travel), False
    arrival = blank(0.55)
    mix(arrival, puff(0.25, 113, 0.075), 0)
    mix(arrival, cargo, 0.06)
    mix(arrival, seat, 0.11, 0.65)
    clips["receiver-arrival"] = fade(arrival), False

    router = blank(1)
    mix(router, cargo, 0.08, 0.45)
    mix(router, latch, 0.3, 0.7)
    mix(router, gears, 0.42)
    mix(router, gear_teeth, 0.44)
    mix(router, seat, 0.63, 0.55)
    mix(router, puff(0.24, 114, 0.09), 0.69)
    clips["router-transfer"] = fade(router), False

    comparisons = []
    for name, air_gain, mechanism_gain in (("A-restrained", 0.5, 0.72), ("B-air-forward", 1.35, 0.88)):
        preview = air(12, 120, gain=0.018 * air_gain)
        # Four bellows cycles, then dispatch, travel, route, travel, receive.
        for index in range(4):
            mix(preview, intake if index % 2 == 0 else intake2, 0.3 + index * 1.4, mechanism_gain)
            mix(preview, exhaust if index % 2 == 0 else exhaust2, 0.99 + index * 1.4, mechanism_gain)
            mix(preview, puff(0.32, 130 + index, 0.043), 1.02 + index * 1.4, air_gain)
        mix(preview, launch, 6.2, mechanism_gain)
        mix(preview, travel, 6.6, air_gain)
        mix(preview, router, 7.4, mechanism_gain)
        mix(preview, travel, 8.25, air_gain)
        mix(preview, arrival, 8.9, mechanism_gain)
        clips[name] = fade(preview, 0.35, 1), False
        comparisons.append(name)

    showcase = blank(0.35)
    timeline = []
    for name in ("bellows-stroke", "sender-launch", "tube-package-pass", "router-transfer", "receiver-arrival"):
        values = clips[name][0]
        timeline.append({"sound": name, "startSeconds": round(len(showcase) / RATE, 3)})
        showcase = np.concatenate((showcase, values, blank(0.7)))
    # Follow the isolated sounds with both complete sequences.
    for name in comparisons:
        timeline.append({"sound": name, "startSeconds": round(len(showcase) / RATE, 3)})
        showcase = np.concatenate((showcase, clips[name][0], blank(1)))
    clips["all-sounds-preview"] = showcase, False

    OUTPUT.mkdir(parents=True, exist_ok=True)
    peak = max(float(np.max(np.abs(values))) for values, _ in clips.values())
    master = min(1, 0.88 / peak)
    files = {}
    for name, (values, loop) in clips.items():
        files[name + ".wav"] = write_wav(OUTPUT / (name + ".wav"), values * master, loop)
    manifest = {
        "schemaVersion": 1,
        "purpose": "Local pneumatic sound review; no runtime promotion",
        "output": "generated/pneumatic-sound-review/current",
        "sampleRate": RATE,
        "channels": 1,
        "pcmBits": 16,
        "masterGain": round(master, 6),
        "sourceAudit": audit,
        "sourceRegions": used,
        "files": files,
        "previewTimeline": timeline,
        "provenance": "Derived locally from installed Vintage Story recordings and deterministic noise synthesis. Vanilla-derived previews are local review material; prefer referencing installed vanilla sounds when implementing playback.",
        "verification": "Decoded every candidate; checked finite samples, headroom, PCM round-trip, zero one-shot edges, and loop boundary movement. Listening and in-game balance require maintainer review.",
    }
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (OUTPUT / "README.md").write_text(
        "# Pneumatic sound review\n\n"
        "Play `all-sounds-preview.wav` for the isolated sounds followed by two full sequences.\n\n"
        "- A-restrained: quieter air and lighter mechanical cues, intended for a busy workshop.\n"
        "- B-air-forward: stronger air movement and clearer package passage, intended to test readability.\n\n"
        "The full sequences start with four bellows strokes, then launch a package, pass a tube, "
        "turn the router, pass another tube, and receive the package.\n\n"
        "Individual mono WAVs include two eight-second airflow loops. "
        "The previews share one master gain so their relative levels can be compared.\n\n"
        "Sources and selected regions are recorded in `manifest.json`. "
        "These are local previews derived from installed game sounds and procedural turbulence. "
        "No runtime assets, simulation code, or save data were changed. "
        "Listen for distracting repetition, harsh air, and overly heavy latch sounds. "
        "The signal checks do not establish perceptual realism.\n",
        encoding="utf-8",
    )
    print(f"Built {len(files)} checked PCM WAVs in generated/pneumatic-sound-review/current")
    print(f"Shared master gain: {master:.4f}; highest peak: {db(peak * master):.2f} dBFS")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-path", type=Path, required=True)
    parser.add_argument("--inspect", action="store_true", help="Audit vanilla sources without building")
    args = parser.parse_args()
    samples, audit = inspect(args.game_path.resolve())
    if not args.inspect:
        build(samples, audit)


if __name__ == "__main__":
    main()
