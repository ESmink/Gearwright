"""Potter's Profile Tool model."""

from gearwright_graphics.model import ModelPackage, Shape


def build() -> ModelPackage:
    package = ModelPackage("pottery_profile_tool")
    shape = Shape("pottery-profile-tool")
    tool = shape.texture("tool", "gearwright:item/pottery-profile-tool")
    shape.box("handle", (7, 1.5, 7), (9, 10, 9), texture=tool, uv=(0, 0, 16, 8))
    shape.box("handle-cap", (6.5, 1, 6.5), (9.5, 3, 9.5), texture=tool, uv=(0, 0, 16, 8))
    shape.box("crossbar", (2, 9, 7), (14, 11, 9), texture=tool, uv=(0, 8, 16, 14))
    shape.box("left-profile-upper", (2, 6, 7), (4, 10, 9), texture=tool, uv=(0, 8, 16, 14))
    shape.box("left-profile-lower", (3.25, 4, 7), (5, 7, 9), texture=tool, uv=(0, 8, 16, 14))
    shape.box("right-profile", (12, 7, 7), (14, 10, 9), texture=tool, uv=(0, 8, 16, 14))
    shape.box("pivot-pin", (7.25, 9.25, 6.5), (8.75, 10.75, 9.5), texture=tool, uv=(0, 14, 16, 16))
    package.shape(shape, "assets/gearwright/shapes/item/pottery-profile-tool.json")
    return package
