"""Export matched-gain before/after WAV reels from the preserved local baseline.

Run after saving the old machines directory under
generated/machine-sound-review/current/before. Never normalizes the two versions
independently; the difference in detail and loudness remains reviewable.
"""

from pathlib import Path
import json
import wave

import av
import numpy as np

from machine_sound_assets import ROOT, RATE, OUTPUT, soften

REVIEW = ROOT / 'generated/machine-sound-review/current'
REELS = {
    'mechanisms': [('sender-work', 0, 1.6, .34), ('receiver-work', 0, 1.6, .34),
                   ('router-turn', 0, 1, .34), ('outlet-arrival', 0, .9, .34),
                   ('pressure-creak1', 0, 1.8, .25), ('pump-valve', 0, .14, .20),
                   ('ratchet-pawl', 0, .1, .12), ('ratchet-engage', 0, .4, .17)],
    'flow-and-bearings': [('airflow', 17, 5, .4), ('bellows', 17, 5, .5),
                         ('water-pipe', 17, 5, .5), ('water-outlet', 17, 5, .5),
                         ('water-sprinkler', 17, 5, .5), ('water-irrigator', 17, 5, .5),
                         ('flywheel', 17, 5, .32), ('pump-mechanism', 17, 5, .36),
                         ('transmission-bearing', 17, 5, .16)]}


def excerpt(path: Path, start: float, seconds: float) -> np.ndarray:
    parts = []
    count = 0
    with av.open(str(path)) as container:
        for frame in container.decode(audio=0):
            parts.append(frame.to_ndarray().reshape(-1))
            count += len(parts[-1])
            if count >= round((start + seconds) * RATE):
                break
    clip = np.concatenate(parts)[round(start * RATE):round((start + seconds) * RATE)].copy()
    # Some historical short cues lost their final codec block. Keep reel
    # positions equal with silence rather than inventing the missing tail.
    return np.pad(clip, (0, max(0, round(seconds * RATE) - len(clip))))


def main():
    timeline = {}
    for reel, clips in REELS.items():
        for version, directory in (('before', REVIEW / 'before/machines'), ('after', OUTPUT)):
            parts, entries, cursor = [], [], 0
            for name, start, seconds, gain in clips:
                clip = excerpt(directory / (name + '.ogg'), start, seconds)
                clip = soften(clip, .02, .035) * gain
                entries.append({'cue': name, 'start': round(cursor / RATE, 3),
                                'seconds': seconds, 'sourceOffset': start, 'playbackGain': gain})
                pause = np.zeros(round(.45 * RATE))
                parts.extend((clip, pause))
                cursor += len(clip) + len(pause)
            filename = f'{reel}-{version}.wav'
            with wave.open(str(REVIEW / filename), 'wb') as output:
                output.setnchannels(1)
                output.setsampwidth(2)
                output.setframerate(RATE)
                output.writeframes((np.concatenate(parts).clip(-1, 1) * 32767).astype('<i2').tobytes())
            timeline[filename] = entries
            print(f'{filename}: {cursor / RATE:.1f}s')
    (REVIEW / 'timeline.json').write_text(json.dumps(timeline, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
