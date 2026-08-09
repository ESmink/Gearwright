"""Command line entry point for the graphics compiler and photoshoot."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .compiler import CompileError, build_package, definition_paths, load_definition
from .photoshoot import main as photoshoot_main


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Gearwright Python graphics pipeline")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("validate", "build", "inventory"):
        command = sub.add_parser(name)
        command.add_argument("--root", type=Path)
        command.add_argument("--model", "--definition", dest="selector")
    stale = sub.add_parser("stale")
    stale.add_argument("--root", type=Path)
    stale.add_argument("--model", "--definition", dest="selector")
    shot = sub.add_parser("photoshoot")
    shot.add_argument("args", nargs=argparse.REMAINDER)
    return parser


def _selected(root: Path, selector: str | None):
    paths = definition_paths(root)
    if selector is None:
        return paths
    candidate = Path(selector)
    if not candidate.is_absolute():
        candidate = root / candidate
    if candidate.is_file():
        return [candidate.resolve()]
    return [path for path in paths if path.stem == selector or path.name == selector]


def _compile_selected(root: Path, paths: list[Path]):
    packages = [(path, load_definition(root, path)) for path in paths]
    compiled: dict[str, bytes] = {}
    owners: dict[str, str] = {}
    for path, package in packages:
        for relative, data in build_package(package, root, write=False).items():
            normalized = relative.replace("\\", "/")
            if normalized in compiled:
                raise CompileError(
                    f"duplicate output ownership: {normalized} is declared by "
                    f"{owners[normalized]} and {path.relative_to(root).as_posix()}"
                )
            compiled[normalized] = data
            owners[normalized] = path.relative_to(root).as_posix()
    return packages, compiled, owners


def _unowned_runtime_shapes(root: Path, owners: dict[str, str]) -> list[str]:
    shapes_root = root / "assets/gearwright/shapes"
    return [
        path.relative_to(root).as_posix()
        for path in sorted(shapes_root.rglob("*.json"))
        if path.relative_to(root).as_posix() not in owners
    ] if shapes_root.is_dir() else []


def main(argv: list[str] | None = None) -> int:
    parser = _parser()
    args = parser.parse_args(argv)
    if args.command == "photoshoot":
        return photoshoot_main(args.args)
    root = (args.root or Path(__file__).resolve().parents[3]).resolve()
    try:
        paths = _selected(root, args.selector)
        if not paths:
            raise CompileError(f"no model definition matched '{args.selector}'")
        packages, compiled, owners = _compile_selected(root, paths)
        if args.command == "inventory":
            print(json.dumps({
                "version": 1,
                "packages": [
                    {
                        "definition": path.relative_to(root).as_posix(),
                        "package": package.package_id,
                        "outputs": sorted(relative for relative, owner in owners.items() if owner == path.relative_to(root).as_posix()),
                    }
                    for path, package in packages
                ],
            }, indent=2))
            return 0
        if args.command == "validate":
            if args.selector is None:
                unowned = _unowned_runtime_shapes(root, owners)
                if unowned:
                    raise CompileError("runtime shape(s) have no Python definition owner: " + ", ".join(unowned))
            print(f"Validated {len(packages)} model package(s).")
            return 0
        if args.command == "stale":
            mismatches = []
            for relative, data in compiled.items():
                if not relative.startswith("assets/gearwright/"):
                    continue
                destination = root / relative
                if not destination.is_file() or destination.read_bytes() != data:
                    mismatches.append(relative)
            if args.selector is None:
                mismatches.extend(_unowned_runtime_shapes(root, owners))
            if mismatches:
                raise CompileError("stale generated runtime shape(s): " + ", ".join(mismatches))
            print("Generated runtime shapes are current.")
            return 0
        for _, package in packages:
            build_package(package, root, write=True)
        print(f"Built {len(packages)} model package(s).")
        return 0
    except (CompileError, OSError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
