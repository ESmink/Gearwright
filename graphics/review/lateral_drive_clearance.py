"""Exact cuboid separation checks for the shared-drive review candidates."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

from gearwright_graphics.animation import evaluate_animation
from gearwright_graphics.compiler import compile_shape
from gearwright_graphics.geometry import _matrix
from graphics.review.lateral_drive_mount import DESCRIPTIONS, REVIEW_ROOT, candidate_shape


def posed_boxes(compiled, phase):
    poses = evaluate_animation(compiled['animations'][0], phase)
    boxes = []
    def visit(elements, parent):
        for element in elements:
            start = np.asarray(element['from'], dtype=float) / 16
            end = np.asarray(element['to'], dtype=float) / 16
            transform = parent @ _matrix(dict(element, **poses.get(element['name'], {})))
            half = (end - start) / 2
            if np.all(half > 0):
                center = (transform @ np.r_[(start + end) / 2, 1])[:3]
                axes = transform[:3, :3]
                extent = np.abs(axes) @ half
                name = element['name']
                owner = '-'.join(name.split('-')[:2]) if name.startswith('device-') else 'shaft'
                boxes.append((name, owner, center, axes, half, center - extent, center + extent))
            child = np.eye(4)
            child[:3, 3] = start
            visit(element.get('children', []), transform @ child)
    visit(compiled['elements'], np.eye(4))
    return boxes


def intersects(a, b):
    delta = b[2] - a[2]
    directions = list(a[3].T) + list(b[3].T)
    directions += [np.cross(x, y) for x in a[3].T for y in b[3].T]
    for direction in directions:
        length = np.linalg.norm(direction)
        if length < 1e-8:
            continue
        direction = direction / length
        reach = np.abs(direction @ a[3]) @ a[4] + np.abs(direction @ b[3]) @ b[4]
        if reach - abs(delta @ direction) <= 1e-8:
            return False
    return True


def audit(candidate, phases=range(0, 361, 5), *, state='four-pumps', through=True):
    compiled = compile_shape(candidate_shape(candidate, state, through=through))
    collisions = {}
    for phase in phases:
        boxes = posed_boxes(compiled, phase)
        lows = np.asarray([box[5] for box in boxes])
        highs = np.asarray([box[6] for box in boxes])
        owners = np.asarray([box[1] for box in boxes])
        rod_parts = np.asarray(['gw-connecting-rod-' in box[0] for box in boxes])
        for i, box in enumerate(boxes):
            # Also check each moving connecting rod against its own device's
            # guides, crosshead and head. Contacts within the assembled rod or
            # within the pressure body are intentional construction joints.
            possible = np.where(np.all(np.minimum(highs, box[6]) - np.maximum(lows, box[5]) > 1e-8, axis=1)
                                & ((owners != box[1]) | (rod_parts != rod_parts[i]))
                                & (np.arange(len(boxes)) > i))[0]
            for j in possible:
                other = boxes[j]
                key = (box[0], other[0])
                if key not in collisions and intersects(box, other):
                    collisions[key] = phase
    return [{'first': pair[0], 'second': pair[1], 'phase': phase} for pair, phase in collisions.items()]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--step', type=int, choices=(1, 5), default=1)
    args = parser.parse_args()
    report = {}
    for candidate in DESCRIPTIONS:
        for state in ('one-pump', 'opposed-pumps', 'adjacent-pumps', 'three-pumps', 'four-pumps'):
            collisions = audit(candidate, range(0, 361, args.step), state=state)
            report[state] = {'sampleDegrees': args.step, 'collisions': collisions}
            print(f'{candidate}/{state}: {len(collisions)} intersecting part pairs', flush=True)
            for collision in collisions[:16]:
                print(collision, flush=True)
    path = Path(REVIEW_ROOT) / 'current/clearance-report.json'
    path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return 1 if any(item['collisions'] for item in report.values()) else 0


if __name__ == '__main__':
    raise SystemExit(main())
