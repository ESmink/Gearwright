#!/usr/bin/env python3
"""Render Vintage Story JSON shapes as a command-line product photoshoot.

Run this file with normal Python.  It performs asset discovery and then invokes
Blender in background mode.  The Blender-only rendering code deliberately lives
in the same file so the tool is easy to copy into another project.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Iterable, Mapping, Optional, Sequence


DEFAULT_VIEWS = "front,front-right,right,back,back-left,left,top"
BASE_GAME_DOMAIN_FALLBACKS = ("game", "survival", "creative")
VIEW_DIRECTIONS = {
    # Vintage Story north maps to Blender +Y, so "front" sees north faces.
    "front": (0.0, 1.0, 0.28),
    "front-right": (1.0, 1.0, 0.38),
    "right": (1.0, 0.0, 0.28),
    "back-right": (1.0, -1.0, 0.38),
    "back": (0.0, -1.0, 0.28),
    "back-left": (-1.0, -1.0, 0.38),
    "left": (-1.0, 0.0, 0.28),
    "front-left": (-1.0, 1.0, 0.38),
    "top": (0.0, 0.02, 1.0),
    "bottom": (0.0, -0.02, -1.0),
    "isometric": (1.0, 1.0, 0.82),
}


class PhotoshootError(RuntimeError):
    """A user-facing configuration or asset error."""


def _strip_json_comments_and_trailing_commas(text: str) -> str:
    """Return permissive Vintage Story JSON as strict JSON.

    Json.NET accepts comments and trailing commas.  Many mod shapes use them,
    so using json.loads directly would reject otherwise valid game assets.
    """

    output: list[str] = []
    index = 0
    in_string = False
    escaped = False
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if in_string:
            output.append(char)
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            index += 1
            continue
        if char == '"':
            in_string = True
            output.append(char)
            index += 1
            continue
        if char == "/" and next_char == "/":
            index += 2
            while index < len(text) and text[index] not in "\r\n":
                index += 1
            continue
        if char == "/" and next_char == "*":
            index += 2
            while index + 1 < len(text) and text[index : index + 2] != "*/":
                index += 1
            index += 2
            continue
        output.append(char)
        index += 1

    uncommented = "".join(output)
    output = []
    in_string = False
    escaped = False
    index = 0
    while index < len(uncommented):
        char = uncommented[index]
        if in_string:
            output.append(char)
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            index += 1
            continue
        if char == '"':
            in_string = True
            output.append(char)
            index += 1
            continue
        if char == ",":
            lookahead = index + 1
            while lookahead < len(uncommented) and uncommented[lookahead].isspace():
                lookahead += 1
            if lookahead < len(uncommented) and uncommented[lookahead] in "}]":
                index += 1
                continue
        output.append(char)
        index += 1
    return "".join(output)


def load_json_bytes(data: bytes, description: str) -> dict[str, Any]:
    try:
        text = data.decode("utf-8-sig")
        parsed = json.loads(_strip_json_comments_and_trailing_commas(text))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PhotoshootError(f"Could not parse {description}: {exc}") from exc
    if not isinstance(parsed, dict):
        raise PhotoshootError(f"Expected a JSON object in {description}")
    return parsed


def _asset_path(domain: str, category: str, relative: str) -> str:
    relative = relative.replace("\\", "/").lstrip("/")
    category_prefix = f"{category}/"
    if relative.lower().startswith(category_prefix):
        relative = relative[len(category_prefix) :]
    assets_prefix = f"assets/{domain}/{category}/"
    if relative.lower().startswith(assets_prefix.lower()):
        relative = relative[len(assets_prefix) :]
    return f"assets/{domain}/{category}/{relative}"


def _with_extensions(path: str, extensions: Sequence[str]) -> Iterable[str]:
    suffix = PurePosixPath(path).suffix
    if suffix:
        yield path
        return
    for extension in extensions:
        yield f"{path}{extension}"


@dataclass(frozen=True)
class ResolvedAsset:
    logical_path: str
    source_label: str
    file_path: Optional[Path] = None
    archive_path: Optional[Path] = None
    archive_member: Optional[str] = None

    @property
    def description(self) -> str:
        if self.file_path is not None:
            return str(self.file_path)
        return f"{self.archive_path}!/{self.archive_member}"

    def read_bytes(self) -> bytes:
        if self.file_path is not None:
            return self.file_path.read_bytes()
        assert self.archive_path is not None and self.archive_member is not None
        with zipfile.ZipFile(self.archive_path) as archive:
            return archive.read(self.archive_member)

    def materialize(self, directory: Path) -> Path:
        if self.file_path is not None:
            return self.file_path
        assert self.archive_member is not None
        suffix = PurePosixPath(self.archive_member).suffix or ".bin"
        safe_name = re.sub(r"[^A-Za-z0-9_.-]+", "_", self.logical_path)
        destination = directory / f"{safe_name}{suffix}"
        destination.write_bytes(self.read_bytes())
        return destination


class AssetSource:
    label: str

    def resolve(self, logical_path: str) -> Optional[ResolvedAsset]:
        raise NotImplementedError

    def domains(self) -> set[str]:
        raise NotImplementedError


class DirectoryAssetSource(AssetSource):
    def __init__(self, package_root: Path):
        package_root = package_root.resolve()
        if package_root.name.lower() == "assets":
            package_root = package_root.parent
        self.package_root = package_root
        self.assets_root = package_root / "assets"
        if not self.assets_root.is_dir():
            raise PhotoshootError(f"No assets directory found under {package_root}")
        self.label = str(package_root)

    def resolve(self, logical_path: str) -> Optional[ResolvedAsset]:
        relative = logical_path.replace("/", os.sep)
        candidate = (self.package_root / relative).resolve()
        try:
            candidate.relative_to(self.package_root)
        except ValueError:
            return None
        if candidate.is_file():
            return ResolvedAsset(logical_path, self.label, file_path=candidate)
        return None

    def domains(self) -> set[str]:
        return {entry.name.lower() for entry in self.assets_root.iterdir() if entry.is_dir()}


class ZipAssetSource(AssetSource):
    def __init__(self, archive_path: Path):
        self.archive_path = archive_path.resolve()
        self.label = str(self.archive_path)
        try:
            with zipfile.ZipFile(self.archive_path) as archive:
                names = [name for name in archive.namelist() if not name.endswith("/")]
        except (OSError, zipfile.BadZipFile) as exc:
            raise PhotoshootError(f"Could not read mod archive {archive_path}: {exc}") from exc
        self.members: dict[str, tuple[str, str]] = {}
        for name in names:
            normalized = name.replace("\\", "/").lstrip("/")
            marker = normalized.lower().find("assets/")
            if marker < 0:
                continue
            logical = normalized[marker:]
            self.members[logical.lower()] = (logical, name)

    def resolve(self, logical_path: str) -> Optional[ResolvedAsset]:
        match = self.members.get(logical_path.lower())
        if match is None:
            return None
        logical, member = match
        return ResolvedAsset(
            logical,
            self.label,
            archive_path=self.archive_path,
            archive_member=member,
        )

    def domains(self) -> set[str]:
        domains: set[str] = set()
        for logical in self.members:
            parts = PurePosixPath(logical).parts
            if len(parts) >= 2 and parts[0].lower() == "assets":
                domains.add(parts[1].lower())
        return domains


def _source_for_package(path: Path) -> AssetSource:
    if path.is_file() and path.suffix.lower() == ".zip":
        return ZipAssetSource(path)
    return DirectoryAssetSource(path)


def discover_mod_sources(path: Path) -> list[AssetSource]:
    path = path.expanduser().resolve()
    if path.is_file():
        return [_source_for_package(path)]
    if not path.is_dir():
        raise PhotoshootError(f"Mod path does not exist: {path}")
    if (path / "assets").is_dir() or path.name.lower() == "assets":
        return [_source_for_package(path)]

    sources: list[AssetSource] = []
    for child in sorted(path.iterdir(), key=lambda item: item.name.lower()):
        if child.is_dir() and (child / "assets").is_dir():
            sources.append(DirectoryAssetSource(child))
        elif child.is_file() and child.suffix.lower() == ".zip":
            sources.append(ZipAssetSource(child))
    if not sources:
        raise PhotoshootError(
            f"{path} is neither a mod package nor a directory containing folder/zip mods"
        )
    return sources


def infer_package_from_asset_file(path: Path) -> tuple[Optional[Path], Optional[str]]:
    resolved = path.resolve()
    parts = resolved.parts
    lowered = [part.lower() for part in parts]
    try:
        assets_index = max(index for index, part in enumerate(lowered) if part == "assets")
    except ValueError:
        return None, None
    if assets_index + 1 >= len(parts):
        return None, None
    package_root = Path(*parts[:assets_index]) if assets_index else Path(resolved.anchor)
    if not (package_root / "assets").is_dir():
        return None, None
    return package_root, parts[assets_index + 1].lower()


class AssetResolver:
    """Layered Vintage Story assets.  Later sources override earlier ones."""

    def __init__(self, sources: Sequence[AssetSource]):
        self.sources = list(sources)

    def _find(self, logical_paths: Iterable[str]) -> Optional[ResolvedAsset]:
        paths = list(logical_paths)
        for source in reversed(self.sources):
            for logical_path in paths:
                match = source.resolve(logical_path)
                if match is not None:
                    return match
        return None

    def resolve_location(
        self,
        category: str,
        location: str,
        *,
        default_domain: str,
        extensions: Sequence[str],
    ) -> Optional[ResolvedAsset]:
        domain, relative = split_asset_location(location, default_domain)
        domains = BASE_GAME_DOMAIN_FALLBACKS if domain == "game" else (domain,)
        logical_paths = (
            candidate
            for candidate_domain in domains
            for candidate in _with_extensions(
                _asset_path(candidate_domain, category, relative), extensions
            )
        )
        return self._find(logical_paths)

    def resolve_model(self, model: str) -> tuple[ResolvedAsset, str]:
        direct = Path(model).expanduser()
        if direct.is_file():
            package_root, domain = infer_package_from_asset_file(direct)
            logical = direct.name
            if package_root is not None and domain is not None:
                relative = direct.resolve().relative_to(package_root.resolve()).as_posix()
                logical = relative
            return ResolvedAsset(logical, "direct file", file_path=direct.resolve()), domain or "game"

        if _has_explicit_domain(model):
            domain, relative = split_asset_location(model, "game")
            domains = BASE_GAME_DOMAIN_FALLBACKS if domain == "game" else (domain,)
            logical_paths = (
                candidate
                for candidate_domain in domains
                for candidate in _with_extensions(
                    _asset_path(candidate_domain, "shapes", relative), (".json",)
                )
            )
            match = self._find(logical_paths)
            if match is not None:
                return match, domain
            raise PhotoshootError(f"Could not find shape asset {model!r}")

        matches: list[tuple[ResolvedAsset, str]] = []
        domains = sorted({domain for source in self.sources for domain in source.domains()})
        for domain in domains:
            logical = _asset_path(domain, "shapes", model)
            match = self._find(_with_extensions(logical, (".json",)))
            if match is not None:
                matches.append((match, domain))
        unique = {(match.description, domain): (match, domain) for match, domain in matches}
        if len(unique) == 1:
            return next(iter(unique.values()))
        if len(unique) > 1:
            choices = ", ".join(f"{domain}:{model}" for _, domain in unique.values())
            raise PhotoshootError(f"Ambiguous model {model!r}; use a domain: {choices}")
        raise PhotoshootError(f"Could not find shape asset {model!r} in any supplied asset source")


def _has_explicit_domain(location: str) -> bool:
    if re.match(r"^[A-Za-z]:[\\/]", location):
        return False
    return ":" in location


def split_asset_location(location: str, default_domain: str) -> tuple[str, str]:
    value = location.strip().replace("\\", "/")
    if _has_explicit_domain(value):
        domain, relative = value.split(":", 1)
    else:
        domain, relative = default_domain, value
    return domain.lower(), relative.lstrip("/")


def _mapping_value(value: Any) -> Optional[str]:
    if isinstance(value, str):
        return value
    if isinstance(value, Mapping):
        for key in ("base", "Base", "path", "Path"):
            if isinstance(value.get(key), str):
                return value[key]
    return None


def _texture_mapping_from_json(document: Mapping[str, Any]) -> dict[str, str]:
    if isinstance(document.get("textures"), Mapping):
        source = document["textures"]
    elif isinstance(document.get("Textures"), Mapping):
        source = document["Textures"]
    else:
        source = document
    mapping = {
        str(key).lstrip("#"): parsed
        for key, value in source.items()
        if (parsed := _mapping_value(value)) is not None
    }
    singular = _mapping_value(document.get("texture")) or _mapping_value(document.get("Texture"))
    if singular:
        mapping.setdefault("0", singular)
        mapping.setdefault("all", singular)
    return mapping


def _walk_elements(elements: Any) -> Iterable[Mapping[str, Any]]:
    if not isinstance(elements, list):
        return
    for element in elements:
        if not isinstance(element, Mapping):
            continue
        yield element
        yield from _walk_elements(element.get("children") or element.get("Children"))


def _referenced_texture_keys(shape: Mapping[str, Any]) -> set[str]:
    keys: set[str] = set()
    elements = shape.get("elements") or shape.get("Elements") or []
    for element in _walk_elements(elements):
        faces = element.get("faces") or element.get("Faces") or {}
        if not isinstance(faces, Mapping):
            continue
        for face in faces.values():
            if not isinstance(face, Mapping) or face.get("enabled", face.get("Enabled", True)) is False:
                continue
            texture = face.get("texture") or face.get("Texture")
            if isinstance(texture, str):
                keys.add(texture.lstrip("#"))
    return keys


def parse_texture_override(value: str) -> tuple[str, str]:
    if "=" not in value:
        raise argparse.ArgumentTypeError("texture overrides must use NAME=PATH_OR_ASSET")
    name, location = value.split("=", 1)
    if not name.strip() or not location.strip():
        raise argparse.ArgumentTypeError("texture overrides must use NAME=PATH_OR_ASSET")
    return name.strip().lstrip("#"), location.strip()


PIPE_FACES = ("north", "east", "south", "west", "up", "down")
PIPE_FACE_ROTATIONS = {
    "north": (0.0, 0.0, 0.0),
    "east": (0.0, -90.0, 0.0),
    "south": (0.0, 180.0, 0.0),
    "west": (0.0, 90.0, 0.0),
    "up": (90.0, 0.0, 0.0),
    "down": (-90.0, 0.0, 0.0),
}


def parse_pipe_state(value: str) -> dict[str, str]:
    state = {face: "empty" for face in PIPE_FACES}
    allowed = {"empty", "connection", "window", "sprinkler"}
    for assignment in (part.strip() for part in value.split(",") if part.strip()):
        if "=" not in assignment:
            raise argparse.ArgumentTypeError(
                "pipe state entries must use FACE=empty|connection|window|sprinkler"
            )
        face, attachment = (part.strip().lower() for part in assignment.split("=", 1))
        if face not in state or attachment not in allowed:
            raise argparse.ArgumentTypeError(f"invalid pipe state entry {assignment!r}")
        if attachment == "sprinkler" and face != "down":
            raise argparse.ArgumentTypeError("the sprinkler attachment is only valid on the down face")
        state[face] = attachment
    return state


def _append_pipe_part(
    resolver: AssetResolver,
    shape: dict[str, Any],
    model: str,
    name: str,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> None:
    asset, _ = resolver.resolve_model(model)
    part = load_json_bytes(asset.read_bytes(), asset.description)
    elements = part.get("elements") or part.get("Elements")
    if not isinstance(elements, list):
        raise PhotoshootError(f"Pipe part {asset.description} has no elements array")
    target_elements = shape.setdefault("elements", [])
    if not isinstance(target_elements, list):
        raise PhotoshootError("The base pipe shape has an invalid elements array")
    rotation_x, rotation_y, rotation_z = rotation
    target_elements.append(
        {
            "name": name,
            "from": [0, 0, 0],
            "to": [0, 0, 0],
            "rotationOrigin": [8, 8, 8],
            "rotationX": rotation_x,
            "rotationY": rotation_y,
            "rotationZ": rotation_z,
            "children": copy.deepcopy(elements),
        }
    )
    textures = shape.setdefault("textures", {})
    part_textures = part.get("textures") or part.get("Textures") or {}
    if isinstance(textures, dict) and isinstance(part_textures, Mapping):
        textures.update(copy.deepcopy(dict(part_textures)))


def compose_pipe_state(
    resolver: AssetResolver,
    base_shape: dict[str, Any],
    state: Mapping[str, str],
    pressure: float,
) -> dict[str, Any]:
    shape = copy.deepcopy(base_shape)
    for face in PIPE_FACES:
        attachment = state.get(face, "empty")
        rotation = PIPE_FACE_ROTATIONS[face]
        if attachment == "connection":
            _append_pipe_part(
                resolver, shape, "gearwright:block/fluid-pipe-arm",
                f"{face}-connection", rotation,
            )
        elif attachment == "window":
            _append_pipe_part(
                resolver, shape, "gearwright:block/fluid-pipe-window",
                f"{face}-window", rotation,
            )
            if pressure > 0:
                _append_pipe_part(
                    resolver, shape, "gearwright:block/fluid-slug",
                    f"{face}-visible-liquid", rotation,
                )
        elif attachment == "sprinkler":
            _append_pipe_part(
                resolver, shape, "gearwright:block/sprinkler-body",
                "down-sprinkler-body",
            )
            if pressure > 0:
                _append_pipe_part(
                    resolver, shape, "gearwright:block/sprinkler-rotor",
                    "down-sprinkler-rotor", (0.0, 32.0, 0.0),
                )
        else:
            _append_pipe_part(
                resolver, shape, "gearwright:block/fluid-pipe-cap",
                f"{face}-cap", rotation,
            )
    return shape


@dataclass
class PreparedModel:
    shape_asset: ResolvedAsset
    shape: dict[str, Any]
    domain: str
    texture_mapping: dict[str, str]
    textures: dict[str, Optional[ResolvedAsset]]
    warnings: list[str]


def build_resolver(vintage_story: Path, mods: Sequence[Path], model: str) -> AssetResolver:
    vintage_story = vintage_story.expanduser().resolve()
    if not vintage_story.is_dir():
        raise PhotoshootError(f"Vintage Story installation does not exist: {vintage_story}")
    if not (vintage_story / "assets").is_dir():
        raise PhotoshootError(
            f"{vintage_story} does not look like a Vintage Story installation (assets/ is missing)"
        )
    sources: list[AssetSource] = [DirectoryAssetSource(vintage_story)]

    direct_model = Path(model).expanduser()
    if direct_model.is_file():
        package_root, _ = infer_package_from_asset_file(direct_model)
        if package_root is not None and package_root.resolve() != vintage_story:
            sources.append(DirectoryAssetSource(package_root))

    for mod in mods:
        sources.extend(discover_mod_sources(mod))
    return AssetResolver(sources)


def prepare_model(args: argparse.Namespace) -> PreparedModel:
    resolver = build_resolver(args.vintage_story, args.mod, args.model)
    shape_asset, domain = resolver.resolve_model(args.model)
    shape = load_json_bytes(shape_asset.read_bytes(), shape_asset.description)
    if args.pipe_state is not None:
        shape = compose_pipe_state(resolver, shape, args.pipe_state, args.pipe_pressure)
    elements = shape.get("elements") or shape.get("Elements")
    if not isinstance(elements, list):
        raise PhotoshootError(
            f"{shape_asset.description} has no elements array; pass a shape JSON, not a block/item/entity type"
        )

    mapping = _texture_mapping_from_json(shape)
    if args.textures is not None:
        texture_document = load_json_bytes(args.textures.read_bytes(), str(args.textures))
        mapping.update(_texture_mapping_from_json(texture_document))
    mapping.update(dict(args.texture))

    referenced_keys = _referenced_texture_keys(shape)
    textures: dict[str, Optional[ResolvedAsset]] = {}
    warnings: list[str] = []
    for key in sorted(referenced_keys):
        value = mapping.get(key)
        visited = {key}
        while isinstance(value, str) and value.startswith("#"):
            alias = value[1:]
            if alias in visited:
                warnings.append(f"Texture alias cycle involving #{key}")
                value = None
                break
            visited.add(alias)
            value = mapping.get(alias)
        if value is None:
            warnings.append(f"No texture mapping supplied for #{key}")
            textures[key] = None
            continue

        direct = Path(value).expanduser()
        resolved: Optional[ResolvedAsset] = None
        if direct.is_file():
            resolved = ResolvedAsset(direct.name, "direct texture", file_path=direct.resolve())
        else:
            resolved = resolver.resolve_location(
                "textures", value, default_domain=domain, extensions=(".png", ".jpg", ".jpeg")
            )
        if resolved is None:
            warnings.append(f"Could not resolve texture #{key} = {value!r}")
        textures[key] = resolved

    if args.strict_textures and warnings:
        raise PhotoshootError("Texture resolution failed:\n  - " + "\n  - ".join(warnings))
    return PreparedModel(shape_asset, shape, domain, mapping, textures, warnings)


def parse_color(value: str) -> tuple[float, float, float, float]:
    text = value.strip().lstrip("#")
    if len(text) not in (6, 8) or not re.fullmatch(r"[0-9a-fA-F]+", text):
        raise argparse.ArgumentTypeError("colors must be #RRGGBB or #RRGGBBAA")
    if len(text) == 6:
        text += "ff"
    return tuple(int(text[index : index + 2], 16) / 255 for index in range(0, 8, 2))  # type: ignore[return-value]


def parse_views(value: str) -> list[str]:
    views = [view.strip().lower() for view in value.split(",") if view.strip()]
    invalid = [view for view in views if view not in VIEW_DIRECTIONS]
    if not views or invalid:
        choices = ", ".join(VIEW_DIRECTIONS)
        raise argparse.ArgumentTypeError(
            f"unknown view(s): {', '.join(invalid) or '(none)'}; choose from {choices}"
        )
    return views


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Render multi-angle photos of a Vintage Story JSON shape using Blender.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument(
        "--vintage-story",
        "--game",
        required=True,
        type=Path,
        help="Vintage Story installation directory (the directory containing assets/)",
    )
    parser.add_argument(
        "--model",
        required=True,
        help="Shape JSON path or asset location such as survival:item/tool/knife",
    )
    parser.add_argument(
        "--pipe-state",
        type=parse_pipe_state,
        help=(
            "Compose a Gearwright pipe preview from comma-separated face states, "
            "for example north=connection,east=window,down=sprinkler"
        ),
    )
    parser.add_argument(
        "--pipe-pressure",
        type=float,
        default=0,
        help="Preview pressure; values above zero show the rotor and window liquid",
    )
    parser.add_argument(
        "--mod",
        action="append",
        type=Path,
        default=[],
        help="Mod folder, mod zip, or directory containing mods; repeat for multiple locations",
    )
    parser.add_argument(
        "--textures",
        type=Path,
        help="JSON texture map, or block/item/entity JSON containing a textures object",
    )
    parser.add_argument(
        "--texture",
        action="append",
        type=parse_texture_override,
        default=[],
        metavar="NAME=PATH_OR_ASSET",
        help="Override one #texture code; repeat for multiple textures",
    )
    parser.add_argument("--output", type=Path, default=Path("photoshoot"), help="Output directory")
    parser.add_argument(
        "--views",
        type=parse_views,
        default=parse_views(DEFAULT_VIEWS),
        help=f"Comma-separated camera names; available: {', '.join(VIEW_DIRECTIONS)}",
    )
    parser.add_argument("--size", type=int, default=1024, help="Square image size in pixels")
    parser.add_argument("--samples", type=int, default=64, help="Eevee temporal anti-aliasing samples")
    parser.add_argument("--padding", type=float, default=1.22, help="Framing padding multiplier")
    parser.add_argument(
        "--background",
        type=parse_color,
        default=parse_color("#edf0e7"),
        help="Studio background color as #RRGGBB",
    )
    parser.add_argument("--transparent", action="store_true", help="Render a transparent background")
    parser.add_argument("--format", choices=("png", "jpeg"), default="png", help="Output image format")
    parser.add_argument("--linear-textures", action="store_true", help="Smooth texture pixels")
    parser.add_argument("--strict-textures", action="store_true", help="Fail instead of using magenta")
    parser.add_argument("--save-blend", action="store_true", help="Also save the composed Blender scene")
    parser.add_argument("--blender", type=Path, help="Path to Blender; otherwise PATH/common installs are searched")
    parser.add_argument("--dry-run", action="store_true", help="Resolve assets and print a report without Blender")
    parser.add_argument("--inside-blender", action="store_true", help=argparse.SUPPRESS)
    return parser


def find_blender(explicit: Optional[Path]) -> Path:
    if explicit is not None:
        candidate = explicit.expanduser().resolve()
        if not candidate.is_file():
            raise PhotoshootError(f"Blender executable does not exist: {candidate}")
        return candidate

    command = shutil.which("blender")
    if command:
        return Path(command).resolve()

    candidates: list[Path] = []
    if sys.platform == "win32":
        program_files = os.environ.get("ProgramFiles")
        if program_files:
            candidates.extend(Path(program_files).glob("Blender Foundation/Blender */blender.exe"))
    elif sys.platform == "darwin":
        candidates.append(Path("/Applications/Blender.app/Contents/MacOS/Blender"))
    else:
        candidates.extend((Path("/usr/bin/blender"), Path("/usr/local/bin/blender")))
    existing = sorted((path for path in candidates if path.is_file()), reverse=True)
    if existing:
        return existing[0].resolve()
    raise PhotoshootError("Blender was not found. Install Blender or pass --blender /path/to/blender.")


def print_preflight(prepared: PreparedModel, args: argparse.Namespace) -> None:
    print(f"Model: {prepared.shape_asset.description}")
    print(f"Domain: {prepared.domain}")
    print(f"Elements: {sum(1 for _ in _walk_elements(prepared.shape.get('elements') or prepared.shape.get('Elements')))}")
    print("Textures:")
    if not prepared.textures:
        print("  (none referenced)")
    for key, asset in prepared.textures.items():
        print(f"  #{key}: {asset.description if asset else '[missing]'}")
    print(f"Views: {', '.join(args.views)}")
    print(f"Output: {args.output.expanduser().resolve()}")
    if args.pipe_state is not None:
        print("Pipe state: " + ", ".join(f"{face}={args.pipe_state[face]}" for face in PIPE_FACES))
        print(f"Pipe pressure: {args.pipe_pressure:g} PU/s")
    for warning in prepared.warnings:
        print(f"Warning: {warning}", file=sys.stderr)


def _argument_list_for_blender(argv: Sequence[str]) -> list[str]:
    result: list[str] = []
    skip_next = False
    for index, value in enumerate(argv):
        if skip_next:
            skip_next = False
            continue
        if value == "--blender":
            skip_next = True
            continue
        if value.startswith("--blender=") or value == "--dry-run" or value == "--inside-blender":
            continue
        result.append(value)
    result.append("--inside-blender")
    return result


def invoke_blender(args: argparse.Namespace, argv: Sequence[str]) -> int:
    blender = find_blender(args.blender)
    command = [
        str(blender),
        "--background",
        "--factory-startup",
        "--python",
        str(Path(__file__).resolve()),
        "--",
        *_argument_list_for_blender(argv),
    ]
    print(f"Launching Blender: {blender}")
    return subprocess.run(command, check=False).returncode


# ---------------------------------------------------------------------------
# Blender rendering implementation.  No bpy imports occur during normal CLI
# validation or unit tests.


def _blender_render(prepared: PreparedModel, args: argparse.Namespace) -> list[Path]:
    import bpy  # type: ignore
    from mathutils import Euler, Matrix, Vector  # type: ignore

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            datablocks.remove(datablock)

    scene = bpy.context.scene
    scene.render.resolution_x = args.size
    scene.render.resolution_y = args.size
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = args.transparent
    scene.render.image_settings.file_format = "PNG" if args.format == "png" else "JPEG"
    scene.render.image_settings.color_mode = "RGBA" if args.format == "png" else "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.image_settings.compression = 18
    scene.render.engine = "BLENDER_EEVEE_NEXT" if hasattr(scene, "eevee") is False and bpy.app.version >= (4, 2, 0) else "BLENDER_EEVEE"
    if hasattr(scene, "eevee"):
        for setting, value in (
            ("taa_render_samples", args.samples),
            ("use_gtao", True),
            ("gtao_distance", 3),
            ("gtao_factor", 1.2),
            ("use_soft_shadows", True),
        ):
            if hasattr(scene.eevee, setting):
                setattr(scene.eevee, setting, value)

    world = bpy.data.worlds.new("Studio World") if bpy.data.worlds.get("World") is None else bpy.data.worlds["World"]
    scene.world = world
    world.use_nodes = True
    world_nodes = world.node_tree.nodes
    background_node = world_nodes.get("Background")
    background_node.inputs["Color"].default_value = args.background
    background_node.inputs["Strength"].default_value = 0.62

    root = bpy.data.objects.new("Vintage Story Model", None)
    bpy.context.collection.objects.link(root)

    basis = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
    basis_inverse = basis.inverted()
    material_cache: dict[tuple[str, int], Any] = {}

    def make_material(key: str, glow: int, texture_path: Optional[Path]):
        cache_key = (str(texture_path) if texture_path else f"missing:{key}", glow)
        if cache_key in material_cache:
            return material_cache[cache_key]
        material = bpy.data.materials.new(f"VS #{key}" + (f" glow {glow}" if glow else ""))
        material.use_nodes = True
        material.use_backface_culling = True
        material.diffuse_color = (0.95, 0.0, 0.72, 1.0) if texture_path is None else (1, 1, 1, 1)
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        principled = nodes.get("Principled BSDF")
        principled.inputs["Roughness"].default_value = 0.72
        if texture_path is None:
            principled.inputs["Base Color"].default_value = (0.95, 0.0, 0.72, 1.0)
        else:
            image = bpy.data.images.load(str(texture_path), check_existing=True)
            image.colorspace_settings.name = "sRGB"
            texture = nodes.new("ShaderNodeTexImage")
            texture.image = image
            texture.interpolation = "Linear" if args.linear_textures else "Closest"
            links.new(texture.outputs["Color"], principled.inputs["Base Color"])
            if "Alpha" in texture.outputs and "Alpha" in principled.inputs:
                links.new(texture.outputs["Alpha"], principled.inputs["Alpha"])
                if hasattr(material, "surface_render_method"):
                    material.surface_render_method = "DITHERED"
                elif hasattr(material, "blend_method"):
                    material.blend_method = "HASHED"
                if hasattr(material, "use_screen_refraction"):
                    material.use_screen_refraction = True
                if hasattr(material, "show_transparent_back"):
                    material.show_transparent_back = True
        if glow > 0:
            emission_input = principled.inputs.get("Emission Color") or principled.inputs.get("Emission")
            strength_input = principled.inputs.get("Emission Strength")
            if emission_input is not None:
                emission_input.default_value = (1.0, 0.46, 0.18, 1.0)
            if strength_input is not None:
                strength_input.default_value = min(glow / 96.0, 3.0)
        material_cache[cache_key] = material
        return material

    with tempfile.TemporaryDirectory(prefix="vs-photoshoot-") as temporary:
        temporary_path = Path(temporary)
        texture_files = {
            key: asset.materialize(temporary_path) if asset is not None else None
            for key, asset in prepared.textures.items()
        }

        face_vertices = {
            "north": lambda w, h, d: [(0, 0, 0), (0, h, 0), (w, h, 0), (w, 0, 0)],
            "east": lambda w, h, d: [(w, 0, 0), (w, h, 0), (w, h, d), (w, 0, d)],
            "south": lambda w, h, d: [(0, 0, d), (w, 0, d), (w, h, d), (0, h, d)],
            "west": lambda w, h, d: [(0, 0, 0), (0, 0, d), (0, h, d), (0, h, 0)],
            "up": lambda w, h, d: [(0, h, 0), (0, h, d), (w, h, d), (w, h, 0)],
            "down": lambda w, h, d: [(0, 0, 0), (w, 0, 0), (w, 0, d), (0, 0, d)],
        }

        texture_width = float(prepared.shape.get("textureWidth", prepared.shape.get("TextureWidth", 16)) or 16)
        texture_height = float(prepared.shape.get("textureHeight", prepared.shape.get("TextureHeight", 16)) or 16)
        texture_sizes = prepared.shape.get("textureSizes") or prepared.shape.get("TextureSizes") or {}

        def uv_for_face(face_name: str, face: Mapping[str, Any], key: str, dimensions: tuple[float, float, float]):
            w, h, d = dimensions
            default_uv = {
                "north": (0, 0, w, h),
                "east": (0, 0, d, h),
                "south": (0, 0, w, h),
                "west": (0, 0, d, h),
                "up": (0, 0, w, d),
                "down": (0, 0, w, d),
            }[face_name]
            uv = face.get("uv") or face.get("Uv") or default_uv
            if not isinstance(uv, (list, tuple)) or len(uv) != 4:
                uv = default_uv
            size = texture_sizes.get(key) if isinstance(texture_sizes, Mapping) else None
            atlas_width, atlas_height = texture_width, texture_height
            if isinstance(size, (list, tuple)) and len(size) >= 2:
                atlas_width, atlas_height = float(size[0]), float(size[1])
            u0, v0, u1, v1 = (float(number) for number in uv)
            corners = [
                (u0 / atlas_width, 1 - v1 / atlas_height),
                (u1 / atlas_width, 1 - v1 / atlas_height),
                (u1 / atlas_width, 1 - v0 / atlas_height),
                (u0 / atlas_width, 1 - v0 / atlas_height),
            ]
            rotation = int(round(float(face.get("rotation", face.get("Rotation", 0))) / 90.0)) % 4
            if rotation:
                corners = corners[-rotation:] + corners[:-rotation]
            return corners

        element_count = 0

        def add_element(element: Mapping[str, Any], parent: Any) -> None:
            nonlocal element_count
            from_value = element.get("from") or element.get("From") or (0, 0, 0)
            to_value = element.get("to") or element.get("To") or from_value
            if len(from_value) < 3 or len(to_value) < 3:
                return
            start = Vector(tuple(float(value) / 16.0 for value in from_value[:3]))
            end = Vector(tuple(float(value) / 16.0 for value in to_value[:3]))
            dimensions = tuple(end[index] - start[index] for index in range(3))
            faces_data = element.get("faces") or element.get("Faces") or {}

            vertices: list[tuple[float, float, float]] = []
            polygons: list[tuple[int, int, int, int]] = []
            polygon_uvs: list[list[tuple[float, float]]] = []
            polygon_materials: list[Any] = []
            if isinstance(faces_data, Mapping):
                for face_name in ("north", "east", "south", "west", "up", "down"):
                    face = faces_data.get(face_name)
                    if not isinstance(face, Mapping) or face.get("enabled", face.get("Enabled", True)) is False:
                        continue
                    key = str(face.get("texture", face.get("Texture", "missing"))).lstrip("#")
                    local_points = face_vertices[face_name](*dimensions)
                    offset = len(vertices)
                    for x, y, z in local_points:
                        mapped = basis @ Vector((x, y, z, 1))
                        vertices.append((mapped.x, mapped.y, mapped.z))
                    polygons.append((offset, offset + 1, offset + 2, offset + 3))
                    polygon_uvs.append(uv_for_face(face_name, face, key, dimensions))
                    glow = int(face.get("glow", face.get("Glow", 0)) or 0)
                    polygon_materials.append(make_material(key, glow, texture_files.get(key)))

            name = str(element.get("name") or element.get("Name") or f"Element {element_count + 1}")
            object_data = None
            if polygons:
                object_data = bpy.data.meshes.new(f"{name} Mesh")
                object_data.from_pydata(vertices, [], polygons)
                object_data.update()
            object_ = bpy.data.objects.new(name, object_data)
            bpy.context.collection.objects.link(object_)
            object_.parent = parent
            element_count += 1

            if object_data is not None:
                materials: list[Any] = []
                for material in polygon_materials:
                    if material not in materials:
                        materials.append(material)
                        object_data.materials.append(material)
                uv_layer = object_data.uv_layers.new(name="VS UV")
                for polygon_index, polygon in enumerate(object_data.polygons):
                    polygon.material_index = materials.index(polygon_materials[polygon_index])
                    polygon.use_smooth = False
                    for loop_index, uv in zip(polygon.loop_indices, polygon_uvs[polygon_index]):
                        uv_layer.data[loop_index].uv = uv

            origin_value = element.get("rotationOrigin") or element.get("RotationOrigin") or (0, 0, 0)
            origin = Vector(tuple(float(value) / 16.0 for value in origin_value[:3]))
            rotation = Euler(
                tuple(
                    math.radians(float(element.get(key, element.get(key[0].upper() + key[1:], 0)) or 0))
                    for key in ("rotationX", "rotationY", "rotationZ")
                ),
                "XYZ",
            ).to_matrix().to_4x4()
            scale = Matrix.Diagonal(
                Vector(
                    (
                        float(element.get("scaleX", element.get("ScaleX", 1)) or 1),
                        float(element.get("scaleY", element.get("ScaleY", 1)) or 1),
                        float(element.get("scaleZ", element.get("ScaleZ", 1)) or 1),
                        1,
                    )
                )
            )
            local_vs = Matrix.Translation(origin) @ rotation @ scale @ Matrix.Translation(start - origin)
            object_.matrix_local = basis @ local_vs @ basis_inverse

            children = element.get("children") or element.get("Children") or []
            if isinstance(children, list):
                for child in children:
                    if isinstance(child, Mapping):
                        add_element(child, object_)

        elements = prepared.shape.get("elements") or prepared.shape.get("Elements") or []
        for element in elements:
            if isinstance(element, Mapping):
                add_element(element, root)
        if element_count == 0:
            raise PhotoshootError("The shape did not contain any renderable elements")

        bpy.context.view_layer.update()
        mesh_objects = [object_ for object_ in root.children_recursive if object_.type == "MESH"]
        if not mesh_objects:
            raise PhotoshootError("All shape faces are disabled; nothing can be rendered")
        world_points = [object_.matrix_world @ Vector(corner) for object_ in mesh_objects for corner in object_.bound_box]
        minimum = Vector(tuple(min(point[index] for point in world_points) for index in range(3)))
        maximum = Vector(tuple(max(point[index] for point in world_points) for index in range(3)))
        center = (minimum + maximum) * 0.5
        root.location -= Vector((center.x, center.y, minimum.z))
        bpy.context.view_layer.update()
        size = maximum - minimum
        target = Vector((0, 0, max(size.z * 0.48, 0.05)))
        radius = max(size.length * 0.5, 0.25)

        if not args.transparent:
            bpy.ops.mesh.primitive_plane_add(size=max(radius * 12, 12), location=(0, 0, -0.002))
            ground = bpy.context.object
            ground.name = "Studio Ground"
            ground_material = bpy.data.materials.new("Studio Ground Material")
            ground_material.diffuse_color = args.background
            ground_material.use_nodes = True
            ground_material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = args.background
            ground_material.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.88
            ground.data.materials.append(ground_material)

        def area_light(name: str, energy: float, color: tuple[float, float, float], location, scale: float):
            data = bpy.data.lights.new(name, "AREA")
            data.energy = energy
            data.color = color
            data.shape = "DISK"
            data.size = max(radius * scale, 1.0)
            object_ = bpy.data.objects.new(name, data)
            bpy.context.collection.objects.link(object_)
            object_.location = Vector(location)
            object_.rotation_euler = ((target - object_.location).to_track_quat("-Z", "Y")).to_euler()

        light_distance = radius * 4.2
        area_light("Key", 620 * max(radius, 0.5), (1.0, 0.96, 0.88), (light_distance, light_distance, light_distance * 1.35), 2.4)
        area_light("Fill", 360 * max(radius, 0.5), (0.74, 0.88, 1.0), (-light_distance, light_distance * 0.4, light_distance * 0.75), 2.8)
        area_light("Rim", 500 * max(radius, 0.5), (0.82, 0.92, 1.0), (-light_distance * 0.5, -light_distance, light_distance * 1.1), 2.0)

        camera_data = bpy.data.cameras.new("Photoshoot Camera")
        camera = bpy.data.objects.new("Photoshoot Camera", camera_data)
        bpy.context.collection.objects.link(camera)
        scene.camera = camera
        camera_data.lens = 55
        camera_data.clip_start = max(radius / 500, 0.001)
        camera_data.clip_end = max(radius * 50, 100)
        distance = radius / math.sin(camera_data.angle * 0.5) * args.padding

        output = args.output.expanduser().resolve()
        output.mkdir(parents=True, exist_ok=True)
        extension = ".png" if args.format == "png" else ".jpg"
        rendered: list[Path] = []
        for view in args.views:
            direction = Vector(VIEW_DIRECTIONS[view]).normalized()
            camera.location = target + direction * distance
            camera.rotation_euler = ((target - camera.location).to_track_quat("-Z", "Y")).to_euler()
            destination = output / f"{view}{extension}"
            scene.render.filepath = str(destination)
            bpy.ops.render.render(write_still=True)
            rendered.append(destination)
            print(f"Rendered {destination}")

        if args.save_blend:
            blend_path = output / "photoshoot.blend"
            bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
            print(f"Saved {blend_path}")

        manifest = {
            "model": prepared.shape_asset.logical_path,
            "domain": prepared.domain,
            "textures": {
                key: asset.logical_path if asset is not None else None
                for key, asset in prepared.textures.items()
            },
            "views": [path.name for path in rendered],
            "settings": {
                "size": args.size,
                "samples": args.samples,
                "padding": args.padding,
                "transparent": args.transparent,
                "format": args.format,
            },
            "warnings": prepared.warnings,
        }
        if args.pipe_state is not None:
            manifest["pipeState"] = dict(args.pipe_state)
            manifest["pipePressure"] = args.pipe_pressure
        (output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        return rendered


def _argv_after_blender_separator() -> list[str]:
    if "--" in sys.argv:
        return sys.argv[sys.argv.index("--") + 1 :]
    return sys.argv[1:]


def main(argv: Optional[Sequence[str]] = None) -> int:
    raw_argv = list(argv if argv is not None else _argv_after_blender_separator())
    parser = build_parser()
    args = parser.parse_args(raw_argv)
    if args.size < 64:
        parser.error("--size must be at least 64")
    if args.samples < 1:
        parser.error("--samples must be positive")
    if args.padding <= 0:
        parser.error("--padding must be positive")
    if args.pipe_pressure < 0:
        parser.error("--pipe-pressure cannot be negative")
    if args.textures is not None:
        args.textures = args.textures.expanduser().resolve()
        if not args.textures.is_file():
            parser.error(f"--textures file does not exist: {args.textures}")

    try:
        prepared = prepare_model(args)
        print_preflight(prepared, args)
        if args.dry_run:
            return 0
        if args.inside_blender:
            _blender_render(prepared, args)
            return 0
        return invoke_blender(args, raw_argv)
    except PhotoshootError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
