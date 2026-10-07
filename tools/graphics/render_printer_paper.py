"""Render the actual SDK receipt meshes/text with the CPU photoshoot rasterizer.

Export first with the contracts' --printer-preview command. No runtime assets or
approval references are changed. Output stays in the disposable photoshoot folder.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from gearwright_graphics.animation import apply_animation
from gearwright_graphics.assets import AssetResolver, texture_mapping
from gearwright_graphics.geometry import Triangle, triangles
from gearwright_graphics.materials import load_material, load_material_bytes
from gearwright_graphics.raster import Camera, render


def mesh_triangles(mesh, texture, alpha):
    vertices = np.array(mesh['xyz']).reshape((-1, 3))
    uvs = np.array(mesh['uv']).reshape((-1, 2))
    # GPU/Cairo row zero is at v=0; the diagnostic sampler uses v=1.
    uvs[:, 1] = 1 - uvs[:, 1]
    result = []
    for indices in np.array(mesh['indices']).reshape((-1, 3)):
        points = vertices[indices]
        normal = np.cross(points[1] - points[0], points[2] - points[0])
        length = np.linalg.norm(normal)
        if length < 1e-10:
            continue
        result.append(Triangle(points, uvs[indices], texture, alpha, 0,
                               normal / length, texture, 'stock-controls', 0))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--vintage-story', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    output = root / 'generated/photoshoot/smart-receiver-paper'
    data = json.loads((output / 'paper-mesh.json').read_text())
    shape = json.loads((root / 'assets/gearwright/shapes/block/pneumatic-smart-receiver.json').read_text())

    def prune(elements):
        return [dict(e, children=prune(e.get('children', []))) for e in elements
                if e['name'] != 'stock-paper-web' and not e['name'].startswith(('stock-print-ink-', 'stock-print-history-'))]

    shape['elements'] = prune(shape['elements'])
    resolver = AssetResolver((args.vintage_story, root))
    materials = {key: load_material_bytes(key, resolver.texture(value).read())
                 for key, value in texture_mapping(shape).items()}
    materials['missing'] = load_material('missing', None)
    materials['receipt'] = load_material('receipt', output / 'labels.png')
    views = [('takeup', (1.65, 1.8, .52), (.70, .85, .5), .72),
             ('paper', (1.18, 1.32, -.05), (.73, .85, .5), .78)]
    board = Image.new('RGB', (900 * 3, 540 * 2), '#181b21')
    draw = ImageDraw.Draw(board)
    for index, frame in enumerate(data['frames']):
        body = triangles(apply_animation(shape, 'stock-check', frame['frame']))
        moving = mesh_triangles(frame['grain'], 'scroll-paper', 'opaque') + mesh_triangles(frame['ink'], 'receipt', 'cutout')
        for row, (view, position, target, scale) in enumerate(views):
            camera = Camera(position, target, 900, 500, orthographic=True, orthographic_scale=scale)
            picture = render(body + moving, materials, camera)
            picture.save(output / f'{view}-{frame["frame"]}.png')
            board.paste(picture.convert('RGB'), (index * 900, row * 540 + 32))
            draw.text((index * 900 + 15, row * 540 + 10), f'{view} / feed {frame["feed"]:.3f}', fill='white')
    board.save(output / 'paper-feed.png')
    print('Rendered actual receipt meshes: generated/photoshoot/smart-receiver-paper/paper-feed.png')


if __name__ == '__main__':
    main()
