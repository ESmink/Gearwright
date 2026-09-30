# Air delivery API

`Gearwright.Air` connects generators to adjacent consumers. Automatic Bellow uses it to deliver to a Pneumatic Accumulator or the vanilla forge adapter. Player instructions are in the GitHub Wiki.

## Generating air

During a server simulation step, call:

```csharp
AirDelivery.Deliver(Api, Pos, new AirFlow(rate, seconds, outletFace));
```

`Rate` measures air units per real second. `Seconds` is the duration covered by this step. `Amount` is their product. One air unit uses the game's bellows delivery scale: a vanilla hand-bellows action sends 0.2 units. These units do not represent litres or pressure. `Direction` points from the generator toward the receiving block.

The generator calculates its rate from the air actually displaced during the step. Shaft speed therefore changes the delivery rate without changing the air in a complete stroke. A reservoir can instead discharge at a fixed rate until empty.

Delivery checks that the source and neighboring destination are loaded, then calls one receiver. Client calls, non-finite values, nonpositive rates and nonpositive durations are rejected. `false` means no delivery occurred. Unreceived air vents; it is not queued for chunk loading or a later connection.

## Receiving air

Implement `IAirReceiver` on a block, block entity, or one of their behaviors:

```csharp
public void ReceiveAir(IWorldAccessor world, BlockPos position, AirFlow flow)
{
    // Rate describes how strongly the generator is blowing during this step.
    // Amount integrates that rate over Seconds for volume-dependent effects.
    receivedThisStep += flow.Amount;
}
```

Deliveries apply to their stated interval only. Add contributions from multiple generators, and clear any per-step accumulator in the consumer's simulation. Do not continue a remembered rate in later steps without new deliveries. A stopped generator produces no new flow.

The software interface takes priority. If none is found, the adapter calls the game's `IBellowsAirReceiver` with `flow.Amount` and `flow.Direction`. This preserves the forge's normal burning check, heat limits and fuel behavior. A consumer implementing both interfaces is called only once.

The API delivers to one adjacent consumer. The pneumatic accumulator stores received air and supplies its tube network separately; neither side may invent supply from a remembered rate.
