"""Approved A wet end, with inboard drops exposing the terminal pipe collars.

Approval: a-clean-folded-pipes in generated/pump-refinement-review/current,
with the maintainer's requested narrower descending pipes (2026-09-11).
Only stationary copper changes. Motion roots and pipe attachment planes stay.
"""

import numpy as np

UPPER_PISTON_BOTTOM = -11.78
STROKE = 6.0
CAPACITY = 4.0
CLEARANCE = .05
FLOOR = UPPER_PISTON_BOTTOM - STROKE - STROKE * CLEARANCE / CAPACITY
OLD_FLOOR = -18.35
DROP_OUTER_X = 7.1
DROP_INNER_X = DROP_OUTER_X - .5
FACE_AXES = (('west', 0, -1), ('east', 0, 1), ('down', 1, -1),
             ('up', 1, 1), ('north', 2, -1), ('south', 2, 1))


def _contains(box, point):
    return all(a < p < b for a, p, b in zip(box[0], point, box[1]))


def _surface_solids(shape, solids, bores):
    """Emit disjoint VS cuboids around connected empty passages.

    Only union-boundary faces render. Greedy merging reduces the partition
    without reintroducing buried walls or duplicate faces at pipe bends.
    """
    axes = [sorted({box[end][axis] for box in solids + bores for end in (0, 1)})
            for axis in range(3)]
    dims = tuple(len(axis) - 1 for axis in axes)
    occupied = np.zeros(dims, dtype=bool)
    for index in np.ndindex(dims):
        point = tuple((axes[d][index[d]] + axes[d][index[d] + 1]) / 2 for d in range(3))
        occupied[index] = any(_contains(box, point) for box in solids) and not any(
            _contains(box, point) for box in bores)
    masks = np.zeros(dims, dtype=np.uint8)
    for index in np.ndindex(dims):
        if not occupied[index]:
            continue
        for bit, (_, axis, direction) in enumerate(FACE_AXES):
            neighbor = list(index)
            neighbor[axis] += direction
            if not 0 <= neighbor[axis] < dims[axis] or not occupied[tuple(neighbor)]:
                masks[index] |= 1 << bit
    used = np.zeros(dims, dtype=bool)
    part = 0
    for start in np.ndindex(dims):
        mask = masks[start]
        if not mask or used[start]:
            continue
        end = [v + 1 for v in start]
        for axis in (0, 1, 2):
            while end[axis] < dims[axis]:
                trial = end.copy()
                trial[axis] += 1
                region = tuple(slice(start[d], trial[d]) for d in range(3))
                if np.any(used[region]) or not np.all(masks[region] == mask):
                    break
                end = trial
        region = tuple(slice(start[d], end[d]) for d in range(3))
        used[region] = True
        shape.box(f'gw-pump-wet-body-{part}',
                  tuple(axes[d][start[d]] for d in range(3)),
                  tuple(axes[d][end[d]] for d in range(3)),
                  texture='#gw-copper', parent=shape.ref('gw-pump-mount'),
                  faces=tuple(name for bit, (name, _, _) in enumerate(FACE_AXES) if mask & (1 << bit)),
                  group='pump-wet-body')
        part += 1


def rebuild_wet_end(shape):
    replaced = {e.name for e in shape.elements if
        (e.group in ('pump-input', 'pump-output') and 'end-coupling' not in e.name)
        or e.name.startswith('gw-cylinder-bottom-cap-')
        or e.name.startswith('gw-cylinder-corner-')
        or e.name in ('gw-single-cylinder-input-wall-solid', 'gw-single-cylinder-output-wall-solid')}
    solids = []
    for element in shape.elements:
        if element.name in replaced and element.group not in ('pump-input', 'pump-output'):
            lo, hi = list(element.from_.values()), list(element.to.values())
            if abs(hi[1] - OLD_FLOOR) < 1e-8:
                hi[1] = FLOOR
            if abs(lo[1] - OLD_FLOOR) < 1e-8:
                lo[1] = FLOOR
            solids.append((tuple(lo), tuple(hi)))
    shape.elements = [e for e in shape.elements if e.name not in replaced]
    bores = []
    for direction in (-1, 1):
        def box(x0, x1, y0, y1, z0, z1):
            xs = sorted((direction * x0, direction * x1))
            return ((xs[0], y0, z0), (xs[1], y1, z1))
        solids.extend((box(4.5, 8, -18, -14, -2, 2),
                       box(4.5, DROP_OUTER_X, -23.25, -16, -2, 2),
                       box(.5, DROP_OUTER_X, -23.25, -19.25, -2, 2),
                       box(.5, 4.5, -21.25, FLOOR, -2, 2)))
        bores.extend((box(5, 8.1, -17.5, -14.5, -1.5, 1.5),
                      box(5, DROP_INNER_X, -22.75, -16, -1.5, 1.5),
                      box(1, DROP_INNER_X, -22.75, -19.75, -1.5, 1.5),
                      box(1, 4, -21.25, FLOOR + .01, -1.5, 1.5)))
    _surface_solids(shape, solids, bores)
