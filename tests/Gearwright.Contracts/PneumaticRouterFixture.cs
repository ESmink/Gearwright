using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gearwright.Pneumatics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;
using Rig = Gearwright.Contracts.PneumaticTransportFixture.Rig;

namespace Gearwright.Contracts;

internal static class PneumaticRouterFixture
{
    private static PneumaticFilterRule Rule(string kind, string value = "", bool exclude = false) => new() { Kind = kind, Value = value, Exclude = exclude };
    private static BlockEntityPneumaticTransport Replace(Rig r, int x = 2)
    {
        var old = r.Hosts.Single(h => h.Pos.X == x);
        r.System.Unregister(old); r.Hosts.Remove(old); r.Entities.Remove(old.Pos);
        return r.Host(x, 2, "router");
    }
    private static int Delivered(Rig r) => r.Target.Inventory.Sum(s => s.StackSize);
    private static void Warm(Rig r) { for (int i = 0; i < 250; i++) r.Step(); }
    private static int Finish(Rig r, int count, bool reload = false)
    {
        int tick = 0;
        while (Delivered(r) < count && tick < 2000) { r.Step(); if (reload && tick % 11 == 0) r.Reload(); tick++; }
        return tick;
    }
    internal static void Run(Action<bool, string> check)
    {
        Predicates(check); Expressions(check); Persistence(check); Air(check); Motion(check); Presentation(check); Runtime(check); RoundRobin(check); Recovery(check); Specks(check);
    }
    private static void Predicates(Action<bool, string> check)
    {
        using var r = new Rig();
        var stack = new ItemStack(r.Item, 4);
        check(PneumaticRouterFilters.Match(Rule("identity", "game:stone-granite"), stack) &&
            PneumaticRouterFilters.Match(Rule("mod", "game"), stack) && PneumaticRouterFilters.Match(Rule("item"), stack) &&
            !PneumaticRouterFilters.Match(Rule("block"), stack) && PneumaticRouterFilters.Match(Rule("stackable"), stack),
            "Router identity, same-mod, item/block and stackability filters inspect actual collectibles");
        var block = new Block { Code = new("example:tile-copper"), BlockMaterial = EnumBlockMaterial.Metal };
        block.Variant = new(new Dictionary<string, string> { ["metal"] = "copper" });
        var blocks = new ItemStack(block);
        check(PneumaticRouterFilters.Match(Rule("block"), blocks) &&
            PneumaticRouterFilters.Match(Rule("material", "block:metal"), blocks) &&
            PneumaticRouterFilters.Match(Rule("material", "metal:copper"), blocks), "Material filters include both block class and named material variants");
        var tool = new Item { Code = new("game:pickaxe-iron"), Tool = EnumTool.Pickaxe, Durability = 100, MaxStackSize = 1 };
        Set(tool, "api", r.Api);
        var tools = new ItemStack(tool); tools.Attributes.SetInt("durability", 30);
        r.Source.Inventory[0].Itemstack = tools;
        var samples = PneumaticRouterFilters.Samples(new[] { r.Source.Inventory }); samples[0].Attributes.SetInt("durability", 50);
        check(r.Source.Inventory[0].Itemstack!.Attributes.GetInt("durability") == 30 && r.Source.Inventory[0].StackSize == 1 &&
            PneumaticRouterFilters.SampleValue("mod", tools) == "game" && PneumaticRouterFilters.SampleValue("durability", tools) == "30",
            "Inventory sample picker reads bounded copies without consuming or editing the player's actual stacks");
        check(PneumaticRouterFilters.Match(Rule("tool", "pickaxe"), tools) && PneumaticRouterFilters.Match(Rule("durability", "30"), tools) &&
            !PneumaticRouterFilters.Match(Rule("durability", "31"), tools) && PneumaticInventory.Supported(tools),
            "Tool type and minimum remaining durability filters accept native durable cargo");
        var food = new Item { Code = new("test:food"), NutritionProps = new(), CombustibleProps = new() { BurnDuration = 10, SmeltedStack = new() } };
        var foods = new ItemStack(food); foods.Attributes.SetString("batch", "one");
        check(PneumaticRouterFilters.Match(Rule("food"), foods) && PneumaticRouterFilters.Match(Rule("fuel"), foods) &&
            PneumaticRouterFilters.Match(Rule("smeltable"), foods) && PneumaticRouterFilters.Match(Rule("attribute", "batch"), foods),
            "Nutrition, fuel, smelting and attribute filters use collectible and stack metadata");
        var port = new PneumaticPortSettings { MatchAny = true, Rules = new[] { Rule("item"), Rule("mod", "other"), Rule("identity", r.Item.Code.ToString(), true) } };
        check(!PneumaticRouterFilters.Match(port, stack), "Exclude rules veto even when match-any includes another matching rule");
        port.Rules = new[] { Rule("item"), Rule("mod", "other") };
        check(PneumaticRouterFilters.Match(port, stack), "Match-any combines positive rules"); port.MatchAny = false;
        check(!PneumaticRouterFilters.Match(port, stack), "Match-all combines positive rules");
        port.Rules = new[] { Rule("mod", "other", true) };
        check(PneumaticRouterFilters.Match(port, stack), "An exclusion-only port permits everything outside its exclusions");
        check(PneumaticRouterFilters.Wildcard("game:*gran?te", "game:stone-granite") && !PneumaticRouterFilters.Wildcard("game:*copper", "game:stone-granite"),
            "Code patterns support bounded star and question-mark wildcards");
        var router = new PneumaticRouterState(); router.Configuration.Ports[1].SortingPriority = 3;
        check(router.Allows(stack, 1, 2, new[] { 2, 3 }, true) && router.Allows(stack, 1, 3, new[] { 2, 3 }, true),
            "Committed cargo may keep a lower-priority route to its intended receiver");
        router.Configuration.Ports[1].Rules = new[] { Rule("mod", "other") };
        check(router.Allows(stack, 1, 3, new[] { 2, 3 }, true) && !router.Allows(stack, 1, 2, new[] { 2, 3 }, true), "Only output filters veto actual cargo");
        foreach (int priority in new[] { 0, 1, 2, 3, 4 })
        { router.Configuration.Ports[1].SortingPriority = priority; check(router.Configuration.Valid == (priority >= 1 && priority <= 3), "Output priority accepts exactly levels 1–3"); }
    }
    private static byte[] Bytes(IAttribute value) { using var s = new MemoryStream(); using var w = new BinaryWriter(s); value.ToBytes(w); return s.ToArray(); }
    private static void Expressions(Action<bool, string> check)
    {
        bool precedence = true, grouped = true;
        foreach (int bits in Enumerable.Range(0, 8))
        {
            bool a = (bits & 1) != 0, b = (bits & 2) != 0, c = (bits & 4) != 0;
            bool Value(string name) => name switch { "a" => a, "b" => b, "c" => c, _ => false };
            precedence &= PneumaticFilterExpression.Parse("a OR b AND c")!.Match(Value) == (a || b && c) &&
                PneumaticFilterExpression.Parse("a AND b OR c")!.Match(Value) == (a && b || c);
            grouped &= PneumaticFilterExpression.Parse("(a OR b) AND c")!.Match(Value) == ((a || b) && c) &&
                PneumaticFilterExpression.Parse("a AND (b OR c)")!.Match(Value) == (a && (b || c));
        }
        check(precedence && grouped, "All truth-table combinations evaluate AND before OR and allow optional parentheses to override it");
        foreach (string invalid in new[] { "", " ", "()", "a b", "a OR", "AND a", "a AND OR b", "(a OR b", "a)", "a \"\"", "a & b", "\"unterminated", "\"bad\\q\"" })
            check(PneumaticFilterExpression.Parse(invalid) == null, "Malformed Boolean value list is rejected: " + invalid);
        check(PneumaticFilterExpression.Parse("a and(b or c)")!.Match(t => t is "a" or "c") &&
            PneumaticFilterExpression.Parse("\"AND\" OR \"batch OR version (old)\"")!.Match(t => t == "batch OR version (old)"),
            "Operators are case-insensitive and quoted values preserve spaces, keywords and parentheses");
        check(PneumaticFilterExpression.Parse(string.Join(" OR ", Enumerable.Repeat("a", 64))) != null &&
            PneumaticFilterExpression.Parse(string.Join(" OR ", Enumerable.Repeat("a", 65))) == null &&
            PneumaticFilterExpression.Parse(new string('(', 17) + "a" + new string(')', 17)) == null &&
            PneumaticFilterExpression.Parse(new string('a', 1025)) == null, "Value expression lengths, term counts and nesting stay bounded");
        using var r = new Rig(); var block = new Block { Code = new("game:tile-copper"), BlockMaterial = EnumBlockMaterial.Metal };
        block.Variant = new(new Dictionary<string, string> { ["metal"] = "copper" });
        var stack = new ItemStack(block);
        string picked = PneumaticRouterFilters.SampleExpression("material", stack);
        var rule = Rule("material"); rule.ValueExpression = picked;
        check(picked == "(block:metal OR metal:copper)" && PneumaticRouterFilters.Match(rule, stack),
            "The material picker defaults to a parenthesized OR list of every tag on its sample");
        rule.ValueExpression = "block:metal AND metal:copper";
        check(PneumaticRouterFilters.Match(rule, stack), "Material AND lists require both tags on the same actual stack");
        rule.ValueExpression = "(block:metal OR metal:copper) AND metal:tin";
        check(!PneumaticRouterFilters.Match(rule, stack), "Changing a cached expression immediately changes its actual predicate");
        rule.ValueExpression = "metal:tin OR block:metal AND metal:copper"; rule.Exclude = true;
        check(!PneumaticRouterFilters.Match(new PneumaticPortSettings { Rules = new[] { rule } }, stack), "Exclusions veto the entire evaluated value expression");
        rule.ValueExpression = "metal:copper OR";
        check(!new PneumaticRouterConfiguration { Ports = new[] { new PneumaticPortSettings { Rules = new[] { rule } }, new(), new(), new() } }.Valid,
            "Server configuration validation rejects invalid expressions before they can replace a working filter");
    }
    private static void Persistence(Action<bool, string> check)
    {
        var json = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pneumatic-router-schema1.json")));
        var tree = new TreeAttribute(); tree.SetInt("schemaVersion", 1); tree.SetString("mount", "down"); tree.SetString("forward", "east");
        tree.SetString("configuration", json["configuration"]!.ToString(Formatting.None)); tree.SetString("unknown", "retain");
        var state = PneumaticRouterState.Read(tree); state.Configuration.Ports[0].Rules = Array.Empty<PneumaticFilterRule>();
        var saved = (ITreeAttribute)state.Write(); var configuration = JObject.Parse(saved.GetString("configuration"));
        check(state.Writable && saved.GetInt("schemaVersion") == 2 && state.Configuration.Version == 3 && saved.GetString("unknown") == "retain" &&
            (string?)configuration["future"] == "retain" && (string?)configuration["Ports"]![0]!["futurePort"] == "retain" &&
            configuration["Ports"]![0]!["Rules"]!.Count() == 0 && PneumaticRouterState.Read(saved).Writable &&
            (int?)configuration["Ports"]![0]!["Priority"] == 0 && (string?)configuration["Ports"]![0]!["Role"] == "input" &&
            "" == state.ExpectedParcel,
            "Router schema-one fixture migrates sequentially without deleting legacy settings or unknown fields");
        state.Lost = true;
        check(PneumaticRouterState.Read(state.Write()).Lost, "Schema-two route alerts survive save and reload");
        var ranks = (JObject)json["configuration"]!.DeepClone();
        int[] priorities = { -10, -3, 0, 10 };
        for (int i = 0; i < 4; i++) ranks["Ports"]![i]!["Priority"] = priorities[i];
        var migrated = PneumaticRouterConfiguration.Parse(ranks.ToString(Formatting.None));
        check(migrated?.Ports.Select(p => p.Priority).SequenceEqual(priorities) == true &&
            migrated.Ports.Select(p => p.SortingPriority).SequenceEqual(new[] { 1, 1, 2, 3 }),
            "Priority migration adds three distinct top ranks while retaining every literal legacy value");
        var malformedLost = saved.Clone(); malformedLost.SetString("lost", "true");
        var protectedLost = PneumaticRouterState.Read(malformedLost);
        check(!protectedLost.Writable && Bytes(malformedLost).SequenceEqual(Bytes(protectedLost.Write())), "Malformed route alerts remain byte-identical and read-only");
        foreach (int version in new[] { 0, 3, 99 })
        {
            var bad = tree.Clone(); bad.SetInt("schemaVersion", version); var unreadable = PneumaticRouterState.Read(bad);
            check(!unreadable.Writable && Bytes(bad).SequenceEqual(Bytes(unreadable.Write())), "Unknown router schemas remain byte-identical and read-only");
        }
        foreach (string text in new[] { "null", "{\"Version\":4}", "{\"Revision\":\"2\"}", "{\"Ports\":[null,null,null,null]}",
            "{\"version\":1}", "{\"Policy\":\"priority\",\"Policy\":\"round-robin\"}" })
        {
            var bad = tree.Clone(); bad.SetString("configuration", text); var unreadable = PneumaticRouterState.Read(bad);
            check(!unreadable.Writable && Bytes(bad).SequenceEqual(Bytes(unreadable.Write())), "Malformed or newer filter JSON is protected without coercion: " + text);
        }
        using var r = new Rig();
        var previous = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pneumatic-router-schema2.json")));
        var second = new TreeAttribute(); second.SetInt("schemaVersion", 2); second.SetBool("lost", true);
        second.SetString("configuration", previous["configuration"]!.ToString(Formatting.None));
        var current = PneumaticRouterState.Read(second); var rewritten = (ITreeAttribute)current.Write();
        var roundTrip = PneumaticRouterState.Read(rewritten);
        var oldRule = roundTrip.Configuration.Ports[1].Rules[1]; var item = new ItemStack(r.Item);
        item.Attributes.SetString("batch OR version (old)", "present");
        var retained = JObject.Parse(rewritten.GetString("configuration"));
        check(roundTrip.Writable && roundTrip.Configuration.Version == 3 && roundTrip.Lost && roundTrip.Configuration.Revision == 7 &&
            roundTrip.Configuration.Ports[1].MatchAny && (string?)retained["Ports"]![1]!["Rules"]![0]!["futureRule"] == "retain" &&
            oldRule.Value == "batch OR version (old)" && oldRule.ValueExpression == "\"batch OR version (old)\"" && PneumaticRouterFilters.Match(oldRule, item),
            "Configuration-two fixture migrates to three, preserving unknown keys, grouping mode, route alert and old literal operator text");
        check(((ITreeAttribute)r.Sender.State.Write()).GetInt("schemaVersion") == 3, "Adding routers leaves existing transport documents at schema three");
    }
    private static void Air(Action<bool, string> check)
    {
        var root = new PneumaticPosition(0, 0, 0, 0); var junction = root.Offset(BlockFacing.EAST);
        var other = junction.Offset(BlockFacing.NORTH);
        var nodes = new[] { new PneumaticLineNode(root, PneumaticLineKind.Intake, null, BlockFacing.EAST),
            new PneumaticLineNode(other, PneumaticLineKind.Intake, null, BlockFacing.SOUTH),
            new PneumaticLineNode(junction, PneumaticLineKind.Router, null, null, Inputs: new[] { BlockFacing.WEST, BlockFacing.NORTH }, Outputs: new[] { BlockFacing.EAST, BlockFacing.SOUTH }),
            new PneumaticLineNode(junction.Offset(BlockFacing.EAST), PneumaticLineKind.Tube, BlockFacing.WEST, BlockFacing.EAST),
            new PneumaticLineNode(junction.Offset(BlockFacing.SOUTH), PneumaticLineKind.Tube, BlockFacing.NORTH, BlockFacing.SOUTH) };
        var flow = PneumaticLineFlow.Solve(nodes, new[] { new PneumaticSupply(root, 1), new PneumaticSupply(other, .5) }, .1);
        check(flow.Status == "ok" && flow.Sections[junction].SelectedInput == BlockFacing.WEST &&
            Math.Abs(flow.Sections[junction.Offset(BlockFacing.EAST)].Received - .4) < 1e-9 &&
            Math.Abs(flow.Consumed + flow.Vented - 1.5) < 1e-9, "Strongest inlet wins and one finite air remainder is split across both outputs");
        var unpowered = nodes.ToArray(); unpowered[4] = unpowered[4] with { Loaded = false };
        flow = PneumaticLineFlow.Solve(unpowered, new[] { new PneumaticSupply(root, 1) }, .1);
        check(!flow.Sections.ContainsKey(unpowered[4].Position) && Math.Abs(flow.Consumed + flow.Vented - 1) < 1e-9,
            "An unloaded router branch consumes no duplicated budget or forced chunk load");
        var loop = new[] { nodes[0], nodes[2] with { Inputs = new[] { BlockFacing.WEST, BlockFacing.SOUTH }, Outputs = new[] { BlockFacing.EAST } },
            nodes[3] with { Output = BlockFacing.SOUTH },
            new PneumaticLineNode(new(0, 2, 0, 1), PneumaticLineKind.Tube, BlockFacing.NORTH, BlockFacing.WEST),
            new PneumaticLineNode(new(0, 1, 0, 1), PneumaticLineKind.Router, null, null,
                Inputs: new[] { BlockFacing.EAST }, Outputs: new[] { BlockFacing.NORTH, BlockFacing.SOUTH }),
            new PneumaticLineNode(new(0, 1, 0, 2), PneumaticLineKind.Tube, BlockFacing.NORTH, BlockFacing.SOUTH) };
        flow = PneumaticLineFlow.Solve(loop, new[] { new PneumaticSupply(root, 1) }, .1);
        check(flow.Sections.ContainsKey(loop[^1].Position) && Math.Abs(flow.Consumed + flow.Vented - 1) < 1e-9,
            "Directed loops cannot recirculate air and their downstream exits remain powered");
    }
    private static void Motion(Action<bool, string> check)
    {
        using var rig = new Rig(); var host = Replace(rig);
        bool joined = true, turnsSealed = true, orientations = true, speeds = true;
        string speedFailure = "";
        foreach (var mount in BlockFacing.ALLFACES)
        foreach (var forward in BlockFacing.ALLFACES.Where(f => f.Axis != mount.Axis))
        {
            host.Router.Mount = mount; host.Router.Forward = forward;
            orientations &= Enumerable.Range(1, 4).Select(host.Router.Face).Distinct().Count() == 4;
            foreach (int input in Enumerable.Range(1, 4)) foreach (int output in Enumerable.Range(1, 4).Where(n => n != input))
            {
                host.State.Cargo = new ItemStack(rig.Item); host.Router.TransitInput = input; host.Router.TransitOutput = output;
                foreach (double p in new[] { 0.0, 1.0 })
                {
                    var pose = PneumaticRouterMotion.Pose(host, p); var c = pose.Cargo;
                    var v = Mat4f.MulWithVec4(host.Router.Matrix, new[] { (float)c.X, (float)c.Y, (float)c.Z, 1f });
                    var f = host.Router.Face(p == 0 ? input : output).Normali;
                    joined &= Math.Abs(v[0] - (.5 + f.X * .5)) < 1e-6 && Math.Abs(v[1] - (.5 + f.Y * .5)) < 1e-6 && Math.Abs(v[2] - (.5 + f.Z * .5)) < 1e-6;
                }
                for (double p = PneumaticRouterMotion.CloseEnd; p < PneumaticRouterMotion.OpenStart; p += .01) turnsSealed &= PneumaticRouterMotion.Pose(host, p).Door < 1e-9;
                const double seconds = .04;
                double step = seconds / PneumaticRouterState.TransitSeconds;
                foreach (double p in new[] { 0.0, .1, .2, .75, .85, .95 })
                {
                    double speed = PneumaticRouterMotion.Pose(host, p).Cargo.DistanceTo(PneumaticRouterMotion.Pose(host, p + step).Cargo) / seconds;
                    if (Math.Abs(speed - 2) >= 1e-6) { speeds = false; if (speedFailure == "") speedFailure = $"local p={p}: {speed:R}"; }
                }
                var inlet = host.Router.Face(input); var outlet = host.Router.Face(output);
                Vec3d World(double p)
                {
                    var local = PneumaticRouterMotion.Pose(host, p).Cargo;
                    var world = Mat4f.MulWithVec4(host.Router.Matrix, new[] { (float)local.X, (float)local.Y, (float)local.Z, 1f });
                    return new(world[0], world[1], world[2]);
                }
                var upstream = new PneumaticState { Input = inlet, Output = inlet.Opposite };
                var downstream = new PneumaticState { Input = outlet.Opposite, Output = outlet };
                var before = PneumaticRenderer.CargoPosition(upstream, PneumaticLineKind.Tube, false, 1 - seconds / .5)
                    .Add(inlet.Normali.X, inlet.Normali.Y, inlet.Normali.Z);
                var after = PneumaticRenderer.CargoPosition(downstream, PneumaticLineKind.Tube, false, seconds / .5)
                    .Add(outlet.Normali.X, outlet.Normali.Y, outlet.Normali.Z);
                double entry = before.DistanceTo(World(0)) / seconds, exit = World(1).DistanceTo(after) / seconds;
                if (Math.Abs(entry - 2) >= 1e-5 || Math.Abs(exit - 2) >= 1e-5)
                { speeds = false; if (speedFailure == "") speedFailure = $"{mount.Code}/{forward.Code} {input}->{output}: entry={entry:R} exit={exit:R}"; }
            }
        }
        check(orientations && joined && turnsSealed, "All 24 mounts and 12 port pairs meet shared faces and turn with the door closed");
        check(speeds && PneumaticRouterState.TransitSeconds == 1, "Router transit takes one second while entry, exit and both neighboring pipe stretches travel at exactly two blocks per second on every mount" + (speedFailure == "" ? "" : ": " + speedFailure));
        host.Router.Lost = true; var lost = PneumaticRouterMotion.Pose(host, 1);
        check(lost.Door < 1e-9 && lost.Cargo.DistanceTo(new Vec3d(.5, .5, .5)) < .2 &&
            PneumaticRouterMotion.AlertBrightness(0) == 0 && PneumaticRouterMotion.AlertBrightness(.75) == 2 && PneumaticRouterMotion.AlertBrightness(1.5) == 0,
            "A lost parcel stays behind a closed door and its alert smoothly reaches black and twice normal brightness");
    }
    private static void Presentation(Action<bool, string> check)
    {
        var presented = new PneumaticRouterPresentation();
        presented.Observe(new(270, 0, new()), 0);
        presented.Observe(new(0, 0, new()), 100);
        check(presented.At(100).Angle == 270 && presented.At(150).Angle == 315 && presented.At(200).Angle == 360,
            "A new catch target smoothly takes the shortest turn across the zero-degree boundary without snapping");
        presented.Observe(new(90, 0, new()), 150);
        check(presented.At(150).Angle == 315 && presented.At(200).Angle == 382.5,
            "A replacement input target starts from the displayed intermediate pose instead of resetting to the server pose");
        presented.Observe(new(90, 1, new()), 250);
        check(presented.At(250).Door == 0 && presented.At(300).Door == .5 && presented.At(350).Door == 1,
            "Arrival and idle packet changes keep the rendered door motion continuous");
        using var r = new Rig(); var host = Replace(r); host.Router.ExpectedParcel = Guid.NewGuid().ToString("N");
        host.Router.ReadyInput = 3; host.Air = 1;
        bool doorClosed = true;
        foreach (double p in new[] { 0.0, .25, .5, .74 }) doorClosed &= PneumaticRouterMotion.Pose(host, p).Door == 0;
        var aligned = PneumaticRouterMotion.Pose(host, 1);
        check(doorClosed && aligned.Angle == -180 && aligned.Door == 1, "Anticipation closes the door, aligns the carriage, then opens only after alignment");
        host.State.Cargo = new ItemStack(r.Item); host.Router.TransitInput = 1; host.Router.TransitOutput = 3;
        Vec3d Path(double p, long _) => PneumaticRouterMotion.Pose(host, p).Cargo;
        var handover = new PneumaticCargoMotion();
        handover.Observe("handover", "previous-pipe", 0, new(-.2, .5, .5), 0);
        handover.ObservePath("handover", "router", 1, 0, Path, 100);
        handover.TryPosition("handover", "router", 150, out var incomingFace);
        handover.TryPosition("handover", "router", 200, out var incomingBoundary);
        handover.ObservePath("handover", "router", 1, .1, Path, 200);
        handover.TryPosition("handover", "router", 250, out var incomingInterior);
        handover.Observe("handover", "previous-pipe", 0, new(-.2, .5, .5), 250);
        check(Math.Abs(incomingFace.DistanceTo(incomingBoundary) / .05 - 2) < 1e-6 &&
            Math.Abs(incomingBoundary.DistanceTo(incomingInterior) / .05 - 2) < 1e-6 &&
            !handover.TryPosition("handover", "previous-pipe", 250, out _),
            "Pipe-to-router handoff preserves velocity when switching from shared world positions to the router path");
        var arrival = new PneumaticCargoMotion();
        arrival.ObservePath("arrival", "router", 1, .3, Path, 0);
        arrival.ObservePath("arrival", "router", 1, .4, Path, 100);
        arrival.TryPosition("arrival", "router", 100, out var entering);
        arrival.TryPosition("arrival", "router", 110, out var caught);
        arrival.TryPosition("arrival", "router", 120, out var stopped);
        check(Math.Abs(entering.DistanceTo(caught) / .01 - 2) < 1e-6 && stopped.DistanceTo(Path(PneumaticRouterMotion.CatchEnd, 0)) < 1e-8,
            "A client packet spanning the catch boundary preserves pipe velocity then stops at the carriage without creeping");
        var departure = new PneumaticCargoMotion();
        departure.ObservePath("departure", "router", 1, .6, Path, 0);
        departure.ObservePath("departure", "router", 1, .7, Path, 100);
        departure.TryPosition("departure", "router", 192, out var launched);
        departure.TryPosition("departure", "router", 197, out var leaving);
        check(Math.Abs(launched.DistanceTo(leaving) / .005 - 2) < 1e-6,
            "A client packet spanning the launch boundary immediately travels at ordinary pipe velocity after launch");
        departure.ObservePath("departure", "router", 1, .9, Path, 300);
        departure.TryPosition("departure", "router", 400, out var faceApproach);
        departure.Observe("departure", "next-pipe", 2, new(1, .5, .5), 400);
        departure.TryPosition("departure", "next-pipe", 450, out var faceCrossing);
        check(Math.Abs(faceApproach.DistanceTo(faceCrossing) / .05 - 2) < 1e-6 &&
            !departure.TryPosition("departure", "router", 450, out _),
            "The shared presentation keeps router-to-pipe handoff velocity and only lets the new host draw the parcel");
        arrival.ObservePath("arrival", "router", 1, .4, Path, 200);
        arrival.TryPosition("arrival", "router", 5000, out var waiting);
        check(waiting.DistanceTo(Path(.4, 0)) < 1e-8, "A paused router never extrapolates unreported parcel progress");
    }
    private static void Runtime(Action<bool, string> check)
    {
        int baseline;
        using (var r = new Rig(tubes: 3)) { Warm(r); r.Fill(); baseline = Finish(r, 64); }
        using (var r = new Rig(tubes: 3))
        {
            var router = Replace(r); Warm(r); r.Fill(); bool anticipated = false, conserved = true; int elapsed = 0;
            for (; elapsed < 2000 && Delivered(r) < 64; elapsed++)
            { r.Step(); anticipated |= router.State.Cargo == null && router.Router.ExpectedParcel != "" && router.Router.Preparation >= 1; conserved &= r.Total == 64; if (elapsed % 11 == 0) r.Reload(); }
            check(anticipated && conserved && Delivered(r) == 64, "Runtime router prepares before arrival and delivers 64 real items through repeated native reloads");
            check(elapsed > baseline, $"The deliberately slower router adds transit delay: {elapsed * .1:F1}s versus {baseline * .1:F1}s for 64 items");
        }
        using (var r = new Rig())
        {
            var router = Replace(r); Warm(r); r.Fill(8);
            router.Router.Configuration.Ports[0].Rules = new[] { Rule("mod", "other") };
            router.Router.Configuration.Ports[0].Role = "closed";
            router.Router.Configuration.Ports[2].Role = "input";
            router.Router.Configuration.Ports[2].Rules = new[] { Rule("mod", "other") };
            for (int i = 0; i < 50; i++) r.Step();
            check(r.Source.Inventory[0].StackSize == 8 && router.PortRole(1) == "input" && router.PortRole(3) == "output", "Pipe directions override retired manual roles and output filtering participates in supplier discovery");
            router.Router.Configuration.Ports[2].Rules = Array.Empty<PneumaticFilterRule>();
            for (int i = 0; i < 100 && r.Sender.State.Cargo == null; i++) r.Step();
            router.Router.Configuration.Ports[0].Rules = new[] { Rule("mod", "other") };
            for (int i = 0; i < 100 && router.State.Cargo == null; i++) r.Step();
            check(router.State.Cargo != null, "Legacy input filters are retained but do not reject committed parcels");
            router.Router.Configuration.Ports[2].Rules = new[] { Rule("mod", "other") };
            for (int i = 0; i < 60; i++) r.Step();
            check(r.Total == 8 && router.State.Cargo != null && Delivered(r) == 0, "Changed live output filters hold cargo inside the router without deletion or rerouting to the wrong inventory");
            router.Router.Configuration.Ports[2].Rules = Array.Empty<PneumaticFilterRule>(); Finish(r, 8);
            check(Delivered(r) == 8 && r.Total == 8, "Restoring compatible filters resumes the original committed delivery");
        }
        using (var r = new Rig())
        {
            var router = Replace(r); Warm(r); r.Fill(8);
            r.Source.Inventory[0].Itemstack!.Attributes.SetInt("durability", 23); r.Item.Tool = EnumTool.Pickaxe; r.Item.Durability = 100;
            router.Router.Configuration.Ports[2].Rules = new[] { Rule("tool", "pickaxe"), Rule("durability", "20") };
            Finish(r, 8);
            check(Delivered(r) == 8 && r.Target.Inventory.Where(s => !s.Empty).All(s => s.Itemstack!.Attributes.GetInt("durability") == 23),
                "Native extraction, router handoff and insertion preserve actual tool durability");
        }
        using (var r = new Rig())
        {
            var router = Replace(r); Warm(r);
            var configuration = new PneumaticRouterConfiguration { Policy = "round-robin" }; r.Store.Fail = true;
            check(!r.System.ConfigureRouter(router, configuration) && router.Router.Configuration.Revision == 0 && router.Router.Configuration.Policy == "priority",
                "Aborted router configuration rolls back the entire versioned document");
            check(r.System.ConfigureRouter(router, new()) && !r.System.ConfigureRouter(router, new()), "Router revisions reject stale concurrent edits");
        }
        using (var r = new Rig(tubes: 3))
        {
            var first = Replace(r); var second = Replace(r, 3);
            first.Router.Configuration.Ports[2].Role = "input";
            Warm(r); r.Fill(8); second.Router.Configuration.Ports[2].Rules = new[] { Rule("mod", "other") };
            for (int i = 0; i < 60; i++) r.Step();
            check(r.Source.Inventory[0].StackSize == 8, "Supplier discovery applies every router's output filters along a chain");
            second.Router.Configuration.Ports[2].Rules = Array.Empty<PneumaticFilterRule>();
            bool conserved = true; for (int i = 0; i < 200; i++) { r.Step(); conserved &= r.Total == 8; }
            check(Delivered(r) == 8 && conserved && second.PortRole(1) == "input" && first.PortRole(3) == "output", "Adjacent routers infer their roles from adjoining pipes and transfer one real parcel through both carriages");
        }
        using (var r = new Rig())
        {
            var router = Replace(r); Warm(r); r.Fill(8); r.Denied.Add(router.Pos);
            for (int i = 0; i < 50; i++) r.Step();
            check(r.Source.Inventory[0].StackSize == 8, "Router route discovery rejects currently protected intermediate blocks before extraction");
            r.Denied.Clear(); for (int i = 0; i < 100 && router.State.Cargo == null; i++) r.Step();
            r.Unloaded.Add(router.Pos); double progress = router.State.Progress;
            for (int i = 0; i < 30; i++) r.Step();
            check(r.Total == 8 && router.State.Progress == progress, "Unloading an occupied router preserves its cargo and mechanical progress");
            r.Unloaded.Clear(); Finish(r, 8); check(Delivered(r) == 8, "Reloaded router resumes its existing delivery after chunk availability returns");
        }
        using (var r = new Rig())
        {
            var router = Replace(r); Warm(r); r.Fill(8); r.Source.Inventory[1].Itemstack = new ItemStack(r.Item, 8);
            r.Source.Inventory[0].Itemstack!.Attributes.SetString("special", "deny");
            router.Router.Configuration.Ports[2].Rules = new[] { Rule("attribute", "special", true) };
            Finish(r, 8);
            check(Delivered(r) == 8 && r.Source.Inventory[0].StackSize == 8 && r.Source.Inventory[1].Empty,
                "A mixed supplier chest skips filtered slots and orders its matching actual stack");
        }
    }
    private static void RoundRobin(Action<bool, string> check)
    {
        foreach (string policy in new[] { "round-robin", "forced-round-robin" })
        {
            using var r = new Rig(); var router = Replace(r);
            var branch = r.Host(2, 2, "receiver", BlockFacing.NORTH, BlockFacing.SOUTH, z: 1);
            var target = r.Chest(branch.Pos.DownCopy());
            router.Router.Configuration.Policy = policy; Warm(r);
            foreach (var slot in target.Inventory) slot.Itemstack = new ItemStack(r.Item, 64);
            r.Fill(8); for (int i = 0; i < 100; i++) r.Step();
            check(Delivered(r) == 8, policy + " only considers routes to the requesting receiver, regardless of a different full receiver");
        }
        using (var r = new Rig())
        {
            var router = Replace(r); var branch = r.Host(2, 2, "receiver", BlockFacing.NORTH, BlockFacing.SOUTH, z: 1);
            var target = r.Chest(branch.Pos.DownCopy()); router.Router.Configuration.Policy = "round-robin"; Warm(r); r.Fill(32);
            for (int i = 0; i < 400; i++) r.Step();
            check(target.Inventory.Sum(s => s.StackSize) > 0 && Delivered(r) > 0 && target.Inventory.Sum(s => s.StackSize) + Delivered(r) == 32 && r.Source.Inventory.All(s => s.Empty),
                "Both receivers fairly order real parcels without overriding their pinned destinations");
        }
    }
    private static void Recovery(Action<bool, string> check)
    {
        foreach (string policy in new[] { "priority", "round-robin", "forced-round-robin" })
        {
            using var r = new Rig(); var router = Replace(r);
            var other = r.Host(2, 2, "receiver", BlockFacing.NORTH, BlockFacing.SOUTH, z: 1);
            var otherChest = r.Chest(other.Pos.DownCopy()); router.Router.Configuration.Policy = policy;
            router.Router.Configuration.Ports[1].SortingPriority = 3;
            router.Router.Configuration.Ports[2].SortingPriority = 1;
            router.Router.Configuration.Ports[1].Rules = new[] { Rule("mod", "other") };
            Warm(r); r.Fill(8);
            for (int i = 0; i < 100 && router.State.Cargo == null; i++) r.Step();
            check(router.State.Cargo != null && router.State.Destination == r.Receiver.State.Instance, policy + " reserves the requesting receiver");
            router.Router.Configuration.Ports[1].Rules = Array.Empty<PneumaticFilterRule>();
            var broken = r.Hosts.Single(h => h.Pos.X == 3 && h.Pos.Z == 0);
            r.System.Unregister(broken); r.Hosts.Remove(broken); r.Entities.Remove(broken.Pos);
            for (int i = 0; i < 40; i++) r.Step(); r.Reload();
            check(router.Router.Lost && router.State.Cargo?.StackSize == 8 && router.State.Progress <= PneumaticRouterMotion.CloseEnd &&
                otherChest.Inventory.All(s => s.Empty) && Delivered(r) == 0 && r.Total == 8,
                policy + " captures lost cargo through reload instead of using a higher-priority exit to another receiver");
            r.System.Unregister(other); r.Hosts.Remove(other); r.Entities.Remove(other.Pos);
            r.Host(2, 2, "tube", BlockFacing.NORTH, BlockFacing.EAST, z: 1);
            r.Host(3, 2, "tube", BlockFacing.WEST, BlockFacing.EAST, z: 1);
            r.Host(4, 2, "tube", BlockFacing.WEST, BlockFacing.NORTH, z: 1);
            r.Receiver.State.Input = BlockFacing.SOUTH; r.Receiver.State.Output = BlockFacing.NORTH;
            if (policy == "priority")
            {
                r.System.Unregister(r.Sender); r.Hosts.Remove(r.Sender); r.Entities.Remove(r.Sender.Pos);
                r.Intake.SetOutlet(BlockFacing.NORTH);
                r.Host(0, 2, "tube", BlockFacing.SOUTH, BlockFacing.EAST, z: -1);
                r.Host(1, 2, "tube", BlockFacing.WEST, BlockFacing.EAST, z: -1);
                r.Host(2, 2, "tube", BlockFacing.WEST, BlockFacing.SOUTH, z: -1);
            }
            r.Store.Fail = true; r.Step();
            check(router.Router.Lost && r.Total == 8, "An aborted path repair preserves the lost parcel and its pinned destination atomically");
            Finish(r, 8, reload: true);
            check(Delivered(r) == 8 && r.Total == 8 && otherChest.Inventory.All(s => s.Empty) && !router.Router.Lost,
                policy + " periodically finds a new path and resumes delivery to the original chest without restoring the broken pipe");
        }
        using (var r = new Rig())
        {
            var router = Replace(r); Replace(r, 3);
            r.Host(2, 2, "tube", BlockFacing.NORTH, BlockFacing.EAST, z: 1);
            var detour = r.Host(3, 2, "tube", BlockFacing.WEST, BlockFacing.NORTH, z: 1);
            Warm(r); router.Router.Configuration.Ports[1].SortingPriority = 3;
            int budget = 10000;
            var graph = new PneumaticRouteGraph(r.Hosts.ToDictionary(h => h.Position), _ => true, () => --budget > 0);
            var path = graph.Find(r.Sender, r.Receiver, new ItemStack(r.Item));
            check(path?.Hosts.Contains(detour) == true, "Among routes to the same receiver, priority 3 wins over the shorter priority-1 exit");
            router.Router.Configuration.Ports[1].Rules = new[] { Rule("mod", "other") };
            path = graph.Find(r.Sender, r.Receiver, new ItemStack(r.Item));
            check(path != null && !path.Value.Hosts.Contains(detour), "A nonmatching priority-3 exit permits the matching priority-1 path to that receiver");
        }
        using (var r = new Rig(tubes: 3))
        {
            var a = Replace(r, 2); var b = Replace(r, 3); Replace(r, 4);
            r.Host(3, 2, "tube", BlockFacing.NORTH, BlockFacing.WEST, z: 1);
            r.Host(2, 2, "tube", BlockFacing.EAST, BlockFacing.NORTH, z: 1);
            r.Host(2, 2, "tube", BlockFacing.SOUTH, BlockFacing.EAST, z: -1);
            r.Host(3, 2, "tube", BlockFacing.WEST, BlockFacing.EAST, z: -1);
            r.Host(4, 2, "tube", BlockFacing.WEST, BlockFacing.SOUTH, z: -1);
            a.Router.Configuration.Ports[2].SortingPriority = 3; b.Router.Configuration.Ports[1].SortingPriority = 3;
            Warm(r); int budget = 20000;
            var graph = new PneumaticRouteGraph(r.Hosts.ToDictionary(h => h.Position), _ => true, () => --budget > 0);
            var path = graph.Find(r.Sender, r.Receiver, new ItemStack(r.Item));
            check(path != null && !graph.Exhausted, "Interacting priorities in a directed loop cannot hide an existing filtered path to the intended receiver");
        }
    }
    private static void Specks(Action<bool, string> check)
    {
        using var r = new Rig(); var router = Replace(r); Warm(r); var specks = new PneumaticAirSpecks();
        PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => PneumaticAirSpecks.ReadCell(r.World, p);
        var id = specks.TryStart(new(r.Sender.Position, BlockFacing.EAST, true), Read, new Random(41));
        bool bypass = false, continued = false;
        for (int i = 0; i < 45; i++)
        {
            specks.Advance(.05, Read);
            if (id != null && specks.TryFrame(id.Value, out var frame))
            { bypass |= frame.Position.X > 2 && frame.Position.X < 3 && frame.Position.Y < 2.22; continued |= frame.Position.X >= 3; }
        }
        check(bypass && continued, "Air specks enter the selected inlet, traverse the low sealed header and continue into an output");
        router.Air = 0;
        check(specks.TryStart(new(r.Sender.Position, BlockFacing.EAST, true), Read, new Random()) == null, "Unpowered routers cannot emit or accept continuing air specks");
        router.Air = 1;
        var branch = r.Host(2, 2, "receiver", BlockFacing.NORTH, BlockFacing.SOUTH, z: 1); r.Chest(branch.Pos.DownCopy()); Warm(r);
        specks = new(); var random = new Random(511); var ids = new List<long>();
        for (int i = 0; i < 20; i++)
        {
            var flight = specks.TryStart(new(PneumaticNetworkSystem.Position(r.Intake.Pos), BlockFacing.EAST, true), Read, random);
            if (flight != null) ids.Add(flight.Value);
        }
        bool straight = false, bent = false;
        for (int i = 0; i < 100; i++)
        {
            specks.Advance(.05, Read);
            foreach (long flight in ids) if (specks.TryFrame(flight, out var frame))
            { straight |= frame.Position.X > 3; bent |= frame.Position.Z > 1; }
        }
        check(straight && bent, "Accumulator-emitted specks choose both router branches instead of repeating one output at an equal hop count");
    }
}
