"""CPU photoshoot renderer and review-scene orchestration."""

from __future__ import annotations

import argparse
import copy
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping, Sequence

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from .animation import apply_animation
from .assets import AssetError, AssetResolver, permissive_json, texture_mapping
from .blocks import resolve_shape
from .compiler import CompileError, compile_shape, definition_paths, load_definition
from .geometry import bounds, triangles
from .materials import load_material, load_material_bytes
from .raster import Camera, render
from .scene import SceneObject, apply_explosion, apply_visibility, transform_matrix

VIEW_DIRECTIONS = {
    "front": (0, 0, -1), "front-right": (1, 0, -1), "right": (1, 0, 0), "back-right": (1, 0, 1),
    "back": (0, 0, 1), "back-left": (-1, 0, 1), "left": (-1, 0, 0), "front-left": (-1, 0, -1),
    "top": (0, 1, .02), "bottom": (0, -1, -.02), "isometric": (1, .8, -1),
}
LIGHTS = {
    "studio": ((.75, .8, 1), (-.5, .4, .6), (0, -.7, .8)),
    "flat": ((0, 1, 0), (-1, 1, 0), (1, 0, 0)),
    "silhouette": ((0, 0, 1), (0, 0, 1), (0, 0, 1)),
}


class PhotoshootError(RuntimeError):
    pass


def parse_color(value: str) -> tuple[float, float, float, float]:
    value = value.strip().lstrip("#")
    if len(value) == 6:
        value += "ff"
    if len(value) != 8 or not re.fullmatch(r"[0-9a-fA-F]{8}", value):
        raise argparse.ArgumentTypeError("colors must be #RRGGBB or #RRGGBBAA")
    return tuple(int(value[index:index + 2], 16) / 255 for index in range(0, 8, 2))  # type: ignore[return-value]


def parse_size(value: str) -> tuple[int, int]:
    match = re.fullmatch(r"(\d+)x(\d+)", value.lower().strip())
    if not match:
        raise argparse.ArgumentTypeError("size must be WIDTHxHEIGHT")
    width, height = int(match.group(1)), int(match.group(2))
    if width < 32 or height < 32:
        raise argparse.ArgumentTypeError("size must be at least 32x32")
    return width, height


def parse_frames(value: str) -> list[float]:
    result: list[float] = []
    for part in value.split(","):
        part = part.strip()
        if not part:
            continue
        if "-" not in part:
            result.append(float(part)); continue
        range_part, _, step_part = part.partition(":")
        left, right = (float(item) for item in range_part.split("-", 1))
        step = float(step_part) if step_part else 1.0
        if step <= 0:
            raise argparse.ArgumentTypeError("frame range steps must be positive")
        current = left
        while current <= right + 1e-8:
            result.append(round(current, 6)); current += step
    if not result:
        raise argparse.ArgumentTypeError("at least one frame is required")
    return list(dict.fromkeys(result))


def parse_assignment(value: str) -> tuple[str, str]:
    if "=" not in value:
        raise argparse.ArgumentTypeError("values must use NAME=VALUE")
    name, item = value.split("=", 1)
    if not name.strip() or not item.strip():
        raise argparse.ArgumentTypeError("values must use NAME=VALUE")
    return name.strip(), item.strip()


def parse_place(value: str) -> tuple[tuple[float, float, float], str]:
    if "=" not in value:
        raise argparse.ArgumentTypeError("placements must use X,Y,Z=ASSET")
    position, asset = value.split("=", 1)
    try:
        coords = tuple(float(part.strip()) for part in position.split(","))
    except ValueError as exc:
        raise argparse.ArgumentTypeError("placement coordinates must be numeric") from exc
    if len(coords) != 3:
        raise argparse.ArgumentTypeError("placements need three coordinates")
    return coords, asset.strip()


def parse_orbit(value: str) -> tuple[float, float, float, tuple[float, float, float]]:
    parts = [float(item.strip()) for item in value.split(",") if item.strip()]
    if len(parts) not in (3, 6):
        raise argparse.ArgumentTypeError("orbit must be azimuth,elevation,distance[,targetX,targetY,targetZ]")
    target = tuple(parts[3:6]) if len(parts) == 6 else (0.0, 0.0, 0.0)
    return parts[0], parts[1], parts[2], target  # type: ignore[return-value]


def parse_pipe_state(value: str) -> dict[str, str]:
    state = {face: "empty" for face in ("north", "east", "south", "west", "up", "down")}
    allowed = {"empty", "connection", "window", "sprinkler"}
    for assignment in value.split(","):
        if not assignment.strip() or "=" not in assignment:
            continue
        face, attachment = (part.strip().lower() for part in assignment.split("=", 1))
        if face not in state or attachment not in allowed:
            raise argparse.ArgumentTypeError(f"invalid pipe state '{assignment}'")
        if attachment == "sprinkler" and face != "down":
            raise argparse.ArgumentTypeError("sprinkler is only valid on down")
        state[face] = attachment
    return state


def _local_shape(path: str, root: Path) -> tuple[dict[str, Any], str]:
    candidate = Path(path).expanduser()
    if not candidate.is_absolute():
        candidate = root / candidate
    candidate = candidate.resolve()
    if not candidate.is_file():
        raise PhotoshootError(f"shape file does not exist: {path}")
    try:
        logical = "project:" + candidate.relative_to(root).as_posix()
    except ValueError:
        logical = "direct:" + candidate.name
    return permissive_json(candidate.read_bytes(), str(path)), logical


def _resolver(root: Path, vintage_story: Path | None, mods: Sequence[Path], direct: str | None) -> AssetResolver:
    sources: list[Path] = []
    if vintage_story is not None:
        sources.append(vintage_story)
    for mod in mods:
        sources.append(mod)
    sources.append(root)
    if direct:
        candidate = Path(direct).expanduser()
        if candidate.is_file():
            sources.append(candidate.parent.parent.parent.parent if len(candidate.parts) > 3 else candidate.parent)
    return AssetResolver(sources)


def _shape_from_asset(resolver: AssetResolver, logical: str) -> tuple[dict[str, Any], str]:
    asset = resolver.shape(logical)
    return permissive_json(asset.read(), asset.logical), asset.logical


def _build_scene(root: Path, args: argparse.Namespace) -> tuple[list[SceneObject], tuple[Any, ...], dict[str, Any]]:
    resolver = _resolver(root, args.vintage_story, args.mod, args.model)
    objects: list[SceneObject] = []
    assemblies: tuple[Any, ...] = ()
    scene_meta: dict[str, Any] = {}
    if args.scene:
        package_name, scene_name = args.scene.split(":", 1) if ":" in args.scene else (args.scene, "normal")
        definition = next((path for path in definition_paths(root) if path.stem == package_name), None)
        if definition is None:
            raise PhotoshootError(f"review definition not found: {package_name}")
        package = load_definition(root, definition)
        review = package.scenes.get(scene_name)
        if review is None:
            raise PhotoshootError(f"review scene not found: {args.scene}")
        assemblies = review.assemblies
        scene_meta = {"package": package.package_id, "name": review.name}
        for index, spec in enumerate(review.objects):
            asset_name = str(spec["asset"])
            if asset_name.startswith("graphics/"):
                shape, logical = _local_shape(asset_name, root)
            elif asset_name in package.outputs:
                shape = compile_shape(package.outputs[asset_name]); logical = asset_name
            else:
                shape, logical = _shape_from_asset(resolver, asset_name)
            objects.append(SceneObject(str(spec.get("name", f"object-{index}")), logical, shape, str(spec.get("role", "target")), tuple(spec.get("placement", (0, 0, 0))), tuple(spec.get("rotation", (0, 0, 0)))))
    else:
        if not args.model and not args.asset:
            raise PhotoshootError("one of --model, --asset, or --scene is required")
        if args.model:
            if Path(args.model).suffix.lower() == ".json" or Path(args.model).is_file() or (root / args.model).is_file():
                shape, logical = _local_shape(args.model, root)
            else:
                shape, logical = _shape_from_asset(resolver, args.model)
            if args.pipe_state is not None:
                module_path = root / "graphics/models/fluid_pipe.py"
                import importlib.util
                spec = importlib.util.spec_from_file_location("gearwright_fluid_pipe_preview", module_path)
                if spec is None or spec.loader is None:
                    raise PhotoshootError("could not load fluid pipe state composer")
                module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
                shape = module.compose_pipe_shape(args.pipe_state, args.pipe_pressure)
            if args.animation:
                shape = apply_animation(shape, args.animation, getattr(args, "frame", 0) or 0, mode=args.interpolation)
            objects.append(SceneObject("target", logical, shape))
            scene_meta = {"name": "direct"}
    for index, placement in enumerate(args.place):
        position, asset = placement
        shape, logical = _shape_from_asset(resolver, asset) if not (Path(asset).suffix or (root / asset).is_file()) else _local_shape(asset, root)
        objects.append(SceneObject(f"neighbor-{index}", logical, shape, "neighbor", position))
    for index, comparison in enumerate(args.compare):
        if ":" in comparison and not Path(comparison).suffix:
            package_name, scene_name = comparison.split(":", 1)
            definition = next((path for path in definition_paths(root) if path.stem == package_name), None)
            if definition is None:
                raise PhotoshootError(f"comparison definition not found: {comparison}")
            package = load_definition(root, definition)
            review = package.scenes.get(scene_name)
            if review is None:
                raise PhotoshootError(f"comparison scene not found: {comparison}")
            for object_index, spec in enumerate(review.objects):
                asset_name = str(spec["asset"])
                if asset_name in package.outputs:
                    shape, logical = compile_shape(package.outputs[asset_name]), asset_name
                else:
                    shape, logical = _shape_from_asset(resolver, asset_name)
                objects.append(SceneObject(f"comparison-{index}-{object_index}", logical, shape, "comparison", (2.0 + index * 2.0, 0, 0)))
        else:
            shape, logical = _shape_from_asset(resolver, comparison) if not (Path(comparison).suffix or (root / comparison).is_file()) else _local_shape(comparison, root)
            objects.append(SceneObject(f"comparison-{index}", logical, shape, "comparison", (2.0 + index * 2.0, 0, 0)))
    if args.asset:
        asset_category = "itemtypes" if "itemtypes/" in args.asset.replace("\\", "/") else "blocktypes"
        asset = resolver.resolve(args.asset, asset_category, (".json",))
        document, logical = permissive_json(asset.read(), asset.logical), asset.logical
        selected = resolve_shape(document, dict(args.state))
        if selected is None:
            raise PhotoshootError(f"block/item asset has no supported shape for state: {args.asset}")
        if ":" not in selected and ":" in args.asset:
            selected = args.asset.split(":", 1)[0] + ":" + selected
        shape, shape_logical = _shape_from_asset(resolver, selected)
        objects.insert(0, SceneObject("target", shape_logical, shape))
        scene_meta = {"asset": logical, "state": dict(args.state)}
    return objects, assemblies, scene_meta


def _textures_for(objects: Sequence[SceneObject], resolver: AssetResolver, strict: bool, root: Path, overrides: Sequence[tuple[str, str]]) -> tuple[dict[str, Any], list[str], dict[str, str]]:
    materials = {"missing": load_material("missing", None)}
    warnings: list[str] = []
    warned: set[str] = set()
    logicals: dict[str, str] = {}
    override_map = dict(overrides)
    for object_ in objects:
        mapping = texture_mapping(object_.shape)
        mapping.update(override_map)
        for element in object_.shape.get("elements", []) or []:
            stack = [element]
            while stack:
                current = stack.pop()
                for face in (current.get("faces", {}) or {}).values():
                    if not isinstance(face, Mapping) or face.get("enabled", True) is False:
                        continue
                    key = str(face.get("texture", "missing")).lstrip("#")
                    value = mapping.get(key)
                    if value is None:
                        message = f"no texture mapping for #{key}"
                        if message not in warned:
                            warnings.append(message); warned.add(message)
                        continue
                    seen = {key}
                    while value.startswith("#"):
                        alias = value[1:]
                        if alias in seen:
                            message = f"texture alias cycle for #{key}"
                            if message not in warned:
                                warnings.append(message); warned.add(message)
                            value = ""; break
                        seen.add(alias); value = mapping.get(alias, "")
                    if not value:
                        continue
                    if key in materials:
                        continue
                    try:
                        asset = resolver.texture(value) if not Path(value).is_file() else type("Direct", (), {"read": lambda self: Path(value).read_bytes(), "logical": value})()
                        materials[key] = load_material_bytes(key, asset.read())
                        logicals[key] = value if ":" in value else str(value).replace("\\", "/")
                    except (AssetError, OSError) as exc:
                        message = f"could not resolve texture #{key} = {value}: {exc}"
                        if message not in warned:
                            warnings.append(message); warned.add(message)
                stack.extend(current.get("children", []) or [])
    if strict and warnings:
        raise PhotoshootError("texture resolution failed:\n  - " + "\n  - ".join(warnings))
    return materials, warnings, logicals


def _all_triangles(objects: Sequence[SceneObject], assemblies: tuple[Any, ...], args: argparse.Namespace, warnings: list[str]):
    result = []
    for object_ in objects:
        shape = apply_explosion(object_.shape, assemblies, args.explode, warnings)
        shape = apply_visibility(shape, tuple(args.hide), tuple(args.only), dict(args.ghost))
        if args.animation and object_.shape.get("animations"):
            shape = apply_animation(shape, args.animation, args.frame or 0, mode=args.interpolation)
        matrix = transform_matrix(object_.placement, object_.rotation)
        for triangle in triangles(shape):
            triangle.vertices[:] = (matrix[:3, :3] @ triangle.vertices.T).T + matrix[:3, 3]
            result.append(triangle)
    return result


def _camera_for(view: str, tris: Sequence[Any], width: int, height: int, padding: float, args: argparse.Namespace) -> Camera:
    minimum, maximum = bounds(tris)
    target = (minimum + maximum) * .5
    size = maximum - minimum
    radius = max(float(np.linalg.norm(size)) * .5, .25)
    if args.camera_target:
        target = np.asarray(args.camera_target, dtype=float)
    direction = np.asarray(VIEW_DIRECTIONS[view], dtype=float)
    direction /= np.linalg.norm(direction) or 1
    field_of_view = args.fov
    if args.focal_length:
        field_of_view = math.degrees(2 * math.atan(36 / (2 * args.focal_length)))
    distance = radius / math.sin(math.radians(field_of_view) * .5) * args.padding
    position = target + direction * distance
    if args.orbit:
        azimuth, elevation, orbit_distance, orbit_target = args.orbit
        target = np.asarray(orbit_target, dtype=float)
        azimuth, elevation = np.radians((azimuth, elevation))
        direction = np.asarray((math.sin(azimuth) * math.cos(elevation), math.sin(elevation), math.cos(azimuth) * math.cos(elevation)))
        position = target + direction * (orbit_distance if orbit_distance > 0 else distance)
    if args.camera_position:
        position = np.asarray(args.camera_position, dtype=float)
    return Camera(position, target, width, height, field_of_view, args.orthographic, args.orthographic_scale, args.roll)


def _grid_overlay(image: Image.Image) -> Image.Image:
    draw = ImageDraw.Draw(image)
    width, height = image.size
    color = (140, 160, 180, 100)
    for fraction in (.25, .5, .75):
        draw.line((int(width * fraction), 0, int(width * fraction), height), fill=color, width=1)
        draw.line((0, int(height * fraction), width, int(height * fraction)), fill=color, width=1)
    draw.rectangle((1, 1, width - 2, height - 2), outline=(190, 210, 225, 120), width=1)
    return image


def _label(image: Image.Image, tris: Sequence[Any], camera: Camera) -> Image.Image:
    draw = ImageDraw.Draw(image)
    seen: set[str] = set()
    for triangle in tris:
        if triangle.name in seen:
            continue
        seen.add(triangle.name)
        point = np.mean(triangle.vertices, axis=0, keepdims=True)
        from .raster import _project, _camera_points  # local to keep public API small
        camera_point = _camera_points(point, camera)
        if camera_point[0, 2] <= camera.near:
            continue
        screen, _ = _project(camera_point, camera)
        x, y = (int(screen[0, 0]), int(screen[0, 1]))
        draw.line((x, y, x + 12, y - 12), fill=(255, 224, 112, 220), width=1)
        draw.text((x + 14, y - 24), triangle.name, fill=(255, 242, 180, 255), stroke_width=1, stroke_fill=(20, 20, 20, 220))
    return image


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Render Vintage Story shapes with the Gearwright CPU photoshoot renderer.", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    parser.add_argument("--vintage-story", "--game", type=Path, help="Vintage Story directory containing assets/")
    parser.add_argument("--mod", action="append", type=Path, default=[], help="mod directory or zip; repeatable")
    parser.add_argument("--model", help="shape JSON path or logical shape asset")
    parser.add_argument("--asset", help="block/item type JSON asset plus --state values")
    parser.add_argument("--scene", help="named model review scene, for example fluid_pipe:active-window-sprinkler")
    parser.add_argument("--state", action="append", type=parse_assignment, default=[])
    parser.add_argument("--pipe-state", type=parse_pipe_state)
    parser.add_argument("--pipe-pressure", type=float, default=0)
    parser.add_argument("--place", action="append", type=parse_place, default=[])
    parser.add_argument("--compare", action="append", default=[], help="place a scene or shape variant beside the target")
    parser.add_argument("--views", "--view", type=lambda value: [item.strip().lower() for item in value.split(",") if item.strip()], default=["front-right"])
    parser.add_argument("--size", type=parse_size, default=(512, 512))
    parser.add_argument("--output", type=Path, default=Path("generated/photoshoot"))
    parser.add_argument("--format", choices=("png", "jpg", "jpeg"), default="png")
    parser.add_argument("--background", type=parse_color, default=(.08, .09, .11, 1))
    parser.add_argument("--transparent", action="store_true")
    parser.add_argument("--ground", action="store_true")
    parser.add_argument("--strict-textures", action="store_true")
    parser.add_argument("--texture", action="append", type=parse_assignment, default=[])
    parser.add_argument("--animation")
    parser.add_argument("--frames", type=parse_frames, default=[0.0])
    parser.add_argument("--progress", type=float)
    parser.add_argument("--interpolation", choices=("hold", "loop"), default="hold")
    parser.add_argument("--contact-sheet", action="store_true")
    parser.add_argument("--explode", type=float, default=0)
    parser.add_argument("--hide", action="append", default=[])
    parser.add_argument("--only", action="append", default=[])
    parser.add_argument("--ghost", action="append", type=parse_assignment, default=[])
    parser.add_argument("--labels", action="store_true")
    parser.add_argument("--grid", action="store_true")
    parser.add_argument("--fov", type=float, default=42)
    parser.add_argument("--focal-length", type=float)
    parser.add_argument("--orthographic", action="store_true")
    parser.add_argument("--orthographic-scale", type=float, default=2.5)
    parser.add_argument("--camera-position", type=lambda value: tuple(float(item) for item in value.split(",")))
    parser.add_argument("--camera-target", type=lambda value: tuple(float(item) for item in value.split(",")))
    parser.add_argument("--orbit", type=parse_orbit, help="azimuth,elevation,distance[,targetX,targetY,targetZ]")
    parser.add_argument("--roll", type=float, default=0)
    parser.add_argument("--padding", type=float, default=1.25)
    parser.add_argument("--framing", choices=("target", "all", "selected"), default="all")
    parser.add_argument("--lighting", choices=tuple(LIGHTS), default="studio")
    parser.add_argument("--light-direction", type=lambda value: tuple(float(item) for item in value.split(",")))
    parser.add_argument("--linear-textures", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    return parser


def run(args: argparse.Namespace, root: Path) -> list[Path]:
    if args.pipe_pressure < 0 or args.explode < 0:
        raise PhotoshootError("pressure and explosion amount cannot be negative")
    if args.transparent and args.format != "png":
        raise PhotoshootError("transparent output requires --format png")
    if args.progress is not None:
        if not 0 <= args.progress <= 1:
            raise PhotoshootError("progress must be between 0 and 1")
        args.frames = [args.progress * 29]
    if args.animation and len(args.frames) > 1 and args.scene and ":" in args.scene:
        pass
    for view in args.views:
        if view not in VIEW_DIRECTIONS:
            raise PhotoshootError(f"unknown view '{view}'")
    objects, assemblies, scene_meta = _build_scene(root, args)
    if args.animation:
        animated = sum(bool(object_.shape.get("animations")) for object_ in objects)
        if animated > 1 and not args.model:
            raise PhotoshootError("--animation needs an unambiguous target object")
    warnings: list[str] = []
    # Compose all selected frames in a single call loop so geometry and manifests stay aligned.
    output_dir = args.output if args.output.is_absolute() else root / args.output
    output_dir.mkdir(parents=True, exist_ok=True)
    rendered: list[Path] = []
    frame_values = args.frames if args.animation else [None]
    for frame in frame_values:
        args.frame = frame
        tris = _all_triangles(objects, assemblies, args, warnings)
        if not tris:
            raise PhotoshootError("scene contains no renderable faces")
        resolver = _resolver(root, args.vintage_story, args.mod, args.model)
        materials, texture_warnings, texture_logicals = _textures_for(objects, resolver, args.strict_textures, root, args.texture)
        warnings.extend(texture_warnings)
        width, height = args.size
        key, fill, rim = LIGHTS[args.lighting]
        if args.light_direction:
            key = args.light_direction
        camera_entries: list[dict[str, Any]] = []
        for view in args.views:
            camera = _camera_for(view, tris, width, height, args.padding, args)
            image = render(tris, materials, camera, background=(0, 0, 0, 0) if args.transparent else args.background, linear=args.linear_textures, key=key, fill=fill, rim=rim, ground=args.ground)
            if args.grid:
                image = _grid_overlay(image)
            if args.labels:
                image = _label(image, tris, camera)
            suffix = f"-{args.animation}-{frame:g}" if args.animation and frame is not None else ""
            destination = output_dir / f"{view}{suffix}.{args.format}"
            converted = image.convert("RGBA" if args.format == "png" else "RGB")
            if args.format == "png":
                converted.save(destination)
            else:
                converted.save(destination, quality=95)
            rendered.append(destination)
            camera_entries.append({"view": view, "position": [round(float(value), 6) for value in camera.position], "target": [round(float(value), 6) for value in camera.target], "fov": camera.fov, "orthographic": camera.orthographic, "orthographicScale": camera.orthographic_scale, "roll": camera.roll})
    manifest = {
        "version": 1, "scene": scene_meta, "objects": [{"name": object_.name, "asset": object_.asset, "role": object_.role, "placement": list(object_.placement), "rotation": list(object_.rotation)} for object_ in objects],
        "views": [path.name for path in rendered], "textures": texture_logicals, "warnings": warnings,
        "camera": camera_entries, "framing": args.framing, "lighting": {"preset": args.lighting, "key": list(key), "fill": list(fill), "rim": list(rim)},
        "explosion": {"amount": args.explode, "assemblies": [{"name": assembly.name, "vector": list(assembly.vector.values()), "magnitude": assembly.magnitude} for assembly in assemblies]}, "visibility": {"hide": args.hide, "only": args.only, "ghost": dict(args.ghost), "labels": args.labels},
        "animation": {"code": args.animation, "frames": args.frames if args.animation else [], "progress": args.progress, "interpolation": args.interpolation},
    }
    (output_dir / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if args.contact_sheet and rendered:
        images = [Image.open(path).convert("RGBA") for path in rendered]
        cell_width, cell_height = images[0].size
        columns = min(4, len(images)); rows = math.ceil(len(images) / columns)
        sheet = Image.new("RGBA", (cell_width * columns, cell_height * rows), (24, 24, 28, 255))
        for index, image in enumerate(images):
            sheet.paste(image, ((index % columns) * cell_width, (index // columns) * cell_height))
        sheet.save(output_dir / "contact-sheet.png")
    return rendered


def main(argv: Sequence[str] | None = None, *, root: Path | None = None) -> int:
    root = (root or Path(__file__).resolve().parents[3]).resolve()
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        if args.dry_run:
            _build_scene(root, args); return 0
        run(args, root)
        return 0
    except (PhotoshootError, AssetError, CompileError, ValueError, OSError) as exc:
        print(f"error: {exc}", file=__import__("sys").stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
