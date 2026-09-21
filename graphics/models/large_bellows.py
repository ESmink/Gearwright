"""Approved A linkage and the authorized direct upper-board mounting adjustment.

Only Gearwright attachment geometry is emitted. The body stays a game asset.
"""
from dataclasses import replace
from gearwright_graphics.model import ModelPackage, Vec3
from graphics.review.large_bellows import candidate_shape
from graphics.review.large_bellows_top import TOP_ROCKER, TOP_OUTPUT, lift_length
from graphics.models.parts.large_bellows_frame import upper_frame

DYNAMIC = {'gw-bellows-rod', 'gw-bellows-rocker', 'gw-bellows-plate-pin',
           'gw-bellows-lift-front', 'gw-bellows-lift-back'}


def mechanism(top=False):
    source = {'textureWidth': 16, 'textureHeight': 16, 'elements': [],
              'textures': {'plainoak': 'game:block/wood/plainoak'}}
    shape = candidate_shape(source, 'a-reinforced-wood', 'through-shaft')
    shape.id = 'large-bellows-' + ('top' if top else 'bottom')
    shape.elements = [e for e in shape.elements if e.name.startswith('gw-bellows-')]
    shape.animations = []
    shape._refs = {n: ref for n, ref in shape._refs.items() if n.startswith('gw-bellows-')}
    for i, element in enumerate(shape.elements):
        if top and (element.name.startswith('gw-bellows-rocker-pin-output') or
                    element.name.startswith('gw-bellows-rocker-strap-') and element.name.endswith('-output')):
            element = replace(element, from_=replace(element.from_, x=element.from_.x + TOP_OUTPUT - 4.32),
                              to=replace(element.to, x=element.to.x + TOP_OUTPUT - 4.32))
            shape.elements[i] = element
        if element.name in DYNAMIC:
            position = (*((TOP_ROCKER if top else (16.8, -2)) if element.name == 'gw-bellows-rocker' else (0, 0)),
                        element.from_.z)
            if element.name != 'gw-bellows-rocker': position = (0, 0, element.from_.z)
            if top and element.name.startswith('gw-bellows-lift-'):
                position = (0, 0, 3.2 if element.name.endswith('front') else 12.8)
            shape.elements[i] = replace(element, from_=Vec3.of(position), to=Vec3.of(position),
                rotation_origin=Vec3.of(position), rotation=Vec3(0, 0, 0))
        elif top and element.name.startswith('gw-bellows-lift-'):
            delta = (lift_length() - 6.6) / 2
            low = element.from_.y + (-delta if element.from_.y < 0 else delta)
            high = element.to.y + (-delta if element.to.y < 0 else delta)
            shape.elements[i] = replace(element, from_=replace(element.from_, y=low), to=replace(element.to, y=high))
        elif top and element.name.startswith('gw-bellows-clevis-'):
            dz = -2 if element.name.endswith('front') else 2
            shape.elements[i] = replace(element, from_=replace(element.from_, y=-1.55, z=element.from_.z + dz),
                                       to=replace(element.to, y=.55, z=element.to.z + dz))
        elif top and element.name.startswith(('gw-bellows-pin', 'gw-bellows-rocker-pin-output')):
            shape.elements[i] = replace(element, from_=replace(element.from_, z=-5.5), to=replace(element.to, z=5.5))
        elif top and element.name.startswith('gw-bellows-rocker-pin-fulcrum'):
            shape.elements[i] = replace(element, from_=replace(element.from_, z=-7.5), to=replace(element.to, z=7.5))
    if top:
        shape.elements = [e for e in shape.elements if not e.name.startswith('gw-bellows-fulcrum-hanger-')]
        upper_frame(shape)
    return shape


def build():
    package = ModelPackage('large_bellows')
    for top in (False, True):
        package.shape(mechanism(top), f'assets/gearwright/shapes/block/large-bellows-{("top" if top else "bottom")}.json')
    return package
