#!/usr/bin/env python3
"""Compatibility entry point for the Gearwright CPU photoshoot."""

from __future__ import annotations

import sys
from pathlib import Path

graphics_tools = Path(__file__).resolve().parent / "graphics"
if str(graphics_tools) not in sys.path:
    sys.path.insert(0, str(graphics_tools))

from gearwright_graphics.photoshoot import main  # noqa: E402


if __name__ == "__main__":
    raise SystemExit(main())
