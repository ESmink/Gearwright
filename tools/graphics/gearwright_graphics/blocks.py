"""Conservative block/item JSON state resolution for review scenes."""

from __future__ import annotations

from typing import Any, Mapping


def _state_matches(candidate: Mapping[str, Any], state: Mapping[str, str]) -> bool:
    for key, value in candidate.items():
        if key in ("shape", "shapeByType", "textures", "rotateX", "rotateY", "rotateZ"):
            continue
        if str(state.get(str(key), "")) != str(value):
            return False
    return True


def resolve_shape(document: Mapping[str, Any], state: Mapping[str, str]) -> str | None:
    """Resolve the common 1.22 shape/variant forms without guessing BE meshes."""
    variants = document.get("variant", document.get("variants"))
    if isinstance(variants, list):
        for variant in variants:
            if isinstance(variant, Mapping) and _state_matches(variant, state):
                value = variant.get("shape", variant.get("Shape"))
                if isinstance(value, Mapping):
                    value = value.get("base", value.get("Base"))
                if isinstance(value, str):
                    return value
    shape_by_type = document.get("shapeByType", document.get("ShapeByType"))
    if isinstance(shape_by_type, Mapping):
        for key in (state.get("type"), state.get("variant"), "*"):
            if key in shape_by_type and isinstance(shape_by_type[key], str):
                return shape_by_type[key]
    shape = document.get("shape", document.get("Shape"))
    if isinstance(shape, Mapping):
        shape = shape.get("base", shape.get("Base"))
    if isinstance(shape, str):
        return shape
    return None
