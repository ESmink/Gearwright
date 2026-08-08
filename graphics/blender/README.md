# Blender authoring sources

`passive-fluid-pump.blend` is the editable source for the passive fluid pump body, pressure mechanism, and liquid preview. The matching files in `exports/` are direct Vintage Story JSON exports from that scene. The graphics recipes copy those exports and replace their texture names with logical project or game asset references.

Keep the existing `passive-fluid-pump-intake` and `passive-fluid-pump-outlet` collections aligned with the block origin. They are separate runtime shapes because the intake can rotate upward when a top tank is selected and the outlet rotates with block placement.

The `pressure` action is a conventional 30-frame armature animation. Frame 0 is unpressurized and frame 29 is full input pressure. Runtime code scrubs this action from the pump's actual pressure instead of playing it at a fixed speed.

The installed Vintage Story JSON add-on currently needs the Blender 5.2 action compatibility helper before importing or exporting animations. Run `tools/graphics/Enable-BlenderVintageStory52Compatibility.py` inside Blender once per session before using the add-on's animation commands.
