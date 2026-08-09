"""Logical asset resolution shared by compiler review and photoshoot."""

from __future__ import annotations

import json
import re
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Iterable, Mapping


class AssetError(RuntimeError):
    pass


def permissive_json(data: bytes, description: str = "JSON") -> dict[str, Any]:
    text = data.decode("utf-8-sig")
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n\r]*", "", text)
    text = re.sub(r"(?m)([,{]\s*)([A-Za-z_][A-Za-z0-9_-]*)(\s*:)", r'\1"\2"\3', text)
    text = re.sub(r",\s*([}\]])", r"\1", text)
    value = json.loads(text)
    if not isinstance(value, dict):
        raise AssetError(f"{description} must contain an object")
    return value


@dataclass(frozen=True)
class Asset:
    logical: str
    source: str
    file: Path | None = None
    archive: Path | None = None
    member: str | None = None

    def read(self) -> bytes:
        if self.file is not None:
            return self.file.read_bytes()
        if self.archive is not None and self.member is not None:
            with zipfile.ZipFile(self.archive) as handle:
                return handle.read(self.member)
        raise AssetError(f"asset has no readable source: {self.logical}")


class AssetResolver:
    def __init__(self, sources: Iterable[Path]) -> None:
        self.sources = tuple(Path(source).expanduser().resolve() for source in sources)

    def _candidates(self, logical: str, category: str, extensions: tuple[str, ...]) -> Iterable[tuple[str, str]]:
        domain, relative = split_location(logical, "gearwright")
        relative = relative.lstrip("/")
        if relative.startswith("assets/"):
            relative = relative[len("assets/"):]
            if relative.startswith(domain + "/"):
                relative = relative[len(domain) + 1:]
        if relative.startswith("textures/") or relative.startswith("shapes/") or relative.startswith("blocktypes/") or relative.startswith("itemtypes/"):
            relative = relative.split("/", 1)[1]
        for suffix in extensions:
            path = relative if PurePosixPath(relative).suffix else relative + suffix
            yield domain, f"assets/{domain}/{category}/{path}"

    def resolve(self, logical: str, category: str, extensions: tuple[str, ...] = ("",)) -> Asset:
        value = logical.strip().replace("\\", "/")
        if value.startswith("project:"):
            value = value[8:]
            root = self.sources[-1] if self.sources else Path.cwd()
            candidate = (root / value).resolve()
            if candidate.is_file():
                return Asset(logical, "project", file=candidate)
        for domain, relative in self._candidates(value, category, extensions):
            domains = ("game", "survival", "creative") if domain == "game" else (domain,)
            for candidate_domain in domains:
                candidate_relative = relative.replace(f"assets/{domain}/", f"assets/{candidate_domain}/", 1)
                for source in self.sources:
                    if source.is_dir():
                        candidate = source / candidate_relative
                        if candidate.is_file():
                            logical_relative = candidate_relative[len("assets/" + candidate_domain + "/"):]
                            return Asset(f"{domain}:{logical_relative}", str(source), file=candidate)
                    elif source.is_file() and source.suffix.lower() == ".zip":
                        with zipfile.ZipFile(source) as archive:
                            names = {name.replace("\\", "/"): name for name in archive.namelist()}
                            if candidate_relative in names:
                                logical_relative = candidate_relative[len("assets/" + candidate_domain + "/"):]
                                return Asset(f"{domain}:{logical_relative}", str(source), archive=source, member=names[candidate_relative])
        raise AssetError(f"could not resolve {category} asset '{logical}'")

    def shape(self, logical: str) -> Asset:
        return self.resolve(logical, "shapes", (".json",))

    def texture(self, logical: str) -> Asset:
        return self.resolve(logical, "textures", (".png", ".jpg", ".jpeg"))


def split_location(value: str, default_domain: str) -> tuple[str, str]:
    value = value.strip().replace("\\", "/")
    if re.match(r"^[A-Za-z]:/", value):
        return default_domain, value
    if ":" in value:
        domain, relative = value.split(":", 1)
        return domain.lower(), relative.lstrip("/")
    return default_domain, value.lstrip("/")


def texture_mapping(shape: Mapping[str, Any]) -> dict[str, str]:
    source = shape.get("textures", shape.get("Textures", {}))
    result: dict[str, str] = {}
    if isinstance(source, Mapping):
        for key, value in source.items():
            if isinstance(value, str):
                result[str(key).lstrip("#")] = value
            elif isinstance(value, Mapping):
                for field in ("base", "Base", "path", "Path"):
                    if isinstance(value.get(field), str):
                        result[str(key).lstrip("#")] = value[field]
                        break
    return result
