"""Animation validation, interpolation, and pose application."""

from __future__ import annotations

import copy
import math
from collections.abc import Mapping
from typing import Any


def _angle_delta(left: float, right: float) -> float:
    return (right - left + 180.0) % 360.0 - 180.0


def interpolate_property(left: float, right: float, amount: float, shortest: bool = False) -> float:
    if shortest:
        return left + _angle_delta(left, right) * amount
    return left + (right - left) * amount


def _frames(animation: Mapping[str, Any]) -> list[Mapping[str, Any]]:
    return list(animation.get("keyframes") or animation.get("Keyframes") or [])


def evaluate_animation(animation: Mapping[str, Any], frame: float, *, mode: str = "hold") -> dict[str, dict[str, float | bool]]:
    """Evaluate a JSON animation without mutating it.

    Before/after behavior is hold by default.  Loop wraps fractional frames
    into the declared range; this is useful for review-only looping previews.
    """
    quantity = float(animation.get("quantityframes", animation.get("QuantityFrames", 0)))
    if quantity <= 0:
        return {}
    if mode == "loop":
        frame = frame % quantity
    frames = sorted(_frames(animation), key=lambda item: float(item.get("frame", item.get("Frame", 0))))
    if not frames:
        return {}
    def frame_number(item: Mapping[str, Any]) -> float:
        return float(item.get("frame", item.get("Frame", 0)))
    if frame <= frame_number(frames[0]):
        return copy.deepcopy(frames[0].get("elements", frames[0].get("Elements", {})))
    if frame >= frame_number(frames[-1]):
        return copy.deepcopy(frames[-1].get("elements", frames[-1].get("Elements", {})))
    before, after = frames[0], frames[-1]
    for current in frames[1:]:
        if frame_number(current) >= frame:
            after = current
            break
        before = current
    left_frame, right_frame = frame_number(before), frame_number(after)
    amount = (frame - left_frame) / (right_frame - left_frame) if right_frame != left_frame else 0.0
    left_values = before.get("elements", before.get("Elements", {}))
    right_values = after.get("elements", after.get("Elements", {}))
    result: dict[str, dict[str, float | bool]] = {}
    for target in set(left_values) | set(right_values):
        result[target] = {}
        keys = set(left_values.get(target, {})) | set(right_values.get(target, {}))
        for key in keys:
            left = float(left_values.get(target, {}).get(key, 0.0))
            right = float(right_values.get(target, {}).get(key, left))
            shortest = key.startswith("rotation") and bool(
                left_values.get(target, {}).get("rotShortestDistance" + key[-1].upper(), False)
                or right_values.get(target, {}).get("rotShortestDistance" + key[-1].upper(), False)
            )
            if key.startswith("rotShortestDistance"):
                result[target][key] = bool(left_values.get(target, {}).get(key, right_values.get(target, {}).get(key, False)))
            else:
                result[target][key] = interpolate_property(left, right, amount, shortest)
    return result


def apply_animation(shape: Mapping[str, Any], code: str, frame: float, *, mode: str = "hold") -> dict[str, Any]:
    result = copy.deepcopy(dict(shape))
    animations = result.get("animations") or result.get("Animations") or []
    matches = [animation for animation in animations if animation.get("code", animation.get("Code")) == code]
    if len(matches) != 1:
        raise ValueError(f"animation code '{code}' is not unambiguous")
    pose = evaluate_animation(matches[0], frame, mode=mode)
    def visit(elements: list[dict[str, Any]]) -> None:
        for element in elements:
            values = pose.get(str(element.get("name", element.get("Name", ""))), {})
            for key, value in values.items():
                if key.startswith("rotShortestDistance"):
                    continue
                element[key] = value
            visit(element.get("children", element.get("Children", [])) or [])
    visit(result.get("elements", result.get("Elements", [])) or [])
    return result
