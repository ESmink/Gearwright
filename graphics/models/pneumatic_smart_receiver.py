"""D3 approved for in-game use, followed by the requested paper-feed refinement."""
from gearwright_graphics.model import ModelPackage
from graphics.models.pneumatic_transport import hardware
from graphics.models.parts.jonas_printer import Controls, TEXTURES, request_printer, tube_saddle

APPROVAL = {
    "candidate": "d3-request-printer",
    "sha256": "c6dd6c9bc3a14d6356a312209a2e07abb4bb6fd6368b9d0cbedf528b331b2ccc",
    "reference": "generated/pneumatic-smart-receiver-review/current/d3-request-printer/assembly.shape.json",
    "direction": "Approved for runtime; thicken the lid linkage and show each order feeding onto the paper.",
}

def smart_receiver(terminal=False):
    shape = hardware("receiver-terminal" if terminal else "receiver")
    for key, (location, size) in TEXTURES.items():
        shape.texture(key, location, size=size)
    request_printer(Controls(shape), tube_saddle)
    shape.id = "pneumatic-smart-receiver" + ("-terminal" if terminal else "")
    return shape

def build():
    package = ModelPackage("pneumatic_smart_receiver")
    for terminal in (False, True):
        shape = smart_receiver(terminal)
        package.shape(shape, f"assets/gearwright/shapes/block/{shape.id}.json")
    return package
