"""Inspect and prepare installed Vintage Story water recordings for irrigation.

Only logical game references and hashes enter the generated manifest. Source
recordings stay in the game installation and are never copied into the repo.
"""

import argparse
import hashlib
import json
from pathlib import Path

import av
import numpy as np

RATE = 24000
SOURCES = ('survival/sounds/effect/watering-loop.ogg', 'survival/sounds/effect/water-pour.ogg')


def decode(path):
    parts = []
    resampler = av.AudioResampler(format='fltp', layout='mono', rate=RATE)
    with av.open(str(path)) as container:
        for frame in container.decode(audio=0):
            parts.extend(item.to_ndarray().reshape(-1) for item in resampler.resample(frame))
        parts.extend(item.to_ndarray().reshape(-1) for item in resampler.resample(None))
    return np.concatenate(parts).astype(np.float64)


def load(assets):
    clips, provenance = [], []
    for reference in SOURCES:
        path = Path(assets) / reference
        values = decode(path)
        if len(values) < RATE or not np.isfinite(values).all():
            raise ValueError('Invalid installed water recording: ' + reference)
        frequencies = np.fft.rfftfreq(len(values), 1 / RATE)
        power = abs(np.fft.rfft(values)) ** 2
        provenance.append({'reference': reference.replace('/', ':', 1),
                           'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                           'seconds': round(len(values) / RATE, 3),
                           'rms': round(float(np.sqrt(np.mean(values ** 2))), 6),
                           'energyAbove6000Hz': round(float(power[frequencies > 6000].sum() / power.sum()), 6)})
        # Remove rumble and bright spray edges, retaining the recorded wet detail.
        response = (1 - np.exp(-(frequencies / 230) ** 2)) / np.sqrt(1 + (frequencies / 2800) ** 10)
        values = np.fft.irfft(np.fft.rfft(values) * response, n=len(values))
        values /= max(float(np.sqrt(np.mean(values ** 2))), 1e-9)
        clips.append(values)
    return clips, provenance


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-assets', required=True, type=Path)
    args = parser.parse_args()
    _, report = load(args.game_assets)
    print(json.dumps(report, indent=2))
