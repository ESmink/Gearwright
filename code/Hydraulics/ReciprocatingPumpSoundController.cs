using System;
using Gearwright.Audio;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Reads the same presentation frame as the piston, liquid and check valves.</summary>
internal sealed class ReciprocatingPumpSoundController : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly BlockPos position;
    private readonly LocalMachineLoop mechanism, water, air;
    private readonly LocalMachineContacts valves;
    private double? previousAngle;
    private ReciprocatingPumpStroke previousStroke;
    private bool previousIntake, previousOutput;

    internal ReciprocatingPumpSoundController(ICoreClientAPI api, BlockPos position)
    {
        this.api = api; this.position = position.Copy();
        mechanism = new(api, position, "pump-mechanism");
        water = new(api, position, "water-pipe");
        air = new(api, position, "airflow");
        valves = new(api, position, "pump-valve", maximumVolume: .22f);
    }

    internal void Update(float seconds, double angle, ReciprocatingPumpStroke stroke,
        bool intake, bool output, bool liquid, double throughput) =>
        UpdateAtDistance(seconds, angle, stroke, intake, output, liquid, throughput,
            MachineMotionAudio.Distance(api, position));

    internal void UpdateAtDistance(float seconds, double angle, ReciprocatingPumpStroke stroke,
        bool intake, bool output, bool liquid, double throughput, double distance)
    {
        valves.UpdateAtDistance(seconds, distance);
        double speed = previousAngle.HasValue
            ? MachineMotionAudio.Speed(MachineMotionAudio.Travel(previousAngle.Value, angle), seconds) : 0;
        double gain = MachineMotionAudio.Gain(speed);
        double travelGain = gain * (.18 + .82 * Math.Abs(Math.Sin(angle)));
        mechanism.UpdateAtDistance(seconds, travelGain, 1, distance);
        // Intake/output throughput comes from the same replicated frame as the
        // visible checks. A dry or blocked chamber never invents flowing water.
        // A paused intake can keep filling, and stored pressure can discharge
        // without piston travel. Only an open displayed wet check plus actual
        // throughput may keep that fluid layer audible.
        double flow = intake || output ? HydraulicMath.AudioFlowIntensity(throughput) * .55 : 0;
        water.UpdateAtDistance(seconds, liquid ? flow : 0, 1, distance);
        air.UpdateAtDistance(seconds, liquid ? 0 : flow * .45, 1, distance);
        if (previousAngle.HasValue && float.IsFinite(seconds) && seconds > 0 && seconds <= .25f)
        {
            bool wetClosure = previousIntake && !intake || previousOutput && !output;
            bool dryClosure = gain > 0 && stroke != ReciprocatingPumpStroke.Stationary &&
                previousStroke != ReciprocatingPumpStroke.Stationary && stroke != previousStroke;
            if (wetClosure || dryClosure)
                valves.Trigger(wetClosure ? .75 : .35, stroke == ReciprocatingPumpStroke.Suction ? .94f : 1.04f);
        }
        previousAngle = double.IsFinite(angle) ? angle : null;
        previousStroke = stroke; previousIntake = intake; previousOutput = output;
    }

    public void Dispose() { mechanism.Dispose(); water.Dispose(); air.Dispose(); valves.Dispose(); }
}
