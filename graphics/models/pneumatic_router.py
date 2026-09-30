"""Approved router A hardware, with independently controlled runtime joints."""
from dataclasses import replace
from gearwright_graphics.model import ModelPackage, Vec3
from graphics.models.pneumatic_transport import strip_cargo
from graphics.review.pneumatic_router import APPROVED_REFERENCE, candidate_shape


def hardware():
    shape = strip_cargo(candidate_shape('a-single-door'))
    removed = {e.name for e in shape.elements if e.name.startswith('context-')}
    shape.elements = [e for e in shape.elements if e.name not in removed]
    shape._refs = {n: ref for n, ref in shape._refs.items() if n not in removed}
    # Fit the base crosspieces between the frame and central spine. Previously
    # the full-width beams shared their top and bottom faces at every crossing.
    base_repairs = []
    for element in shape.elements:
        if element.name.startswith('bypass-') and element.name.endswith('-glass-outer'):
            element = replace(element, from_=Vec3(-7.39, element.from_.y, element.from_.z))
        elif element.name.startswith('bypass-') and element.name.endswith(('-glass-before', '-glass-after')):
            element = replace(element, to=Vec3(element.to.x, 3.18, element.to.z))
        elif element.name == 'base-index-beam':
            element = replace(element, from_=Vec3(7.3, .2, 2.4), to=Vec3(8.7, .6, 13.6))
        elif element.name.startswith('base-crossbeam-') or element.name == 'base-table-beam':
            base_repairs.append(replace(element, name=element.name + '-right',
                                        from_=Vec3(8.7, element.from_.y, element.from_.z),
                                        to=Vec3(13.6, element.to.y, element.to.z)))
            element = replace(element, from_=Vec3(2.4, element.from_.y, element.from_.z),
                              to=Vec3(7.3, element.to.y, element.to.z))
        base_repairs.append(element)
    shape.elements = base_repairs
    for number in range(1, 5):
        parent = shape._refs[f'port-{number}']
        pivot = shape.pivot(f'port-{number}-valve-pivot', (-6.65, 4.15, 0), parent=parent)
        updated = []
        for e in shape.elements:
            if e.name == f'port-{number}-isolation-valve':
                e = replace(e, parent=pivot.name, from_=Vec3(-.44, -.06, -.98), to=Vec3(.44, .06, .98))
            elif e.name == f'port-{number}-valve-handle':
                e = replace(e, parent=pivot.name, from_=Vec3(-.93, -.07, -.45), to=Vec3(-.77, .09, .45))
            updated.append(e)
        shape.elements = updated
        shape.box(f'router-cap-{number}', (-7.26, 3.06, -1.1), (-6.04, 3.18, 1.1),
                  parent=shape._refs[f'bypass-{number}'], texture='#glass', faces=('up',),
                  render_pass=1, uv=(0, 0, 16, 16), group='conditional-cap')
    shape.id = 'pneumatic-router'
    return shape


def build():
    package = ModelPackage('pneumatic_router')
    package.shape(hardware(), 'assets/gearwright/shapes/block/pneumatic-router.json')
    return package
