using System;
using System.Buffers.Binary;

namespace Gearwright.Mechanics;

/// <summary>Transient server presentation; never stored in a world or used to apply torque.</summary>
internal readonly record struct OverrunningDriveState(
    bool Available, bool Engaged, int Handedness, long InputNetworkId, long OutputNetworkId)
{
    internal static OverrunningDriveState Unavailable => new(false, false, 1, 0, 0);

    // Version 1: version, availability/engagement flags, handedness, two little-endian network IDs.
    internal byte[] Encode()
    {
        byte[] data = new byte[19];
        data[0] = 1;
        data[1] = (byte)((Available ? 1 : 0) | (Engaged ? 2 : 0));
        data[2] = (byte)(Handedness < 0 ? 0 : 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(3), InputNetworkId);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(11), OutputNetworkId);
        return data;
    }

    internal static bool TryDecode(byte[] data, out OverrunningDriveState state)
    {
        state = Unavailable;
        if (data == null || data.Length != 19 || data[0] != 1 ||
            data[1] is not (0 or 1 or 3) || data[2] > 1) return false;
        long input = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(3));
        long output = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(11));
        bool available = (data[1] & 1) != 0;
        if (available && input == output) return false;
        state = new(available, (data[1] & 2) != 0, data[2] == 0 ? -1 : 1, input, output);
        return true;
    }
}
