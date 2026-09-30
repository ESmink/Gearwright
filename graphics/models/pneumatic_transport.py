"""Approved direct-line hardware. Review builders never write these runtime paths."""
from dataclasses import replace
from collections import OrderedDict
from gearwright_graphics.model import ModelPackage, Shape, Face, Vec3
from graphics.review.pneumatic_direct_line import Canvas, TEXTURES, tube, ring_x, ring_y
from graphics.review.pneumatic_sender import candidate_shape as sender
from graphics.review.pneumatic_receiver import candidate_shape as receiver
from graphics.review.pneumatic_accumulator import candidate_shape as accumulator

APPROVALS = {
    'sender': 'fee034d4eea28d6a4b379841e6d2b68a853f2a7316d9a43709d3a4e698a661f9',
    'receiver': 'be424a07b652259f211efecbb0ea8d122def1b7c05d561e16a84abb345d90bb0',
    'accumulator': '811b4e5a252d2d5194eb1270bdfb4786403f0fc5496b2e5785ffbf64f72f801b',
}
PORT_APPROVALS = {
    'sender': 'e192725742960386a0f1abe3d5ddc1ff58c49dbfb221e9327e2b14098b7bbac5',
    'receiver': '708b3e8789b875391a25d59cbda0b38d2c8dc086ae0c5c7f1a00118130f964a8',
    'terminal': '8d66640f89e51998f5a46dcac8660d7cb080c38d1a8650d1efd3756e1bd89e1c',
    'sender-terminal': '268e85140f38309822ae43297b2091909beea5bf432082fc8aacf9af2c7e4935',
    'receiver-terminal': '45a511f0b48069bd259ce7ea7c935d4cbb6effbe3299bccdd2a958959dc98cfc',
}
EXTENSION = 2.25  # Reach past the native chest inset, then overlap by a quarter pixel.
GLASS_RECESS = .04  # Separate glazed faces from the metal's mating planes.


def base_hardware(kind, *, include_cargo=False):
    if kind == 'sender':
        shape = sender('a-rack-lift')
    elif kind == 'receiver':
        shape = receiver('a1-return-cam')
    elif kind == 'accumulator':
        shape = accumulator('a-leather-bellows')
    else:
        shape = Shape('pneumatic-' + kind)
        for key, (location, size) in TEXTURES.items():
            shape.texture(key, location, size=size)
        tube(Canvas(shape), 'b-rotary-pocket', elbow=kind == 'elbow')
    if not include_cargo:
        strip_cargo(shape)
    shape.id = 'pneumatic-' + kind
    return shape


def strip_cargo(shape):
    # Cargo comes from its actual ItemStack in the client renderer. Retain every
    # approved hardware cuboid, pivot, material and animation track unchanged.
    removed = {e.name for e in shape.elements if e.group == 'cargo' or 'representative-cargo' in e.name}
    shape.elements = [e for e in shape.elements if e.name not in removed]
    shape._refs = {n: ref for n, ref in shape._refs.items() if n not in removed}
    shape.animations = [replace(a, keyframes=tuple(replace(k, elements={n: v for n, v in k.elements.items()
                              if n not in removed}) for k in a.keyframes)) for a in shape.animations]
    return shape


def inventory_ports(shape, state):
    """Approved A brass sleeve, including the receiver clearance revision."""
    is_sender, is_receiver = state.startswith('sender'), state.startswith('receiver')
    shape.texture('shadow', 'gearwright:block/inspection-shadow', size=(4, 4))
    shape.texture('iron', 'game:block/metal/sheet-plain/iron2', size=(32, 32))
    updated = []
    for e in shape.elements:
        if is_receiver and e.name == 'host-glass-back':
            for label, x0, x1, y0, y1 in (('left', .8, 10.35, 4.9, 10.9),
                    ('right', 11.65, 15.2, 4.9, 10.9), ('above-gate', 10.35, 11.65, 5.85, 10.9)):
                updated.append(replace(e, name=e.name + '-' + label, from_=Vec3(x0, y0, 10.9), to=Vec3(x1, y1, 11.1)))
            continue
        if is_receiver and e.name.startswith('gate-closed-stop-'):
            e = replace(e, to=Vec3(e.to.x, 5.06, e.to.z))
        if (is_sender and (e.name.startswith(('glass-front-load-', 'loader-', 'host-shoulder-')) or e.name == 'glass-back-load') or
                is_receiver and e.name.startswith(('roller-glass-', 'roller-side-', 'outlet-pane-'))):
            e = replace(e, faces=OrderedDict((f, Face('#brass')) for f in
                ('north', 'east', 'south', 'west', 'up', 'down')), render_pass=0, group='branch')
        updated.append(e)
    shape.elements = updated
    c = Canvas(shape)
    if is_sender or is_receiver:
        ring_y(c, 'inventory-spigot', -EXTENSION, .8, 'brass', low=5.4, high=10.6, width=.55)
        ring_y(c, 'inventory-cuff', -.35, .4, 'brass', low=5.1, high=10.9, width=.45)
        ring_y(c, 'inventory-dark-liner', -EXTENSION + .1, 2.9 if is_sender else 1.3,
               'shadow', low=5.94, high=10.06, width=.15)
        shape.box('inventory-dark-depth', (6.08, -.18, 6.08), (9.92, -.12, 9.92),
                  texture='#shadow', group='occlusion', shade=False)
        if is_sender:
            shape.box('sender-throat-shadow', (5.94, 3.10, 5.94), (10.06, 3.14, 10.06),
                      texture='#shadow', group='occlusion', shade=False)
            shape.box('sender-slot-shadow', (8.84, .3, 5.58), (9.56, 4.9, 5.60),
                      texture='#shadow', group='occlusion', shade=False)
    if state == 'terminal' or state.endswith('-terminal'):
        start = len(shape.elements)
        ring_x(c, 'terminal-spigot', 15.5, 16 + EXTENSION, 'brass', low=4.9, high=11.1, width=.4)
        ring_x(c, 'terminal-cuff', 15.35, 16.2, 'brass', low=4.65, high=11.35, width=.45)
        ring_x(c, 'terminal-dark-liner', 15.55, 16 + EXTENSION - .1, 'shadow', low=5.29, high=10.71, width=.14)
        shape.box('terminal-dark-depth', (16.05, 5.42, 5.42), (16.11, 10.58, 10.58),
                  texture='#shadow', group='occlusion', shade=False)
        if state == 'elbow-terminal':
            # The canonical elbow exits upward. Mount the exact approved
            # fitting on that face; the host renderer supplies world orientation.
            mount = shape.pivot('terminal-mount', (8, 8, 8), group='terminal')
            shape.elements[-1] = replace(shape.elements[-1], rotation=Vec3(0, 0, 90))
            for index in range(start, len(shape.elements) - 1):
                e = shape.elements[index]
                shape.elements[index] = replace(e, parent=mount.name,
                    from_=Vec3(e.from_.x - 8, e.from_.y - 8, e.from_.z - 8),
                    to=Vec3(e.to.x - 8, e.to.y - 8, e.to.z - 8))
    return shape


def repair_surfaces(shape, state):
    """Repair the approved fittings' coplanar faces without changing their design."""
    updated = []
    for e in shape.elements:
        lo, hi = list(e.from_.values()), list(e.to.values())
        if e.render_pass == 1:
            for face, axis, high in (('west', 0, False), ('east', 0, True),
                    ('down', 1, False), ('up', 1, True), ('north', 2, False), ('south', 2, True)):
                if face in e.faces and e.faces[face].enabled:
                    if high:
                        hi[axis] -= GLASS_RECESS
                    else:
                        lo[axis] += GLASS_RECESS
        # The original inventory rim already occupies y=0..0.3/0.8.
        # The extension must meet it, not duplicate four of its wall surfaces.
        if e.name.startswith('inventory-spigot-'):
            hi[1] = 0
        if e.name.startswith('terminal-') and e.name != 'terminal-mount':
            # Terminal elbow children are authored relative to the block centre.
            offset = 8 if e.parent == 'terminal-mount' else 0
            if e.name.startswith('terminal-spigot-'):
                lo[0] = 16.2 - offset
            elif e.name.startswith('terminal-cuff-'):
                lo[0] = 16 - offset
                # Match the spigot bore. Each collar occupies its own axial
                # interval so the output has a continuous four-sided lip.
                part = e.name.removeprefix('terminal-cuff-')
                if part == 'bottom': hi[1] = 5.3 - offset
                if part == 'top': lo[1] = 10.7 - offset
                if part in ('front', 'back'):
                    lo[1], hi[1] = 5.3 - offset, 10.7 - offset
                    if part == 'front': hi[2] = 5.3 - offset
                    else: lo[2] = 10.7 - offset
        updated.append(replace(e, from_=Vec3(*lo), to=Vec3(*hi)))
    shape.elements = updated
    return shape


def hardware(kind):
    base = kind.removesuffix('-terminal')
    shape = base_hardware(base)
    if base in ('sender', 'receiver') or kind.endswith('-terminal'):
        inventory_ports(shape, kind)
    if base != 'accumulator':
        repair_surfaces(shape, kind)
    shape.id = 'pneumatic-' + kind
    return shape


def build():
    package = ModelPackage('pneumatic_transport')
    for kind in ('straight', 'elbow', 'sender', 'receiver', 'accumulator',
                 'straight-terminal', 'elbow-terminal', 'sender-terminal', 'receiver-terminal'):
        package.shape(hardware(kind), f'assets/gearwright/shapes/block/pneumatic-{kind}.json')
    return package
