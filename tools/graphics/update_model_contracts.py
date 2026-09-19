"""Update frozen graphics contracts for explicitly named, approved shapes.

Uses the same canonicalizer as the contract tests. Refuses to update unrelated
shapes or introduce a new runtime asset. Review the resulting fixture diff.
"""

import argparse
import json
import re
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
sys.path.insert(0, str(ROOT / 'tests/graphics'))

from gearwright_graphics.compiler import build_package, load_definition
from test_pipeline import _semantic_digest, _walk_elements


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', required=True, help='Existing graphics/models module name')
    parser.add_argument('--shape', required=True, action='append', help='Exact approved runtime asset path')
    args = parser.parse_args()
    definition = (ROOT / 'graphics/models' / (args.model + '.py')).resolve()
    definition.relative_to(ROOT / 'graphics/models')
    outputs = build_package(load_definition(ROOT, definition), ROOT, write=False)
    inventory_path = ROOT / 'tests/graphics/fixtures/model-contracts.json'
    semantics_path = ROOT / 'tests/graphics/fixtures/legacy-model-semantics.json'
    inventory = json.loads(inventory_path.read_text())
    semantics = json.loads(semantics_path.read_text())
    inventory_text = inventory_path.read_text()
    semantics_text = semantics_path.read_text()
    for shape in args.shape:
        if shape not in outputs or shape not in inventory['shapes'] or shape not in semantics['shapes']:
            parser.error(f'{shape} is not an existing contract owned by {args.model}')
        document = json.loads(outputs[shape])
        inventory['shapes'][shape].update(
            elements=sum(1 for _ in _walk_elements(document['elements'])),
            textureWidth=document['textureWidth'], textureHeight=document['textureHeight'])
        semantics['shapes'][shape] = _semantic_digest(document)
        # Replace only these flat entries, preserving unrelated formatting too.
        inventory_text, count = re.subn(r'("' + re.escape(shape) + r'"\s*:\s*)\{[^{}]*\}',
            lambda match: match[1] + json.dumps(inventory['shapes'][shape]), inventory_text, count=1)
        if count != 1:
            parser.error(f'Cannot locate one inventory entry for {shape}')
        semantics_text, count = re.subn(r'("' + re.escape(shape) + r'"\s*:\s*)"[a-f0-9]+"',
            lambda match: match[1] + json.dumps(semantics['shapes'][shape]), semantics_text, count=1)
        if count != 1:
            parser.error(f'Cannot locate one semantic entry for {shape}')
    inventory_path.write_text(inventory_text, encoding='utf-8')
    semantics_path.write_text(semantics_text, encoding='utf-8')
    print(f'Updated contracts for {len(args.shape)} explicitly selected shape(s).')


if __name__ == '__main__':
    main()
