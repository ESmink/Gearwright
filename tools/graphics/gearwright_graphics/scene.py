"""Review scenes, placement, visibility, and deterministic explosion metadata."""

from __future__ import annotations

import copy
import fnmatch
import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Mapping

import numpy as np

from .model import ReviewAssembly, ReviewScene, Vec3


@dataclass
class SceneObject:
    name: str
    asset: str
    shape: dict[str, Any]
    role: str = "target"
    placement: tuple[float, float, float] = (0, 0, 0)
    rotation: tuple[float, float, float] = (0, 0, 0)
    alias: str | None = None
    ghost: float | None = None
    comparison_only: bool = False


@dataclass
class Scene:
    name: str
    objects: list[SceneObject] = field(default_factory=list)
    assemblies: tuple[ReviewAssembly, ...] = ()
    warnings: list[str] = field(default_factory=list)


def load_manifest(root: Path, package_id: str) -> dict[str, Any]:
    path = root / "generated/model-review" / f"{package_id}.json"
    if not path.is_file():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def select_assembly(assemblies: tuple[ReviewAssembly, ...], name: str) -> ReviewAssembly | None:
    return next((assembly for assembly in assemblies if assembly.name == name), None)


def _matches(name: str, patterns: tuple[str, ...]) -> bool:
    return any(fnmatch.fnmatchcase(name, pattern) for pattern in patterns)


def _translate(value: Any, delta: np.ndarray) -> list[float]:
    return [float(value[index]) + float(delta[index]) for index in range(3)]


def _element_names(element: Mapping[str, Any]) -> tuple[str, str | None]:
    return str(element.get("name", element.get("Name", ""))), element.get("group", element.get("Group"))


def apply_explosion(shape: dict[str, Any], assemblies: tuple[ReviewAssembly, ...], amount: float, warnings: list[str]) -> dict[str, Any]:
    if amount == 0:
        return shape
    result = copy.deepcopy(shape)
    authored = {assembly.name: assembly for assembly in assemblies}
    if not authored:
        warnings.append("fallback explosion used; add a named review assembly for deliberate directions")
    def visit(elements: list[dict[str, Any]]) -> None:
        for element in elements:
            name, group = _element_names(element)
            assembly = next((value for value in authored.values() if _matches(name, value.members) or (group and _matches(group, value.members))), None)
            if assembly is not None:
                delta = np.asarray(assembly.vector.values(), dtype=float) * float(assembly.magnitude) * float(amount) * 16.0
                for key in ("from", "to"):
                    if key in element:
                        element[key] = _translate(element[key], delta)
                if "rotationOrigin" in element:
                    element["rotationOrigin"] = _translate(element["rotationOrigin"], delta)
            else:
                # Top-level fallback vectors are based on the element center.
                if not authored and amount:
                    start = np.asarray(element.get("from", (0, 0, 0)), dtype=float)
                    end = np.asarray(element.get("to", start), dtype=float)
                    center = (start + end) * .5
                    delta = np.asarray((center[0], center[1] - 8, center[2]), dtype=float)
                    length = np.linalg.norm(delta) or 1
                    delta = delta / length * amount * 3
                    element["from"] = _translate(element["from"], delta)
                    element["to"] = _translate(element["to"], delta)
                    if "rotationOrigin" in element:
                        element["rotationOrigin"] = _translate(element["rotationOrigin"], delta)
            visit(element.get("children", element.get("Children", [])) or [])
    visit(result.get("elements", result.get("Elements", [])) or [])
    return result


def apply_visibility(shape: dict[str, Any], hidden: tuple[str, ...], only: tuple[str, ...], ghosts: Mapping[str, float]) -> dict[str, Any]:
    result = copy.deepcopy(shape)
    def visit(elements: list[dict[str, Any]]) -> None:
        for element in elements:
            name, group = _element_names(element)
            keys = (name, group or "")
            should_hide = bool(hidden and any(_matches(key, hidden) for key in keys))
            should_hide = should_hide or bool(only and not any(_matches(key, only) for key in keys))
            if should_hide:
                for face in (element.get("faces", {}) or {}).values():
                    if isinstance(face, dict):
                        face["enabled"] = False
            opacity = next((float(value) for pattern, value in ghosts.items() if _matches(name, (pattern,)) or (group and _matches(group, (pattern,)))), None)
            if opacity is not None:
                element["_ghostOpacity"] = max(0.0, min(1.0, opacity))
                element["renderPass"] = max(1, int(element.get("renderPass", 0)))
            visit(element.get("children", element.get("Children", [])) or [])
    visit(result.get("elements", result.get("Elements", [])) or [])
    return result


def transform_matrix(placement: tuple[float, float, float], rotation: tuple[float, float, float]) -> np.ndarray:
    rx, ry, rz = np.radians(rotation)
    mx = np.array(((1, 0, 0), (0, np.cos(rx), -np.sin(rx)), (0, np.sin(rx), np.cos(rx))), dtype=float)
    my = np.array(((np.cos(ry), 0, np.sin(ry)), (0, 1, 0), (-np.sin(ry), 0, np.cos(ry))), dtype=float)
    mz = np.array(((np.cos(rz), -np.sin(rz), 0), (np.sin(rz), np.cos(rz), 0), (0, 0, 1)), dtype=float)
    result = np.eye(4)
    result[:3, :3] = mz @ my @ mx
    result[:3, 3] = placement
    return result
