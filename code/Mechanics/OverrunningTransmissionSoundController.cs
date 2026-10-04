using System;
using Gearwright.Audio;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>Pawl drops use relative tooth travel, not a fixed ratchet recording rate.</summary>
internal sealed class OverrunningTransmissionSoundController : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly BlockPos position;
    private readonly LocalMachineLoop bearings;
    private readonly LocalMachineContacts pawls, engagement;
    private OverrunningRotorState? previous;
    private bool previousOverrun;
    private int previousHand;

    internal OverrunningTransmissionSoundController(ICoreClientAPI api, BlockPos position)
    {
        this.api = api; this.position = position.Copy();
        bearings = new(api, position, "transmission-bearing");
        pawls = new(api, position, "ratchet-pawl", maximumVolume: .18f);
        engagement = new(api, position, "ratchet-engage", MachineSoundKind.Informational, .27f);
    }

    internal void Update(float seconds, OverrunningRotorState? state, int hand, bool overrun) =>
        UpdateAtDistance(seconds, state, hand, overrun, MachineMotionAudio.Distance(api, position));

    internal void UpdateAtDistance(float seconds, OverrunningRotorState? state, int hand, bool overrun, double distance)
    {
        pawls.UpdateAtDistance(seconds, distance); engagement.UpdateAtDistance(seconds, distance);
        double gain = 0;
        if (state.HasValue && previous.HasValue && hand == previousHand)
        {
            var current = state.Value; var before = previous.Value;
            double inputTravel = MachineMotionAudio.Travel(before.InputAngle, current.InputAngle);
            double outputTravel = MachineMotionAudio.Travel(before.OutputAngle, current.OutputAngle);
            gain = MachineMotionAudio.Gain(Math.Max(MachineMotionAudio.Speed(inputTravel, seconds),
                MachineMotionAudio.Speed(outputTravel, seconds)));
            if (gain > 0 && float.IsFinite(seconds) && seconds > 0 && seconds <= .25f)
            {
                if (overrun && previousOverrun && MachineMotionAudio.CrossedTooth(
                    hand * (before.OutputAngle - before.InputAngle),
                    hand * (outputTravel - inputTravel), OverrunningPawlMath.ToothPitch))
                    pawls.Trigger(.35 + .65 * gain);
                if (overrun != previousOverrun)
                    engagement.Trigger(overrun ? .45 : .85, overrun ? .95f : 1);
            }
        }
        bearings.UpdateAtDistance(seconds, gain * .65, 1, distance);
        previous = state; previousHand = hand; previousOverrun = overrun;
    }

    public void Dispose() { bearings.Dispose(); pawls.Dispose(); engagement.Dispose(); }
}
