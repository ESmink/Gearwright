"""Direct upper-board attachment, using the approved wooden reducing linkage."""
import math
from functools import lru_cache
from graphics.review.large_bellows import circle_joint

TOP_CRANK = (24, 24)
TOP_ROCKER = (16.8, 30)
TOP_OUTPUT = 3.0


def rotate(vector, degrees):
    a = math.radians(degrees)
    return (vector[0] * math.cos(a) - vector[1] * math.sin(a),
            vector[0] * math.sin(a) + vector[1] * math.cos(a))


def upper_pin(fill):
    # Native fold hinges and a clevis 1.5 units above the upper oak board.
    vectors = (rotate((0, .4), 1.5 * fill), rotate((0, .6), 9.5 * fill),
               rotate((14.8095, 2.5), 11.5 * fill))
    return tuple((4, 10.8)[i] + sum(v[i] for v in vectors) for i in range(2))


def top_drive(phase):
    a = math.radians(phase)
    journal = (24 + 3 * math.sin(a), 24 + 3 * math.cos(a))
    pin, angle = circle_joint(TOP_ROCKER, (7.2, 0), journal, 7)
    output = tuple(TOP_ROCKER[i] + TOP_OUTPUT / 7.2 * (pin[i] - TOP_ROCKER[i]) for i in range(2))
    return journal, pin, output, angle


@lru_cache(maxsize=1)
def lift_length():
    ranges = [sorted(math.dist(top_drive(p)[2], upper_pin(f)) for f in (0, 1)) for p in range(360)]
    low, high = max(r[0] for r in ranges), min(r[1] for r in ranges)
    if low >= high:
        raise ValueError(f'Upper link cannot close: {low} >= {high}')
    return (low + high) / 2


def top_state(phase):
    journal, input_pin, output_pin, angle = top_drive(phase)
    low, high = 0., 1.
    for _ in range(45):
        middle = (low + high) / 2
        if math.dist(output_pin, upper_pin(middle)) > lift_length():
            low = middle
        else:
            high = middle
    fill = (low + high) / 2
    return journal, input_pin, output_pin, upper_pin(fill), angle, fill


def lower_angle(fill):
    """Passive lower board: suction lifts it; the return stroke admits fresh air."""
    return -12 + 8 * fill


def attachment_scene(document, *, top, both=False):
    from collections import OrderedDict
    from dataclasses import replace
    from gearwright_graphics.model import Shape, ElementRef, Keyframe
    from graphics.models.large_bellows import mechanism
    from graphics.models.parts.reciprocating_drive import rebuild_crank
    from graphics.review.lateral_motion_system import _crank_state, _pivot
    from graphics.review.large_bellows import (candidate_shape, import_bellows,
        add_chamber_pose, finish_animation)
    if not top:
        shape = candidate_shape(document, 'a-reinforced-wood', 'through-shaft')
    else:
        shape = mechanism(True)
        import_bellows(document, shape, exclude=('LINKAGE-1d',))
    crank = _crank_state(through=True, throw=3)
    rebuild_crank(crank, through=True)
    for texture in crank.textures.values():
        shape.texture(texture.key, texture.location, size=texture.size)
    prefix = 'inactive-' if both else ''
    mount = _pivot(shape, prefix + 'gw-shaft-mount', (24, 24, 8), rotation=(0, 90, 0))
    for e in crank.elements:
        e = replace(e, name=prefix + e.name, parent=prefix + e.parent if e.parent else mount.name)
        shape.elements.append(e)
        shape._refs[e.name] = ElementRef(shape.id, e.name)
    if not top:
        # The unused shaft can turn independently without moving a second linkage.
        for frame in shape.animations[0].keyframes:
            frame.elements['inactive-gw-crank-phase'] = {'rotationX': -frame.frame * 2,
                                                       'rotShortestDistanceX': False}
        return shape
    frames = []
    def bar(a, b):
        return {'offsetX': (a[0] + b[0]) / 2, 'offsetY': (a[1] + b[1]) / 2,
                'rotationZ': math.degrees(math.atan2(a[0] - b[0], b[1] - a[1]))}
    for phase in range(361):
        journal, input_pin, output, pin, angle, fill = top_state(phase)
        poses = OrderedDict((
            ('gw-crank-phase', {'rotationX': phase, 'rotShortestDistanceX': False}),
            ('gw-bellows-rod', bar(journal, input_pin)),
            ('gw-bellows-rocker', {'rotationZ': angle}),
            ('gw-bellows-plate-pin', {'offsetX': pin[0], 'offsetY': pin[1], 'rotationZ': 11.5 * fill}),
        ))
        for side in ('front', 'back'): poses['gw-bellows-lift-' + side] = bar(pin, output)
        add_chamber_pose(poses, set(shape._refs), lower_angle(fill), fill,
                         fill < top_state(phase - .1)[-1])
        frames.append(Keyframe(phase, poses))
    return finish_animation(shape, frames)


if __name__ == '__main__':
    values = [top_state(p)[-1] for p in range(361)]
    print('Upper fill range:', min(values), max(values), 'link length:', lift_length())
