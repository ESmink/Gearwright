"""Restore the legacy Action.fcurves view expected by the Vintage Story JSON add-on in Blender 5.2."""

import bpy
from bpy_extras import anim_utils


def _compatible_fcurves(action):
    if len(action.slots) == 0:
        slot = action.slots.new("OBJECT", action.name)
        channel_bag = anim_utils.action_ensure_channelbag_for_slot(action, slot)
        return channel_bag.fcurves

    channel_bag = anim_utils.action_get_channelbag_for_slot(action, action.slots[0])
    if channel_bag is None:
        channel_bag = anim_utils.action_ensure_channelbag_for_slot(action, action.slots[0])
    return channel_bag.fcurves


if not hasattr(bpy.types.Action, "fcurves"):
    bpy.types.Action.fcurves = property(_compatible_fcurves)

print("Vintage Story JSON animation compatibility enabled for this Blender session.")
