"""Review-package discovery shared by the desktop model reviewer."""

from __future__ import annotations

import json
import math
import re
from pathlib import Path
from typing import Any


def _humanize(value: str) -> str:
    return value.replace("-", " ").replace("_", " ").title()


def _stage_key(path: Path) -> tuple[int, str]:
    order = {"rest": 0, "middle": 1, "drawn": 2}
    stage = path.name[: -len(".shape.json")]
    return (order.get(stage, 10), stage.lower())


def _inside(root: Path, candidate: Path) -> bool:
    try:
        candidate.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


def _resolve_review_folder(root: Path, requested: Path) -> Path:
    candidate = requested if requested.is_absolute() else root / requested
    candidate = candidate.resolve()
    if not _inside(root, candidate) or not candidate.is_dir():
        raise ValueError(f"review path must be a directory inside the project: {requested}")
    return candidate


def _candidate_dirs(review_root: Path) -> list[Path]:
    direct = sorted(
        (path for path in review_root.iterdir() if path.is_dir() and list(path.glob("*.shape.json"))),
        key=lambda path: path.name.lower(),
    )
    if direct:
        return direct
    return [review_root] if list(review_root.glob("*.shape.json")) else []


def resolve_review_root(root: Path, requested: Path) -> Path:
    candidate = _resolve_review_folder(root, requested)
    if _candidate_dirs(candidate):
        revisions = [
            path
            for path in candidate.iterdir()
            if path.is_dir()
            and re.search(r"revision-(\d+)", path.name, re.IGNORECASE)
            and _candidate_dirs(path)
        ]
        if revisions:
            return max(
                revisions,
                key=lambda path: int(re.search(r"revision-(\d+)", path.name, re.IGNORECASE).group(1)),
            )
        return candidate

    children = [path for path in candidate.iterdir() if path.is_dir() and _candidate_dirs(path)]
    if children:
        def sort_key(path: Path) -> tuple[int, float, str]:
            match = re.search(r"revision-(\d+)", path.name, re.IGNORECASE)
            return (int(match.group(1)) if match else -1, path.stat().st_mtime, path.name.lower())

        return max(children, key=sort_key)
    raise ValueError(f"no candidate shape JSON files found under {requested}")


def _read_json(path: Path) -> dict[str, Any] | None:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    return value if isinstance(value, dict) else None


def _read_descriptions(review_root: Path) -> dict[str, str]:
    readme = review_root / "README.md"
    if not readme.is_file():
        return {}
    descriptions: dict[str, str] = {}
    for line in readme.read_text(encoding="utf-8", errors="replace").splitlines():
        match = re.match(r"^\s*-\s+`?([^`/]+)`?/?\s*[—–-]\s*(.+?)\s*$", line)
        if match:
            descriptions[match.group(1)] = match.group(2)
    return descriptions


def _description_for(candidate: Path, review_root: Path) -> str:
    current = candidate
    while _inside(review_root, current):
        descriptions = _read_descriptions(current)
        if candidate.name in descriptions:
            return descriptions[candidate.name]
        if current == review_root:
            break
        current = current.parent
    return "Generated review candidate."


def _candidate_label(candidate: Path, review_root: Path) -> str:
    relative = candidate.relative_to(review_root)
    return "Generated root" if relative == Path(".") else " / ".join(_humanize(part) for part in relative.parts)


def _all_model_dirs(review_root: Path) -> list[Path]:
    parents: set[Path] = set()
    for shape in review_root.rglob("*.shape.json"):
        relative = shape.relative_to(review_root)
        if not any(part.startswith(".") for part in relative.parts):
            parents.add(shape.parent)
    return sorted(parents, key=lambda path: path.relative_to(review_root).as_posix().lower())


def _model_folder_signature(review_root: Path) -> tuple[tuple[str, int, int], ...]:
    signature: list[tuple[str, int, int]] = []
    for path in [*review_root.rglob("*.shape.json"), *review_root.rglob("README.md")]:
        relative = path.relative_to(review_root)
        if any(part.startswith(".") for part in relative.parts):
            continue
        try:
            stat = path.stat()
        except OSError:
            continue
        signature.append((relative.as_posix(), stat.st_size, stat.st_mtime_ns))
    return tuple(sorted(signature))


def _manifest_for(candidate: Path, stage: str) -> dict[str, Any] | None:
    preferred = candidate / f"fixed-render-{stage}" / "manifest.json"
    if preferred.is_file():
        return _read_json(preferred)
    for path in sorted(candidate.rglob("manifest.json")):
        manifest = _read_json(path)
        if manifest and manifest.get("camera"):
            return manifest
    return None


def _animations(shape: Path) -> list[dict[str, Any]]:
    document = _read_json(shape) or {}
    result: list[dict[str, Any]] = []
    for animation in document.get("animations", []) or []:
        if not isinstance(animation, dict):
            continue
        code = str(animation.get("code") or animation.get("name") or "").strip()
        if not code:
            continue
        try:
            quantity = max(1, int(animation.get("quantityframes", 1)))
        except (TypeError, ValueError):
            quantity = 1
        result.append({"code": code, "label": str(animation.get("name") or code), "frames": quantity})
    return result


def _camera_defaults(review_root: Path, candidates: list[dict[str, Any]]) -> dict[str, Any]:
    for candidate in candidates:
        for stage in candidate["stages"]:
            manifest = _manifest_for(review_root / candidate["id"], stage["id"])
            if not manifest or not manifest.get("camera"):
                continue
            camera = manifest["camera"][0]
            position = [float(value) for value in camera.get("position", (3.5, 2.0, -3.5))]
            target = [float(value) for value in camera.get("target", (0.5, 0.75, 2.25))]
            delta = [position[index] - target[index] for index in range(3)]
            distance = math.sqrt(sum(value * value for value in delta)) or 1.0
            return {
                "azimuth": math.degrees(math.atan2(delta[0], delta[2])),
                "elevation": math.degrees(math.asin(max(-1.0, min(1.0, delta[1] / distance)))),
                "distance": distance,
                "target": target,
                "scale": float(camera.get("orthographicScale", 4.8)),
            }
    return {"azimuth": 28.0, "elevation": 12.0, "distance": 6.5, "target": [0.5, 0.75, 2.25], "scale": 4.8}


class ReviewCatalog:
    """Read-only index of candidate shape files and their review metadata."""

    def __init__(self, root: Path, requested: Path, *, all_models: bool = False) -> None:
        self.root = root.resolve()
        self.all_models = all_models
        self.review_root = (
            _resolve_review_folder(self.root, requested)
            if all_models
            else resolve_review_root(self.root, requested)
        )
        descriptions = _read_descriptions(self.review_root)
        candidates: list[dict[str, Any]] = []
        candidate_paths = _all_model_dirs(self.review_root) if all_models else _candidate_dirs(self.review_root)
        for candidate_path in candidate_paths:
            stages: list[dict[str, Any]] = []
            for shape in sorted(candidate_path.glob("*.shape.json"), key=_stage_key):
                stage = shape.name[: -len(".shape.json")]
                preview = candidate_path / f"{stage}.png"
                stages.append({
                    "id": stage,
                    "label": _humanize(stage),
                    "shape": shape.relative_to(self.root).as_posix(),
                    "preview": preview.relative_to(self.root).as_posix() if preview.is_file() else None,
                    "animations": _animations(shape),
                })
            if stages:
                candidate_id = candidate_path.relative_to(self.review_root).as_posix()
                candidates.append({
                    "id": candidate_id,
                    "label": _candidate_label(candidate_path, self.review_root) if all_models else _humanize(candidate_path.name),
                    "description": descriptions.get(candidate_path.name, _description_for(candidate_path, self.review_root)),
                    "stages": stages,
                })
        if not candidates:
            raise ValueError(f"no candidate shape JSON files found under {self.review_root}")
        self.candidates = candidates
        self.defaults = _camera_defaults(self.review_root, candidates)

    def model_folder_signature(self) -> tuple[tuple[str, int, int], ...]:
        return _model_folder_signature(self.review_root)

    def option_document(self) -> dict[str, Any]:
        first_candidate = self.candidates[0]
        readme = self.review_root / "README.md"
        return {
            "review": {
                "path": self.review_root.relative_to(self.root).as_posix(),
                "title": self.review_root.name.replace("-", " ").title(),
                "readme": readme.read_text(encoding="utf-8", errors="replace") if readme.is_file() else "",
            },
            "candidates": self.candidates,
            "defaults": {
                **self.defaults,
                "candidate": first_candidate["id"],
                "stage": next(
                    (stage["id"] for stage in first_candidate["stages"] if stage["id"] == "rest"),
                    first_candidate["stages"][0]["id"],
                ),
            },
        }

    def shape_path(self, candidate: str, stage: str) -> Path:
        for option in self.candidates:
            if option["id"] != candidate:
                continue
            for state in option["stages"]:
                if state["id"] == stage:
                    path = (self.review_root / option["id"] / f"{state['id']}.shape.json").resolve()
                    if _inside(self.root, path) and path.is_file():
                        return path
        raise ValueError("unknown candidate or state")

    def animation(self, candidate: str, stage: str, code: str) -> dict[str, Any] | None:
        for option in self.candidates:
            if option["id"] != candidate:
                continue
            for state in option["stages"]:
                if state["id"] == stage:
                    return next((item for item in state["animations"] if item["code"] == code), None)
        return None
