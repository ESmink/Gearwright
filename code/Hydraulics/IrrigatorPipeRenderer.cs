using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Spawns pressure-scaled drops from the three drilled outlets.</summary>
internal sealed class IrrigatorPipeRenderer : IRenderer, IDisposable
{
    private const int ParticleAlpha = 72;
    private readonly BlockEntityIrrigatorPipe pipe;
    private readonly ICoreClientAPI capi;
    private float particleAccumulator;
    private int nextOutlet;

    public IrrigatorPipeRenderer(BlockEntityIrrigatorPipe pipe, ICoreClientAPI capi)
    {
        this.pipe = pipe;
        this.capi = capi;
    }

    public double RenderOrder => .5;
    public int RenderRange => 48;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (pipe.CurrentContentCode == null || pipe.ContentAmountLitres <= 0 ||
            PipeContent.IsSteam(pipe.CurrentContentCode)) return;
        float performance = (float)HydraulicMath.IrrigatorPerformance(pipe.CurrentPressure);
        if (performance <= 0) return;

        particleAccumulator += deltaTime * (18 + 72 * performance);
        int quantity = (int)particleAccumulator;
        if (quantity <= 0) return;
        particleAccumulator -= quantity;

        int color = ColorUtil.ToRgba(ParticleAlpha, 226, 240, 248);
        for (int i = 0; i < quantity; i++)
        {
            int outlet = nextOutlet;
            nextOutlet = (nextOutlet + 1) % 3;
            SpawnDrop(outlet, performance, color);
        }
    }

    private void SpawnDrop(int outlet, float performance, int color)
    {
        Random random = capi.World.Rand;
        Vec3d origin = pipe.Pos.ToVec3d();
        double originX;
        double originZ;
        double targetX;
        double targetZ;
        if (outlet == 0)
        {
            originX = RandomBetween(random, 7.55 / 16.0, 8.45 / 16.0);
            originZ = RandomBetween(random, 7.55 / 16.0, 8.45 / 16.0);
            targetX = RandomBetween(random, .08, .92);
            targetZ = RandomBetween(random, .08, .92);
            origin.Add(originX, 5.96 / 16.0, originZ);
        }
        else
        {
            if (!pipe.AlongX)
            {
                bool west = outlet == 1;
                originX = west ? 6.22 / 16.0 : 9.78 / 16.0;
                originZ = RandomBetween(random, 7.55 / 16.0, 8.45 / 16.0);
                targetX = west
                    ? RandomBetween(random, -.92, -.08)
                    : RandomBetween(random, 1.08, 1.92);
                targetZ = RandomBetween(random, .08, .92);
            }
            else
            {
                bool south = outlet == 1;
                originX = RandomBetween(random, 7.55 / 16.0, 8.45 / 16.0);
                originZ = south ? 9.78 / 16.0 : 6.22 / 16.0;
                targetX = RandomBetween(random, .08, .92);
                targetZ = south
                    ? RandomBetween(random, 1.08, 1.92)
                    : RandomBetween(random, -.92, -.08);
            }
            origin.Add(
                originX,
                RandomBetween(random, 6.2 / 16.0, 7.1 / 16.0),
                originZ);
        }

        float life = .75f + .55f * (float)random.NextDouble();
        Vec3f velocity = new(
            (float)((targetX - originX) / life),
            -.08f - .34f * (float)random.NextDouble(),
            (float)((targetZ - originZ) / life));
        float scale = .09f + .05f * performance;
        capi.World.SpawnParticles(
            1, color, origin, origin, velocity, velocity,
            life, .65f, scale, EnumParticleModel.Cube, null);
    }

    private static double RandomBetween(Random random, double minimum, double maximum) =>
        minimum + (maximum - minimum) * random.NextDouble();

    public void Dispose() { }
}
