"""Measure audible gaps in decoded mono loop assets, independently of playback."""

import argparse
import json
from pathlib import Path

import av
import numpy as np


def continuity_profile(values, rate):
    # 50 ms windows expose the several-times-per-second gating heard in-game.
    width = round(rate * .05)
    levels = np.sqrt(np.mean(values[:len(values) // width * width].reshape(-1, width) ** 2, axis=1))
    rms = float(np.sqrt(np.mean(values ** 2)))
    quiet = levels < rms * .2
    transitions = np.diff(np.r_[False, quiet, False].astype(int))
    starts, ends = np.flatnonzero(transitions == 1), np.flatnonzero(transitions == -1)
    return {
        'rms': round(rms, 6),
        'peak50msRms': round(float(levels.max()), 6),
        'quietFraction': round(float(quiet.mean()), 6),
        'quietEntriesPerSecond': round(len(starts) / (len(values) / rate), 6),
        'longestQuietSeconds': round(float(max(ends - starts, default=0)) * width / rate, 6),
        'envelopeVariation': round(float(levels.std() / max(levels.mean(), 1e-12)), 6),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('files', type=Path, nargs='+')
    args = parser.parse_args()
    for path in args.files:
        with av.open(str(path)) as container:
            rate = container.streams.audio[0].sample_rate
            values = np.concatenate([frame.to_ndarray().reshape(-1) for frame in container.decode(audio=0)])
        print(json.dumps({'file': path.name, **continuity_profile(values, rate)}))


if __name__ == '__main__':
    main()
