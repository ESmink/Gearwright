# Pneumatic item transport - working specification

Status: Direct transport and the approved router A are implemented. Section 15 specifies current router behavior; section 14 records the approved mechanical design.
Approved receiver A1, narrow sender A and full-height accumulator A are promoted.
Earlier design sections are historical where section 13 specifies first-version behavior.
Last updated: 2026-09-30.

This temporary developer document defines behaviour, implementation boundaries and
acceptance tests. Maintain it in place. Requirements reflect maintainer direction;
labelled implementation defaults fill gaps without claiming final balance or visual
approval. This document does not itself authorise code or modelling work. Follow
AGENTS.md when implementation is requested. Full player instructions belong in the
Wiki and concise operating instructions in the handbook.

## 1. Feature contract

Provide readable item transport around a base without requiring players to learn
pneumatics. Ordinary delivery should be inexpensive; routing and stockkeeping add
cost and capability. Models must remain simple enough for Vintage Story.

- Glass tubes show travelling cargo. A carrier is a visual abstraction, never a
  second inventory resource to craft, supply, maintain or return.
- A dedicated air intake adapts the automatic bellows air interface to the tube
  network and accepts bellows supply from multiple faces.
- Every normal shipment starts with a receiver's order. Senders never push goods
  merely because their inventories contain something.
- Ordinary receivers request the first available goods they can accept. Jonas
  stockkeepers request specific shortages against configured target quantities.
- Junction filters restrict which sender offers can reach a receiver through
  that junction. Filtering is allowed before the Jonas tier.
- Junctions have four endpoint positions around their mounting base. The mounting
  face and its opposite cannot hold tube endpoints.
- Junction endpoints have air bypasses. An upstream tube's output supplies the
  junction, which feeds downstream tubes' input ends. The maintainer explicitly
  confirmed this direction of airflow.
- With multiple incoming pipes, only the strongest supply powers the junction.
  Their strengths are not added there. No live incoming supply means no air power.
- Active parts have a simple clockwork appearance with a temporal gear built into
  their model and crafting recipe. It is not separately installed or consumed.
  Air is the ongoing operating cost. Junctions have no rotational-power connection.
- Ordinary tube sections consume a little air. More complex parts consume more.
- Senders and inline receivers retain a straight through-tube with a branch to
  their inventory. An end receiver terminates the line.
- Choose the shortest valid route. A junction counts as ten blocks. Loops must
  neither multiply available stock nor make parcels circulate indefinitely.
- Cargo is real while in transit. Destroying its current host pipe drops the
  actual items once. Receivers accept unordered cargo caused by intervention.
- Menus are allowed for junctions and stockkeepers. Identify actual visible ports
  without requiring the player to know compass directions.
- The maintainer authorised rendered model candidates for the no-junction slice
  on 2026-09-22. Keep these in the review package until a candidate is approved.
  On 2026-09-26 the maintainer authorised router mechanism candidates. Stockkeeper
  modelling remains deferred; router runtime implementation follows model selection.

## 2. Terms and directions

| Term | Meaning |
| --- | --- |
| Tube input | The end through which air and normal cargo enter a tube |
| Tube output | The end through which they leave a tube |
| Junction inlet | A connection supplied by an adjacent tube's output |
| Junction outlet | A connection feeding an adjacent tube's input |
| Offer | A snapshot of goods available at a sender, not extracted cargo |
| Order | A receiver's request for a quantity, optionally of a specific item |
| Reservation | A claim on offered goods and destination capacity before dispatch |
| Parcel | One real ItemStack in transit, with delivery metadata |
| Host | The one pipe or machine currently owning a parcel |

Air and normal cargo travel downstream. Sender discovery and orders search
upstream. Orders are software control operations, not physical signal capsules.
No separate control wiring or signal configuration is required.

```text
Automatic bellows -> Air intake -> Sender ===== Junction ===== Receiver
                                    |                            |
                               Supply chest                Destination chest

Air and cargo:       ------------------------------------------->
Orders/discovery:    <-------------------------------------------
```

Inventory branches are separate from the through-tube. The intake introduces air
only; it is not an item source or cargo destination.

## 3. Blocks and connections

### 3.1 Glass transport tube

Provide straight and elbow connections, borrowing the existing pipe system's
placement and wrench conventions where useful. Plain tubes have two cargo ends;
multiway branching belongs to a junction. Metal collars keep empty glass visible.

Implementation default: use directed input/output endpoints. Placement continues
an existing outlet naturally; wrench interaction reverses direction or selects
the exit. Show arrows during placement and inspection. Exact input bindings can
follow project conventions, but direction must be visible before placement.

One parcel per section is sufficient initially. Each parcel contains one item
type, potentially several items up to a batch limit. Render one representative
item, optionally in a simple open holder. No lids, packing animations or carrier
returns. Final appearance remains subject to later review.

### 3.2 Pneumatic Accumulator

This block converts free bellows air into enclosed transport airflow. The current
visual direction gives it a solid base, three horizontal bellows receiving faces
and one horizontal tube outlet. Its moving reservoir shows the available reserve.
Section 12.14 records the mechanism comparison; section 12.15 develops selected
A with sockets fitted to the Automatic Bellow. The accumulator has no axle connection.

The existing, unregistered intake foundation still accepts all five non-outlet
faces. That is a legacy implementation default, superseded for the new model by
the three-inlet design. Integrate the selected layout without renaming existing
classes, asset codes or saved fields; preserve any older saved orientation/data.

Implement the existing contract rather than modifying the bellows:

- Implement `Gearwright.Air.IAirReceiver` on the intake block entity or behaviour.
- `ReceiveAir(world, position, flow)` is called on the server by `AirDelivery`.
- `AirFlow.Rate` is vanilla bellows air units per real second; `Seconds` is the
  interval; `Amount = Rate * Seconds` is the quantity actually delivered.
- `Direction` points from source to intake. The receiving face is its opposite.
  Validate that face, block configuration and writable saved state.
- Sum actual amounts from independent bellows, including different intake faces.
  Do not add rates from unequal intervals or credit a delivery per consumer.
- A delivery is not a promise of future flow. If deliveries stop, supply stops
  after any explicitly bounded stored air is exhausted.

Implementation default: a small bounded plenum smooths bellows strokes. Store
only delivered air, cap capacity, vent excess and drain through a bounded outlet
rate. It bridges strokes without inventing air or requiring player pressure tuning.

Use a tuning conversion from bellows units to transport air units. Do not call the
API's values litres or kPa: it explicitly describes free airflow, not pressure.
Several bellows adding air at an intake is intentional and distinct from a
junction selecting only its strongest incoming pipe.

### 3.3 Sender

Retain a straight through-tube and a side branch to one adjacent supply inventory.
The branch inserts ordered goods; other in-transit parcels pass along the mainline
without interacting with that inventory.

Advertise extractable stacks and fulfil valid reservations. Never initiate a
shipment without a receiver order. Follow inventory extraction rules and claim
permissions; do not scan unrelated nearby containers. Revalidate source slot,
quantity and item attributes immediately before extraction.

Implementation default: advertise source slots in stable inventory order. No
sender filter menu is needed initially; junctions filter routes and stockkeepers
generate specific requests.

### 3.4 Ordinary receiver

An inline receiver has a straight through-tube and an inventory branch. Cargo for
a downstream receiver stays on the mainline; deliveries to this receiver enter
its branch. An end receiver has one tube connection and an unloading chamber or
branch, with no continuing line required.

When it has usable destination capacity, request the first valid offer found by
section 6's discovery rules. "First" must be deterministic, not dictionary or
chunk-tick order. Do not request goods that the attached inventory rejects.

Ordinary and Jonas receivers share insertion and unsolicited-cargo handling.
Their difference is how they generate orders.

### 3.5 Rotary junction

Let `baseFace` be the face against the supporting block. Valid endpoint faces are
the four perpendicular to its axis. Reject `baseFace` and its opposite during
placement, connection discovery, interaction and saved-configuration validation.

Floor or ceiling mounting permits four horizontal endpoints. Wall mounting
permits up, down and the two sides along the wall. Rotate port labels, roles and
filters with the physical machine rather than with world north.

Each fitted endpoint has an inlet/outlet role and an air bypass around the cargo
chamber. The bypass maintains permitted air paths while the chamber turns. Cargo
still passes through the chamber and cannot bypass its route or filter.

Implementation default: the first supplied connection becomes an inlet on
placement; further connections default to outlets. Players can change roles in
the menu, including several inlets. Seal unused faces. Role changes invalidate
affected routes and reservations before any further dispatch.

The mechanism performs a short receive, turn, forward cycle, one parcel at a
time. One visible cradle and a built-in gear convey the mechanism. Air drives
transport; the temporal gear provides the fictional power for selection/turning.
There is no separate gear slot, fuel state or rotational network load.

The junction is air-powered only if a correctly oriented live inlet supplies
enough air to pay its own cost. A touching or reversed pipe, stale flow, or a loop
with no live source does not qualify. The built-in temporal gear cannot create
air. A supplied but idle junction need not animate continuously.

### 3.6 Jonas stockkeeper

Use the receiver geometry with a more elaborate but still simple clockwork control
assembly. Its extra crafting cost buys exact stock maintenance. Ordinary junction
filters remain useful and do not require a Jonas stockkeeper to function.

For each configured item:

```text
shortage = max(0, target - stored - reserved_not_dispatched - in_transit)
```

Each quantity belongs to one term. Dispatch changes reserved to in-transit;
acceptance changes in-transit to stored. Never briefly omit or double-count it.

Request only the shortage, within batch and inventory capacity limits. Coordinate
senders and junctions through the same order system as ordinary receiving. Do not
create goods, craft recipes, teleport items or automatically export excess stock.

## 4. Airflow and clockwork power

### 4.1 Air distribution

Use a bounded transport model. A general gas-pressure simulation, separate air
pipes, compressors and steam integration are outside the initial scope.

For each common simulation interval:

1. Collect intake deliveries and debit released plenum air once.
2. Recompute directed offers from live intakes. Retain source provenance; previous
   downstream offers must not become independent sources on the next tick.
3. Deduct each conducting component's configured air loss. Plain tubes have a
   small positive loss; inventory branches and junctions have greater losses.
4. At each junction, compare incoming amounts over the same interval. Select the
   greatest live supply, breaking equal values by stable physical port order.
   Do not add supplies or debit an unselected inlet for junction work.
5. Pay the junction's own cost and allocate the remainder through its outlets.
   Implementation default: divide equally among connected, enabled outlets.
   Closed or disconnected outlets receive no share.
6. Allocate movement/actuation from the air delivered. A branch cannot receive
   the full budget independently of all other branches. Motion slows or waits
   when its section has insufficient air.

Deduct losses once per physical component and interval, not per receiver search.
A bypass forwards existing supply. It cannot restore bellows strength at every
junction or create a full copy for every outlet.

Normalised test example, not final balance: inlets offer 12 and 20 air units per
second. Select 20. A junction loss of 6 leaves 14; two outlets each receive 7 before
subsequent losses. Do not use 32 or deliver 14 to each outlet. Removing the stronger
source makes the next update use 12. Removing both leaves no continuing supply.

Air loops must not amplify or sustain themselves. Use positive losses, bounded
source-rooted propagation and cycle rejection. Count a root's budget only once
when branches split and rejoin. Smoothed air is a finite stored quantity, never
a remembered "powered" flag that outlives its source.

The selected cargo route must have usable air throughout. Strongest incoming
supply and shortest cargo route are separate decisions. A ten-block junction
route weight does not automatically mean ten air units of consumption.

### 4.2 Built-in temporal gears

The temporal gear appears in the active part's model and is paid in its crafting
recipe. The crafted block is complete. Do not add an installation interaction,
removable gear inventory, gear charge, wear, fuel lifetime or replacement loop.
There is no "missing gear" operational state for a normally crafted block.

Air is the ongoing cost and limits all transport. A junction with no air stays
inactive despite its visible gear. Active mechanisms can stop their animation
when idle or starved. The bellows retains its existing mechanical drive.

Implementation default: gear-bearing active parts are senders, receivers,
junctions and stockkeepers. Plain tubes and the passive intake adapter do not
require a temporal gear. Confirm final recipes and endpoint costs during balance;
keep direct ordinary transport cheaper than a network of junctions/stockkeepers.

## 5. Junction filters and sender discovery

Implementation default: each junction outlet has an optional item allow-list.
It determines which goods can be ordered through that outlet. An unset filter
permits all normal offers; a configured filter permits matches only. Every
junction filter along a candidate route must pass.

Apply filters before reservation, while finding offers available to the receiver.
If a sender contains mixed goods, retain matching offers. Do not hide the whole
sender merely because one slot fails. A sender with no matching offer is
unavailable through that route.

Use one item-matching helper for filters and stockkeeper samples. Match item
identity and meaningful variants, not display names. Normal food-age changes
should not invalidate an item category. Preserve the actual stack's attributes.
Define tool-condition and container-content matching before supporting special
cases; do not accidentally match an arbitrary filled container by its shell only.

Recheck filters when reserving and entering a changed junction. Cargo already in
the chamber when a filter changes remains real: finish a committed safe exit or
hold for rerouting. Never delete a parcel to enforce a filter.

## 6. Orders and shortest routes

### 6.1 Route graph and deterministic selection

Represent directed tube links and legal junction transitions. Only traverse
loaded, compatible, writable connections with correct directions, usable air and
passing filters. Use weighted shortest-path search, not unweighted breadth-first
search. Count each ordinary tube as 1 and each junction traversal as 10 total,
not 10 per port and not 11.

Implementation default: an inline sender/receiver traversed on its mainline also
costs 1. Source/destination inventory attachments add 0. These are block-based
weights, independent of animation duration or airflow loss.

Example: 18 tube sections plus a junction cost 28. A valid alternative of 24 plain
sections costs 24 and wins. Air availability is an eligibility condition; it does
not replace route cost with a "most air" cargo-path preference.

Search upstream from the receiver, reversing permitted cargo edges. Include the
relevant port in search state when transitions differ. Apply filters to candidate
item signatures; cache by topology/filter revision and match conditions rather
than declaring a sender globally available to every request.

Positive weights and best-known distances terminate loops. Deduplicate senders
and their offers even when several paths reach them. Keep the cheapest valid
route for each offer. Never count the same source quantity once per path.

Implementation default for ties: route cost, stable sender position including
dimension, source slot, then stable port order. An ordinary receiver takes the
first insertable offer in that order. If a route becomes invalid, use the shortest
remaining valid route instead. Exact ties must not flap between ticks.

### 6.2 Dispatch lifecycle

1. Determine destination capacity and existing commitments. Stockkeepers also
   calculate the shortage for a configured item.
2. Discover eligible offers. An ordinary receiver accepts the first it can store;
   a stockkeeper restricts candidates to its requested item.
3. Reserve source quantity and destination capacity together, up to the available
   stock, batch limit, free capacity and remaining shortage.
4. Revalidate source, claims, route, filters, air and capacity on the server.
   Cancel a failed undispatched reservation without extracting anything.
5. Atomically remove the actual quantity from its source and create one parcel in
   the sender's insertion chamber. Change its commitment to in-transit.
6. Move between adjacent hosts. Junctions perform their receive/turn/forward cycle.
   Air and free transit slots limit progress.
7. Insert through the destination inventory API. Record actual acceptance, fulfil
   only that quantity and retain any remainder as real cargo.

An order never owns a second copy of its goods. Undispatched reservations may
expire; dispatched cargo does not expire because an order is old. Locate its
owner before retrying a shipment or extracting replacements.

Implementation default: one outstanding batch per ordinary receiver and one per
stockkeeper row. Schedule receivers fairly, such as rotating which gets the first
turn, while preserving shortest-route selection. Advance existing cargo before
dispatching more into congested sections. Blocked work waits with backoff.

### 6.3 Intervention and unordered arrivals

Accept cargo physically delivered into a receiving branch whenever its inventory
can normally store it, even without a matching order. Jonas receivers also accept
items absent from their target list: targets govern requests, not rejection of
unexpected arrivals.

Examples include player insertion through a supported interaction, altered tube
connections and an order whose destination has been removed or reconfigured.
The manual insertion interaction itself is optional; unsolicited acceptance is
required regardless of which player intervention produces the arrival.

An inline receiver does not normally steal addressed cargo passing on its main
tube. Acceptance applies when it enters that receiver's branch, including a
player-caused diversion.

If full, keep the parcel in its current host/chamber and wait. Do not erase it,
eject it routinely or bounce it around a loop. An unexpected insertion can consume
capacity expected by another order; reconcile reservations and hold affected
in-transit goods safely rather than overwriting inventory.

Record the actual receiving endpoint. A diversion must not falsely fulfil the
original destination's order. Settle that shipment once, release its old
commitment and let the original receiver recalculate its outstanding need.

## 7. Ownership, destruction and recovery

The current host block entity owns the real ItemStack. Clients interpolate its
movement; the server owns extraction, host changes, insertion and world drops.

Minimal parcel data: unique ID, actual stack, current host/local progress,
intended receiver/order reference when present, and a revalidatable next-hop plan.
A cached route never authorises insertion into an unloaded, removed or newly
protected block.

Required invariants:

- Quantity belongs to a source inventory, one transit host, a destination inventory
  or a world drop. Reservations hold metadata, not spendable item copies.
- The departure host retains ownership until the adjacent-host move commits.
  Interpolated visuals across boundaries cannot make the drop location ambiguous.
- Breaking the host pipe drops its actual cargo at that block once. Apply this to
  occupied senders, junctions and receiver chambers too. Drop no visual carrier.
- Breaking a different route section leaves cargo in its current host. Invalidate
  the route and find a valid shortest continuation or wait for intervention.
- Multiple engine removal callbacks cannot produce duplicate drops. Settle
  ownership removal, the drop and affected commitments idempotently.
- Preserve item identity, quantity, variants, durability, container contents and
  normal spoilage. An order's sample never replaces actual cargo attributes.
- Unloading/restarting preserves cargo and pauses affected work. Do not force-load
  chunks to finish an order.

Design crash recovery before source extraction and cross-host movement. Where the
engine cannot save affected inventories atomically, use stable transfer IDs and
an explicit recoverable commit/receipt mechanism. A reservation alone is not
crash safety. Test interruptions around every ownership change; do not guess
which duplicate is authoritative or discard ambiguous saved data.

Use new versioned pneumatic state documents. Preserve unknown fields; retain
malformed/newer data unchanged and disable writes as AGENTS.md requires. Persist
cargo, configuration, finite stored air and unresolved transactions. Rebuild
disposable route/flow caches. Reconcile orders against surviving parcel IDs before
issuing replacement orders. Bound journals, queues and per-tick recovery work.

## 8. Menus that correspond to the machine

### 8.1 Junction faceplate

Show the actual four-port arrangement around a central chamber on a compact
clockwork faceplate. Give ports permanent local labels A-D and distinct symbols
or dot patterns visible on the block. Colour can supplement these labels.

- Open with the clicked physical endpoint selected where possible.
- Orient the preview to match the viewed machine, including wall/ceiling mounting.
  Physical identities stay stable as the player walks around it.
- Selecting/hovering a port outlines that endpoint in the world.
- Show arrows and plain roles: "Air in", "Air out" or "Closed". Display the
  adjacent block/container name when available.
- Outlet controls have a few ghost sample slots for the allow-list. Copy samples
  without consuming items. An unset filter visibly says "Any".
- Show useful central status: supplying port, waiting for air, waiting for cargo
  or blocked exit. Draw the chosen transfer between its visible endpoints.

Avoid compass dropdowns, coordinate lists and mandatory route names. A static
textured faceplate plus world highlighting gives it a clockwork character without
an intricate animated UI. Do not add a temporal-gear slot or fuel gauge.

### 8.2 Stockkeeper tally plate

Use a small set of rows with a ghost sample, target and current progress:

```text
Charcoal     Keep [64]      Here 24   Coming 16   Still needed 24
```

A number-wheel appearance can use practical direct number entry and +/- buttons.
Do not force dozens of clicks. Copying a held stack may set the initial sample and
quantity. Show the linked inventory name above the rows.

Implementation default: four rows, empty rows inactive, matching item variants by
sample. Explain "No matching supply", "No powered route" and "Destination full"
on the affected row. Routing details and compass directions stay out of this menu.

All menu changes are server-validated configuration requests. Validate claims,
legal faces, sample count and quantity bounds. Client packets cannot authorise
arbitrary extraction or fabricate cargo.

## 9. Implementation boundaries and integration points

Keep new transport code under `code/Pneumatics/`. Suggested responsibilities below
are not final registered names or persisted identifiers.

| Responsibility | Boundary |
| --- | --- |
| Intake adapter | Existing air interface and bounded plenum |
| Topology/ports | Mount orientation, directed links, revisioning |
| Air distribution | Source budgets, losses, strongest inlet, outlet allocation |
| Route search | Weighted search, filter predicates, stable ties |
| Order coordinator | Offers, reservations, shortages, fair bounded scheduling |
| Cargo transfer | Stack ownership, host moves, receipts, insertion/drop |
| Block entities | Local configuration/cargo persistence and registration |
| Client presentation | Parcel motion, highlights, menus and status text |

Do not make cargo pretend to be a fluid or reuse hydraulic saved identities.
Share suitable API patterns while preserving existing contracts. Version topology
and filter changes to invalidate cached plans. Bound network nodes, parcels,
queued orders and search work, with readable statuses when limits are reached.

Verified integration points at this revision:

- `code/Air/IAirReceiver.cs`: per-step air consumption; independent generators add
  amounts. Preserve this contract at the intake.
- `code/Air/AirFlow.cs`: rate, duration, direction, quantity and validity checks.
- `code/Air/AirDelivery.cs`: server-only adjacent delivery with loaded checks;
  resolves block/entity/behaviour interfaces and retains vanilla compatibility.
- `code/Mechanics/BEBehaviorLargeBellows.cs`: its `Deliver` method uses that
  interface. Upper-drive strokes deliver immediately; stored lower-drive air
  drains through the same interface. Reuse this supply without redesigning it.
- `tests/Gearwright.Contracts/LargeBellowsFixture.cs`: existing amount/rate,
  multiple-generator, unloaded and client-delivery coverage.
- `code/Hydraulics/BlockFluidPipe.cs`: useful interaction/connection patterns.
- `code/Hydraulics/BlockEntityHydraulicNode.cs` and schema classes: save-protection
  patterns, not storage documents to reuse for cargo.
- `Gearwright.csproj`: compiles `code/` and packages runtime assets. This design
  document stays outside the mod archive.

Recheck current code when work begins; do not invent a replacement interface from
an earlier conversation. Target the game version in `modinfo.json`.

## 10. Implementation sequence and acceptance tests

### Current progress

The first implementation slice is under `code/Pneumatics/`:

- `BlockEntityPneumaticAirIntake` implements the existing `IAirReceiver` contract.
  It accepts delivered amounts through all faces except its configured outlet,
  caps stored air, and releases only a bounded amount on the server while loaded.
  It is deliberately not registered or attached to any block asset yet.
- `PneumaticIntakeState` starts an independent schema-1 document under
  `gearwrightPneumaticIntake`. It saves `outlet` and `storedAir`, preserves unknown
  fields and protects malformed/newer data, including an invalid outer attribute.
- `PneumaticLineFlow` handles bounded snapshots of directed straight/elbow tubes,
  senders, inline receivers and end receivers. It deducts component losses once,
  carries root provenance and stops at missing, reversed, unloaded or protected
  connections. Each update requires fresh, already-debited intake budgets. Its
  forwarded values are not independently spendable cargo-movement allowances.
- `PneumaticAirFixture` tests native bellows delivery, face validation, finite
  supply, schema-1 save/reload and protected bytes, direct-line loss accounting,
  independent dimensions, source-free loops and invalid/oversized snapshots.

This is a tested air foundation, not completed direct delivery. Remaining Phase 1
work includes the server coordinator, placement/connection discovery, inventory
permissions and offers, receiver orders and reservations, and actual stack
ownership with restart-safe transfer receipts. Design and test interrupted
ownership changes before enabling extraction. Runtime registration and player
documentation follow the approved visual and working gameplay implementation.
Section 12 defines the smaller end-to-end test that now takes priority. Rendered
candidates for its intake, tubes, sender and end receiver are under review.
Junctions, stockkeepers and their menus remain pending.

### Phase 1 - direct ordered delivery

Implement the intake, air consumption, straight/elbow links, branched sender,
inline/end receiver and authoritative parcel lifecycle. All deliveries already
use orders. Prove breakage/restart behaviour before branch routing. Verification
can start with simulation and SDK fixtures; runtime models still require approval.

### Phase 2 - junctions and weighted routes

Add mounted four-port junctions, bypass flow, strongest-inlet selection, filters,
loops and competing routes. Implement faceplate controls and world highlighting.
Exercise every mount orientation without relying on cardinal UI labels.

### Phase 3 - stockkeeping and tuning

Add shortage calculations, tally controls and contention among receivers. Tune
air costs, timing and batches against actual bellows output. Add Wiki and handbook
instructions from implemented behaviour. Follow project checks/package/installation
requirements before delivering runtime changes.

Minimum acceptance scenarios:

| Scenario | Required outcome |
| --- | --- |
| Bellows supply different intake faces | Actual delivered amounts add once |
| Bellows stops | Only finite stored air continues; supply then stops |
| Wrong/occupied intake face | No accepted pneumatic supply through that face |
| All six junction mounting orientations | Exactly four perpendicular endpoint faces work |
| Junction has no live incoming air | No forwarding or active cargo handling |
| Newly crafted junction receives enough air | Works without gear insertion, fuel or axle |
| Junction inlet offers 12 and 20 | Select 20, never 32; stable ties |
| One inlet supplies two outlets | Total allocated air plus losses never exceeds supply |
| Longer line/extra complex component | More air needed; no free strength reset |
| Sender has stock but no receiver order | No extraction or unsolicited dispatch |
| Ordinary receiver has several offers | First insertable offer in deterministic route/slot order |
| Filter excludes items at a mixed sender | Matching offers remain discoverable |
| Several filters along a route | All must permit the candidate item |
| 18 tubes + junction versus 24 tubes | Cost 24 wins over 28 if both routes are valid |
| Loop or split/rejoin | Search terminates; stock, air and shipments are not duplicated |
| Two receivers reserve the same stock | No overdrawing the source |
| Target 64, stored 24, reserved 8, travelling 16 | Request at most 16 more |
| Inventory changes after dispatch | Insert what fits or hold cargo; no overwrite/loss |
| Unordered cargo reaches regular/Jonas receiver | Accept what fits even without an order or target |
| Addressed parcel passes inline receiver | Stay on mainline unless delivered/diverted into its branch |
| Current host breaks during travel | Drop actual stack and settle commitment once |
| A later route section breaks | Retain current cargo; revalidate or wait |
| Unload/crash at transfer boundaries | Recover one owner, correct commitments and original attributes |
| Malformed/newer saved state | Preserve data, disable writes, explain pause |
| Rotated menu view | Selected port matches the highlighted physical endpoint |
| Long-running active block | No temporal-gear depletion or replacement interaction |

## 11. Balance choices and maintenance

The air foundation uses provisional constants: 100 transport air units per
bellows unit, a 40-unit plenum, and a 15-unit/second outlet. Plain tubes lose
1 unit/second; sender and receiver sections lose 2. The passive intake adds no
further loss. Snapshots permit at most 256 nodes and intervals up to 0.25 seconds;
invalid intervals are rejected rather than running unloaded-time catch-up. These
values support deterministic tests and are not final gameplay balance. Intake
release must run once per common server interval, before consumer searches.

Still to tune: air conversion, plenum capacity, per-component losses, actuation
costs, cargo speed, batch size, network limits, UI row/filter-slot counts and
recipes. Confirm exact endpoint crafting costs while keeping ordinary transport
affordable. Special matching rules for tools/filled containers and final cargo
appearance also need review. These do not reopen the behavioural requirements.

Update normative rules, implementation defaults, examples and tests together when
the maintainer changes direction. Keep this developer note separate from player
documentation; retire it when implementation and permanent references supersede it.

Superseded ideas: physical carrier inventories/returns, mandatory menu-less setup,
rotational junction power, sender-initiated push delivery, separately installed
temporal gears and temporal-gear fuel consumption.

- 2026-09-21: Started the Phase 1 air foundation after implementation was requested.
  Added intake state, the server bellows adapter, bounded direct-line flow and SDK
  contracts. No runtime blocks, recipes, item transfers or models are enabled.
- 2026-09-21: Replaced the conversational outline with component, airflow, order,
  routing, ownership, UI and test contracts. Confirmed strongest incoming air flows
  through the junction to outgoing tubes. Temporal gears are integral model and
  crafting ingredients; air is the ongoing operating cost.

## 12. First vertical slice: ordered input to output, no junction

This section defines the next deliverable. It is a complete playable transfer
through a small line, including the necessary visuals, interaction and recovery.
The broader feature rules above still apply. This section describes work to
implement; it does not claim that the rendered models move real items yet.

The maintainer prefers the original B piping and has approved receiver A1 in
glass, brass and iron with thicker vanilla-style gears. Sections 12.10-12.12
supersede the original end-receiver visual assumptions below. Sender A is approved
with a narrower inventory branch in section 12.13. Section 12.14 compares three
Pneumatic Accumulator designs; A is now selected and revised in section 12.15. Visual approval
does not claim that runtime transfer or inventory integration is implemented.

### 12.1 Test rig and completion condition

```text
Existing driven Automatic Bellow
             |
             v
         Air intake -> Sender -> Glass tube -> Glass tube -> End receiver
                         |                                      |
                    Input chest                            Output chest
```

The bellows, its Reciprocating Drive Shaft, the mechanical power source and both
inventories are existing game/mod equipment. Four new block families are needed:
air intake, glass transport tube, sender and end receiver. One tube item supports
both straight and elbow configurations. The new receiver study preserves a
straight host tube and diverts into its inventory branch. The first rig can end
at that receiver, but the selected receiver must also leave passing cargo a
straight path when idle. This does not add a routing junction to the slice.

Build the first test in a small loaded area using ordinary single wooden chests.
Connect each endpoint's inventory branch to a chest's side, keeping its lid and
front interaction clear. The candidate gallery shows the canonical downward
branch above schematic inventory boxes. Do not assume that a gravity-dependent
receiver can rotate about the tube axis: section 12.10 records each candidate's
orientation and footprint. Chest-lid clearance remains an explicit engine check
before runtime approval.

Starting with 64 identical ordinary items in the input and an empty output, the
receiver orders batches of up to eight. With the bellows running, visible cargo
must travel through each host and all 64 items must arrive. At every stable step:

```text
source quantity + hosted cargo + destination quantity + actual world drops = 64
```

No junction, filter menu, stock target, carrier supply, axle connection on an
endpoint or gear-installation step is needed. A second test substitutes a
straight-up elbow, one vertical section and an up-to-horizontal elbow for the
straight run. Cargo must follow the resulting path, including height changes.

### 12.2 Blocks, geometry and review choices

| Part | Cargo / air connection | Shape and purpose |
| --- | --- | --- |
| Pneumatic Accumulator | One horizontal tube outlet; three other horizontal faces receive free bellows air | Solid base and visible moving air reservoir; small grilles differ from the large tube collar |
| Glass tube | Exactly one input and one different output | Hollow 6-unit glass body, 7-unit collars, visible contents; straight or square elbow |
| Sender | Opposed mainline input/output; one perpendicular inventory branch | A loading chamber joining the branch to the through-line, one visible temporal gear |
| End receiver | One tube input; one perpendicular inventory branch | A catch/unloading chamber with a visibly closed end and one temporal gear |

The first family study fitted all parts in a single block; a receiver attachment
may now occupy a second block as described in section 12.10. Tube ports centre on the block faces at model
coordinate 8; 16 model units equal one block. Collars meet at block boundaries.
The two halves of a connected collar must not overlap. The revised material
direction is brass collars with geometric direction marks. The live
placement preview also draws explicit arrows; colour alone is insufficient.

The original family study compared the following mechanisms. Only B's piping is
preferred; its active components are not approved:

| Candidate | Loading mechanism | What to judge |
| --- | --- | --- |
| A - timber lift | Oak-post frame and broad glass chamber; bronze tray rises 3.5 units | Most exposed mechanism; visibility of the tray, size of the wooden structure |
| B - rotary pocket | Brass clock face and bronze cradle turning 90 degrees from branch to mainline | Compact rotary motion; whether the clock face hides too much cargo |
| C - slide lock | Copper sleeve with sight windows; brass shutters retract 2.8 units each before an air pulse lifts cargo | Few exposed parts; whether the small lock still makes loading readable |

The original receiver study reversed loading to unload; the new receiver study
instead separates diversion and insertion. Trays, cradles and
shutters belong to the machine and never travel with the items. Copper carries
light air loads; oak carries broad structural loads; bronze provides sliding
and bearing contact. Brass is used for light clockwork plates and shutters.
No candidate needs steel, and no brass-to-brass toothed mesh is introduced.

The builder is `graphics/review/pneumatic_direct_line.py`. Its single managed
package is `generated/pneumatic-direct-line-review/current/`. It provides whole
lines, empty/loaded straight tubes, an elbow, the intake, loading poses and a
receiver catch pose, with front/back/side/top/bottom evidence. The comparison
image faces the line from the opposite side to the diagram: air and cargo move
right to left there. The images label this explicitly.

Source materials are inspected installed oak, copper, brass, tin-bronze and
temporal-gear textures plus Gearwright inspection glass. The illustrated wooden
cargo cube is a scale reference. Runtime cargo must use the actual item's mesh,
scaled to fit the bore; it is not a new crate or carrier resource.

### 12.3 Placement and ordinary use

1. Place the two inventories with room for side-mounted endpoints and the line.
2. Place the sender against the input chest. The clicked supporting face becomes
   its inventory branch. The preview selects a perpendicular straight mainline
   from the player's horizontal look direction and shows input/output arrows.
3. Fit the receiver to a straight host tube and connect its inventory outlet to
   the output chest. A two-block candidate previews and validates its attachment
   cell as well. Only mechanically valid orientations are offered; do not rotate
   gravity-driven drop chambers into side or ceiling forms.
4. Place tubes outward from the sender's outlet. Placement against an existing
   outlet sets the new input to that shared face and previews the selected exit.
   Only reciprocal output-to-input faces connect. Touching sealed sides do not.
5. Place the accumulator at the sender's input and point driven bellows into its
   three marked side grilles. The preview separates its tube outlet from grilles.
6. Put items in the source and run the bellows. The receiver starts ordering when
   it has capacity and a powered route. There is no sender Start button.

Wrench right-click cycles the permitted output among faces other than the input
or inventory branch. On a sender it cycles the legal mainline axes; crouch-wrench
reverses input/output. On an end receiver it cycles the input while retaining the
inventory branch. Accumulator wrench use rotates its horizontal outlet while
keeping the solid base down. Preview the result and
reject a change that would leave an illegal face combination. Reconfiguration
cancels affected undispatched reservations before another dispatch; existing cargo
stays owned by its host and pauses until its continuation is valid.

An empty-hand right-click on an endpoint reports its linked inventory and status;
no configuration dialog is needed for this slice. Opening the adjacent chest
uses the chest's normal interaction. Server validation checks the acting player,
claims, reach, selected physical face and writable configuration. Client packets
contain configuration requests only, never item quantities to extract or stacks
to manufacture. An offline placer does not grant a bypass of current claims.

### 12.4 Inventory scope, offers and orders

The first concrete inventory adapter supports ordinary single wooden chests.
Other inventories remain unavailable until their extraction, insertion and save
participation are verified. Inspect only the one adjacent branch inventory.
Identify it by dimension, position and persistent instance identity so replacing
a chest at the same coordinates cannot inherit an old order or receipt.

Use the native slot rules: extraction requires `CanTake`; destination offers
must pass its insertion rules and current remaining capacity. Apply the actual
move with the world-aware `ItemSlot.TryPutInto` overload, recording its returned
accepted count and retaining the real remainder. Never replace a target slot
with an order sample. These hooks exist in the installed 1.22.3 SDK; they are
inventory operations, not a documented atomic-save API.

Initial orders may carry any ordinary item category the supported adapter can
round-trip intact. Tests must cover differing stack attributes. Tools, filled
containers and perishables are enabled only after their intact-stack and normal
transition/spoilage tests pass; an unsupported case reports that status and is
not extracted. In particular, never match two filled containers solely by shell
code or discard age/durability data to make a stack merge.

An end receiver has one outstanding batch. Search upstream through reciprocal,
loaded, writable and powered links, stopping at the intake. An intake is never
an item offer. Order candidates by route cost, sender position including dimension,
then slot index; deduplicate each inventory slot. The first offer the output can
accept wins. Reuse the common route weights even though a simple chain normally
has only one route. A visited set and node limit terminate malformed loops.

Reserve source quantity and destination capacity together on the server:

```text
batch = min(8, unreserved source quantity, unreserved destination capacity)
```

Before extraction, revalidate the inventory instances, slot content and quantity,
claims, route revision, usable air, receiver capacity and empty sender chamber.
A failed reservation is released without touching the source. Start recovery
reconciliation before accepting any new order after loading; never issue a
replacement while an earlier shipment may still exist.

### 12.5 One bounded server step

Use a 0.1-second common simulation interval. Cap accumulated work after a hitch;
perform at most two catch-up steps, without extrapolating missing bellows deliveries
or unloaded time. Process at most 256 nodes, 64 active parcels and 32 order checks
per step, rotating which receiver is considered first. An unsupported oversized
connected component pauses new dispatch with `network-limit`.

The server step runs in this order:

1. Reconcile a bounded number of unresolved transfers and topology changes.
2. Snapshot loaded participating nodes and validate all positions and directions.
3. Release each intake's finite air once. There are no consumer-specific releases.
4. Propagate that interval's budget, paying the existing section losses and each
   occupied section's actual movement work before forwarding the remainder.
5. Advance existing parcels, downstream first. Claim a free next-host slot and
   commit at most one adjacent-host move per parcel in the interval.
6. Accept arrivals through the inventory API and settle actual delivered amounts.
7. Consider new receiver orders and dispatch only after reservation validation.
8. Save changed stable state and send compact presentation updates.

For the first tuning pass, retain the existing 40-unit plenum, 15-unit/second
outlet, 1-unit/second tube loss and 2-unit/second endpoint loss. Each complete
parcel stage costs an additional 2 air units and takes at least 0.5 seconds:

```text
progress increment = min(seconds / 0.5, remaining local air / 2, 1 - progress)
movement air spent = 2 * progress increment
```

Debit that movement air before forwarding the remainder. The present line solver
only pays fixed losses; it must gain this movement debit before it drives cargo.
Do not let every occupied host spend the same forwarded budget independently.
Reservations and blocked parcels consume no movement work; fixed conducting
losses still apply. A full successor holds the current host at its exit. A
starved host holds its exact progress. A later speed change does not skip hosts.

The basic rig has fixed losses of 6 units/second, leaving up to 9 for movement.
The elbow rig has five tube sections and two endpoints, costing 9 units/second.
Both fit the current maximum outlet when the bellows sustains it. These are
budgets for the test, not promised throughput from a stopped or slow bellows.

### 12.6 Parcel ownership and a transfer protocol

A host owns one real stack plus `parcelId`, intended receiver/order, local stage
and progress, and a revalidatable next hop. The order contains a commitment,
not a second spendable stack. Each ownership-changing operation has a distinct
`transferId`: chest-to-sender, adjacent-host move, receiver-to-chest or host-to-drop.

Required persistence boundary: a transfer store must provide durable prepare and
participant acknowledgements. Each participant saves its transfer receipt with
the same snapshot as its inventory/cargo change. Plain `MarkDirty`, a reservation,
or the return from `ISaveGame.StoreData` is not accepted as evidence that several
independently saved blocks reached disk together. The installed SDK documentation
does not specify that guarantee.

Implement and prove this boundary before enabling extraction. The planned
`IPneumaticTransferStore` isolates engine save/flush integration from item movement.
The chest adapter must participate in the same protocol through additive receipt
state; do not rename the chest's asset, replace its inventory format, or assume
that any arbitrary `IInventory` supports crash-safe transfers.

| Step | Authoritative state and allowed action |
| --- | --- |
| Prepare | Durably record transfer ID, parcel ID, participant instances, exact before/after images and intended count. The donor still owns the items. Lock affected slots and host chambers against competing moves and removal. |
| Apply | Apply the native move once on the server and record actual acceptance. Attach the same ID and resulting ownership revision to both participant snapshots. Recovery images in the journal are evidence, not usable inventory. |
| Acknowledge | Obtain durable acknowledgements for each changed participant. Keep their affected slots/chambers locked until this is proven. No downstream transfer or player withdrawal may escape the unacknowledged boundary. |
| Commit | Durably mark the transfer committed, update the order commitment once, then unlock. The recipient is now the only live owner. |
| Reclaim | Retain receipts until both participants and the journal share a durable checkpoint. Only then prune the completed record. Never expire unresolved transfers. |

At recovery, a before-image with no applied receipt can be advanced once; an
applied receipt is never applied again. Mixed saved generations are completed
from the prepared record while participants remain locked. A missing/unloaded
participant waits. An unexpected third state, changed instance, unsupported
schema or conflicting receipt protects the records and pauses affected work;
never pick one of two apparent copies by item count or delete the evidence.

The persistence implementation must prove that prepare reached durable storage
before any independently saved participant changed, and that unlocking cannot
expose a change whose receipt can still be lost. This is a specific SDK integration
milestone, not an already implemented capability. If the engine cannot support
that proof through the chosen adapter, change the transfer-store implementation
before cargo work proceeds; do not silently downgrade to best-effort saves.

World drops use the same protocol with a persistent drop identity and an outbox
record. Clear host ownership and acknowledge the drop before allowing pickup.
Repeated removal callbacks reuse that transfer ID. After a committed drop is
picked up, absence of the entity must never trigger a replacement drop. Breaking
a different section invalidates the route but cannot drop remotely hosted cargo.

### 12.7 Saved state and runtime boundaries

Preserve the existing `gearwrightPneumaticIntake` schema 1 and the meaning of
`storedAir`. New independently versioned documents are planned at
`gearwrightPneumaticNode`, `gearwrightPneumaticInventoryReceipt` and
`gearwright:pneumatic-transfers` for node state, attached-inventory receipts and
the transfer journal respectively. These are proposed new keys; once published
their meaning is permanent.

| Document | Required durable data |
| --- | --- |
| Intake, existing schema 1 | Configured outlet, finite stored air and unknown fields |
| Pneumatic node, new schema 1 | Instance identity; input/output/branch configuration; placer identity; revision; hosted parcel including full stack; outstanding transfer receipts |
| Inventory participant, new schema 1 | Attached chest instance identity, ownership revision and transfer receipts saved with the native inventory snapshot |
| Transfer journal, new schema 1 | Prepared operation, participant identities, exact recovery images, acknowledgements, commit/checkpoint state and unresolved drops |

Use full `ItemStack` tree serialization and native item-code mapping on reload;
numeric item IDs alone are insufficient. Embedded records belong to their parent
document's schema, preserving unknown nested fields. Newer/malformed originals
remain untouched and disable writes. The journal is separate from
`gearwright:world-state` and hydraulic documents. Routes, air offers and render
caches rebuild after load. Preserve loaded cargo even when a destination is gone.

Limit active parcels to 64 and unresolved journal entries to 256 for the first
slice. Cap a serialized stack at 64 KiB, one transfer record including recovery
images at 128 KiB, and the complete journal at 8 MiB. Account for actual encoded
attributes before reserving; a too-large stack or full journal blocks dispatch.
Do not prune active records to meet either bound. Failed storage work pauses the
component and logs the affected operation without dumping private save contents.

Suggested implementation boundaries under `code/Pneumatics/`:

| Component | Job |
| --- | --- |
| Existing intake and line flow | Preserve intake semantics; add per-stage movement consumption |
| Topology registry | Loaded node lifecycle, reciprocal connections, revisions and dimension-safe positions |
| Chest adapter | Slot rules, claims, stable inventory instances, actual moves and receipt participation |
| Order coordinator | Deterministic offers, reservations, fairness, expiry of undispatched metadata |
| Transfer store and recovery | Durable prepare/apply/commit/receipts, recovery, checkpoint pruning and drop outbox |
| Parcel controller | Host ownership, stage/progress, adjacent moves and arrival handling |
| Block/entity classes | Placement, wrench validation, persistence, registration and removal hooks |
| Client renderer | Actual item mesh, parcel interpolation across committed host changes, mechanism pose and direction highlights |

The four proposed asset names are `gearwright:pneumatic-air-intake`,
`gearwright:pneumatic-tube`, `gearwright:pneumatic-sender` and
`gearwright:pneumatic-end-receiver`. They are design names, not existing registered
identifiers. Put all four in both the General and Gearwright creative tabs for
the test. Survival crafting is a later balance step; active-part recipes must
include a temporal gear when they become craftable. Creative blocks are complete
and operate with no installed gear item or gear fuel state.

### 12.8 What the player sees

The server sends host/parcel identity, item appearance, stage, progress, next face
and stable status. It never accepts client-authored ownership changes. Clients
interpolate reported positions, retaining a parcel's displayed world position when
a committed host update arrives. A pause or unload freezes progress. Do not show cargo in a successor that has
not accepted ownership. The representative mesh retains a consistent small
visual size through elbows and shows the real item, not an order's sample.

| Status code | Player text |
| --- | --- |
| `waiting-air` | Waiting for air |
| `no-input-inventory` | Connect the sender branch to a supported inventory |
| `no-output-inventory` | Connect the receiver branch to a supported inventory |
| `no-supply` | No available items |
| `destination-full` | Output inventory is full |
| `route-broken` | Tube line is disconnected or reversed |
| `waiting-chunk` | Waiting for the next section to load |
| `waiting-space` | Waiting for space in the next section |
| `access-denied` | Inventory access is not permitted |
| `unsupported-cargo` | This item is not supported by this test build |
| `network-limit` | This line exceeds the current transport limit |
| `recovering` | Finishing a saved transfer |
| `protected-state` | Transport paused to protect saved cargo; check the server log |

Retry ordinary blocked work after 0.5, 1, then at most 2 seconds; meaningful
inventory, air or topology changes can wake it earlier. No per-tick error spam.
Low air never changes cargo identity or quantity. Receivers accept unsolicited
cargo physically diverted into their branch whenever it fits, independently of
an order. Record the actual receiver and settle the old commitment once.

### 12.9 Implementation order and acceptance gate

1. Select or revise a visual candidate. Keep its reviewed port geometry fixed.
2. Build the durable transfer-store/chest-adapter spike and interruption fixtures.
   Demonstrate persistence ordering and protected ambiguous recovery before the
   first real extraction is enabled.
3. Add versioned host state and order/parcel simulation fixtures. Wire topology,
   movement air, reservation validation and the actual inventory adapter.
4. Promote the approved models, register the four block families, add placement,
   wrench interactions, cargo rendering and concise status text.
5. Add handbook instructions and the focused Wiki page from the implemented
   behaviour, including the exact test rig and supported inventory/item limits.
6. Run full project checks, package/install and isolated server smoke checks.
   The maintainer performs interactive engine checks; agents use SDK tests,
   deterministic renders and logs, never desktop automation.

| Test | Required evidence |
| --- | --- |
| Basic 64-item line | Eight ordered batches arrive; conservation checked after every ownership change |
| No receiver / full output / stopped bellows | No source extraction; finite stored air runs out without remembered power |
| Unequal bellows intervals / wrong face | Actual amounts add once; the tube outlet cannot receive bellows air |
| Two elbows and a vertical section | Cargo follows every host, clears both bends and obeys air costs |
| Inventory replaced or claims changed | Stale reservations cancel; travelling goods stay real and held |
| Output fills after dispatch | Native insertion accepts what fits, retains the remainder and never overwrites |
| Multiple senders/receivers on simple chains | Stable offer order, shared stock accounting and fair scheduling |
| Current host removed; callback repeated | Actual cargo drops at that host once, with no carrier drop |
| Later section removed/reversed | Current ownership is unchanged; repair resumes the same parcel |
| Clean save/reload and cross-chunk unload | Exact item attributes, progress and commitments survive; unloaded work pauses |
| Crash before/after each persistence step | Recover one owner under every saved-generation combination, or protect ambiguous evidence |
| Crash around drop creation and pickup | No duplicate drop or replacement after a committed pickup |
| Changed attributes / special item cases | Preserve complete stack data; unsupported cases remain in the source |
| Unordered arrival | Accept what fits, retain remainder and settle the actual destination correctly |
| Client packets / read-only schema / network limits | Cannot fabricate cargo, mutate protected data or exceed bounded work |
| Geometry and engine presentation | Empty mainline is clear; all required orientations align; chest lids, glass, arrows and actual item meshes are readable |

Visual approval chooses geometry. The slice is complete only when the real-item
tests pass and the installed build can perform the rig in-game. Review stills,
an air-only simulation and a clean build alone do not satisfy that gate.

### 12.10 Receiver focus: visible diversion and insertion

This records the first receiver comparison. The maintainer has since selected A
for further iteration. B and C are retired from the managed gallery; section
12.11 describes the current three A variations. This is a direction choice, not
approval to promote a runtime model.

The maintainer selected B's piping as the preferred reference, requested mostly
glass and brass with iron mating teeth, and withheld approval of the other
elements. Keep the six-unit glass body, seven-unit collars and thin band. Show a
keyed shaft from the temporal gear into actual transmission parts. A decorative
gear beside an independently moving item is insufficient.

Receiver work has two distinct phases: take cargo out of the straight line, then
mechanically insert it into the attached inventory. All three candidates retain
west/east host ports. Their branch terminates at an inventory, never another
tube route. B and C attach to an existing straight tube and occupy one additional
cell; removing that attachment must restore the host's ordinary service pane.

| Receiver candidate | Diversion | Insertion and power path | Footprint and question |
| --- | --- | --- | --- |
| A — tip gate and feed rollers | The floor flap turns up 90 degrees before arrival, opening a drop while becoming the downstream stop | Temporal shaft / iron 8-tooth pinion / brass 16-tooth feed wheel / iron roller. An iron 10-tooth mate drives the flap through a trip clutch; the flap holds while the feed continues | One block, inventory below. Is this compact action readable enough, and can the roller contact suit the intended item envelope? |
| B — drop chamber and crank ram | The same gate drops cargo into a lower glass chamber. Equal small cranks and a constant-length rod connect the gate to its clutched drive | Temporal shaft / iron 10-tooth pinion / brass 40-tooth wheel / 4.8-unit crank / iron slotted crosshead. The 9.6-unit ram stroke pushes cargo into the neighbouring chest | Two cells stacked vertically, chest beside the lower attachment. Does the long visible stroke justify the height? Gravity requires this orientation |
| C — swing fork and rack ram | A six-unit-radius fork turns 180 degrees inside a sealed glass sidecar. The shared hood replaces the host service pane | The temporal shaft drives the fork through a clutch. Its iron 12-tooth pinion also drives a brass 24-tooth wheel; a second clutched iron pinion advances the brass insertion rack 13.5 units | Two cells side by side, inventory beyond the sidecar. Does supported handling and the separate glass outlet justify the width? |

Brass carries the light housing, bearing brackets and appropriate driven teeth.
Iron is used for compact shafts, the roller contact surface, fork tines and the
ram crosshead. Every brass toothed member has an iron mate. Glass exposes the
movement rather than becoming a solid metal machine with a small window.

The source is `graphics/review/pneumatic_receiver.py`; its only managed package
is `generated/pneumatic-receiver-review/current/`. Each candidate has assembly,
installed inventory-cutaway, glass-removed mechanism and parked pass-through states. The `receive` animation
shows one delivery and holds its final pose. The gallery contains rest, diversion,
handoff and insertion, with the same scale, cameras and lighting across choices.
The Python reviewer is part of delivery, not a command left for the maintainer
to run. The older full-line package remains reference for B piping only.

These are kinematic design studies. The clutches show the two output paths and
their disengagement, but their trip/detent controller, indexed engagement and
return sequence need engineering after selection. The review does not establish
native glass rendering, a finished automatic gearbox, inventory transfers or
pressure sealing. The parcel is a 2.4-unit envelope; actual item meshes and their
contact presentation need approval in the implementation.

For either attachment candidate, the host is the sole simulation and cargo
owner. A subordinate attachment stores a versioned host reference and instance
identity, not a second parcel. Placement validates both cells, replaceability,
claims and inventory access before committing either part. An incomplete pair
after unload/recovery stays blocked without extracting cargo. Removal callbacks
for either part resolve through the host and reuse its transfer/drop receipt;
they cannot each drop a copy. Store the selected shape/orientation additively in
the proposed node schema; existing intake schema 1 remains unchanged.

Revalidate inventory capacity before starting insertion. If it becomes blocked,
hold the owned parcel and the mechanism in a supported pose. For an inline host,
unselected cargo stays on the mainline; it must not be caught merely because an
inventory is attached. Full outputs must not leave a flipper raised indefinitely
to intercept unrelated traffic. The choice of receiver must specify when the
host becomes occupied and when the through-line is released. Those timing rules
join the same server-authoritative transfer and recovery tests in section 12.9.

The immediate approval is the receiver concept and its material/footprint
direction. It does not approve the sender, intake or complete old B family.

### 12.11 Receiver A: cam-driven flipper and feed variations

This comparison led to the maintainer selecting A1; section 12.12 records the
requested compact revision. Keep the compact flipper and downward feed from A.
The comparison is
about actual mechanical transmission: concentric keyed shafts, correctly spaced
iron/brass gear pairs, supported bearings, follower contact and a visible return
stroke. The former unexplained trip clutch is removed. Each cam is generated
from its follower's required path in the rotating cam's coordinate system.

All three candidates occupy one block, retain B's glass tube/collar proportions,
and connect to inventory below. Both rollers are powered in every variation.
The temporal gear's eight teeth directly mesh with a 14-tooth iron wheel keyed
to the left roller. That wheel meshes with a 14-tooth brass wheel keyed to the
right roller and cam. All three gears share one working plane; there is no
separate input pinion hiding the temporal gear's role. Their pitch radii are
1.6, 2.8 and 2.8 model units, with cuboid strips approximating shortened involute
teeth and leaving running clearance. The temporal gear turns 630 degrees for
each 360-degree cam cycle. Both roller surfaces travel down at the nip at equal
speed. This shared train is a requirement, not a difference between candidates.

Glass and brass remain dominant. Iron is used
for mating teeth, shafts, bearing bolts, roller cores and cam followers. Thin
leather roller sleeves provide a plausible traction surface against the cargo.
The inspected source material is referenced logically in the review source.

| Variation | Drive from temporal gear | Flipper actuation | Intended comparison |
| --- | --- | --- | --- |
| A1 — weighted return cam | Shared temporal 8 → iron 14 → brass 14 train powers both rollers and the cam | An external cam lifts a short roller-follower lever. A weighted arm keeps a closing moment even at the vertical position and returns the flap as the cam falls | Fewest active interfaces; an exposed, readable cam. Depends on gravity and an unobstructed return |
| A2 — captive cam | Same shared train and two powered rollers | Two cam rails capture the follower, guiding both opening and closing | Positive opening and return without a weight; an extra rail and more sliding contact |
| A3 — rack-driven flipper | Same shared train and two powered rollers | The captured follower translates a guided brass rack 1.885 model units against an iron 12-tooth gate pinion of pitch radius 1.2, producing 90 degrees of travel | Clearest intermediate linear stroke; most parts and the tallest drive face |

One cam revolution defines the review cycle. From 0 to 110 degrees the floor
turns upward through 90 degrees. It holds until 235 degrees, opening the drop and
blocking the onward line. Cargo arrives only after opening and descends between
the rollers. From 235 to 355 degrees the gate returns to its floor stops after
cargo clears its sweep. At 360 the gate is parked and the representative parcel
is entirely below the inventory interface. The review holds that final pose;
it does not create a second parcel or teleport the first one to the inlet.

This replaces the old receiver set in the same fixed
`generated/pneumatic-receiver-review/current/` package. Assembly, exposed
mechanism, cam detail with drive wheels hidden, installed inventory-cutaway and
empty pass-through states remain.
The Python reviewer is launched for the maintainer. The comparison uses identical
cameras and scale; individual boards show all six significant cycle poses.

Geometry checks must cover cam/follower clearance including interpolated poses,
the temporal-to-iron and iron-to-brass tooth meshes, equal inward roller motion,
gate-to-cargo clearance, shaft/bearing apertures, bounds, and the
opening/hold/return sequence. Matching the motion numerically does not establish
native pressure sealing, traction or automatic recovery from a jam. The fixed
2.4-unit nip and representative parcel are still review abstractions. After a
variation is selected, define preload/adjustment for irregular rendered items,
air/traction balance and safe server-controlled holds before runtime promotion.
The persistence and inventory-conservation requirements remain unchanged.

### 12.12 Selected A1: compact gearwork

The maintainer approved the final thick-gear A1 assembly with SHA-256
`be424a07b652259f211efecbb0ea8d122def1b7c05d561e16a84abb345d90bb0`.
The manifest records this exact approved reference and permits a subsequent
runtime promotion. A geometry-hash check preserves it while the sender is
designed. The review generator continues to write only its managed package.

The maintainer selected `a1-return-cam` and requested minimum space between
gearwork elements. The managed package now contains only A1. Its manifest
records the selected assembly reference and SHA-256
`a186b2cb28c6885fdfca72bcdd913335d9b02a19baa4bec1895376bdcb6a0dd0`.
The captive-cam and rack alternatives are retired from the generator and gallery.

The first compact pass reduced the spacing between mechanical layers but left
visible shaft gaps against the housing. Following the maintainer's marked side
view, the gear, cam and follower planes now sit at 3.80 / 4.15 / 4.38 model units.
The temporal hub-to-bearing gap is reduced from 1.21 to 0.13 units. The lower cam
extends 0.138 units into the frame depth, removing the exposed stand-off there.

The gear rims and teeth are now 0.80 units thick, increased from 0.176 following
the maintainer's request for thicker vanilla-style gears. Hubs are 1.00 unit deep.
All additional depth extends outward, preserving the close rear mounting faces.
Cam rails remain 0.216 units thick and the follower lever 0.10.
Independent faces retain 0.086 units between the gear rims and cam
webs, 0.05 between the cam rail and return weight, and 0.08 between the follower
and gate-bearing bolts. The brass output wheel and iron cam hub share a keyed
shaft. Shafts pass through the full wheel depth and finish just beyond the hubs.

The installed `game:block/metal/mechanics/gear24`, `game:item/gear-temporal` and
`game:block/wood/mechanics/spurgear16` models were inspected and rendered.
The receiver's metal wheels use gear24's thick faceted rim, broad cross spokes,
square projecting hub and block teeth as construction references. Four cuboid
steps per tooth retain the existing working profile. The temporal wheel uses
the native item's crossed-bar lattice, small face pins, texture and glow while
keeping its eight teeth in the working mesh. Inspected native iron and brass
plate textures replace the sheet materials on the wheels. Source references
and dimensions are recorded in the manifest; native reference renders remain
in the same managed review package. These are newly authored cuboids fitted to
the receiver, not copied game shapes.

Front roller bearings are recessed to z=4.90 and the gate bearing to z=4.86.
Their supporting sills move inward while remaining joined to the frame posts.
A fitted port in the main tube's front pane clears the recessed gate bearing.
The inlet floor ends at x=5.98 and the downstream floor begins at x=11.32, giving
the flipper nose and keyed gate shaft clearance throughout the cycle.

The temporal axle moves 30 degrees inward around the iron roller gear, reducing
the height and empty space above the drive. Its teeth are indexed to the new
contact angle. The temporal and brass gear tip envelopes remain over 0.10 units
apart: a second mesh there would lock the train. Existing gear pitch distances,
8:14:14 tooth counts, inward roller motion, cargo clearance, cam profile and
weighted return are retained. The frame still supports all three driven axles.

The render board shows the full assembly, a close side view of the housing fit, and the
exposed cam. The Python reviewer retains the complete animation and all five
inspection states. Tests cover the moving mechanism against the front frame,
bearings and glass, the marked shaft gaps, inter-layer clearances, and the
non-mating gear pair throughout the cycle. This completes the requested design revision;
the remaining runtime engineering in section 12.11 is still separate work.

### 12.13 Approved sender A: rack lift and narrow inventory branch

`graphics/review/pneumatic_sender.py` owns
`generated/pneumatic-sender-review/current/`. The maintainer selected A and asked
for a narrower inventory input branch. Only the revised rack lift remains in
the package; B's yoke and C's parallel arms are retired. The selected basis is
`a-rack-lift/assembly.shape.json`, SHA-256
`f3bfda1ba75e4c697c86f61e780a28ad35caa9f7234b48515cf48ab86dac08e3`.
The maintainer subsequently approved the narrowed assembly at SHA-256
`fee034d4eea28d6a4b379841e6d2b68a853f2a7316d9a43709d3a4e698a661f9`.
The manifest records this exact reference and a regression test preserves it.
The revised sender retains the
approved receiver's thick cuboid gears, native plate and temporal textures,
glass enclosure, brass collars and iron wear parts. The temporal gear directly
meshes with the driving wheel. No brass teeth mesh against brass teeth.

The lower glass branch is reduced from 9.5 to 5.2 units wide and is 5.2 units
deep, matching the inventory collar. Its frame contracts around the branch,
and glass shoulders close the remaining through-tube floor around the loading
opening. A fitted front slot clears the carriage stem in both the pane and
shoulder. The guide rails attach to the smaller base and the tube's crown band.
The full-size 3.3-by-3.4 tray retains 0.8 units of side clearance and 0.75 units
of depth clearance. The gears, rack profile, cargo size and 6.2-unit stroke are
unchanged. The revised branch remains a review-only geometry change.

The canonical inventory branch points down and the mainline runs west to east.
One 2.4-unit representative parcel starts on the tray at the inventory mouth
after the inventory-to-sender ownership handoff. The mechanism lifts it 6.2
model units into the mainline, holds it for the transport-air launch, then
returns the tray empty. The cargo leaves through the outlet and never teleports
back to the source. The mechanism fits one block and leaves a 2.4-unit
through-line envelope clear when parked. Source extraction and dispatch remain
receiver-ordered under the ownership protocol in section 12.6.

The original mechanism comparison led to the selection of A:

| Candidate | Actual load path | What this option tests |
| --- | --- | --- |
| A - rack lift | Eight temporal teeth drive a ten-tooth iron pinion. Its radius-2 pitch circle drives a brass rack and a guided tray through 6.2 units. The train reverses for return. | Straight motion with few joints; the rack projects above the tube during loading. The temporal wheel is offset left so it cannot engage the rising rack. |
| B - crank and sliding yoke | Eight temporal teeth drive a 14-tooth iron wheel. A radius-3.1 crank pin runs sideways in a horizontal yoke while rails constrain the tray vertically. One forward revolution lifts and returns it, stopping for the launch dwell. | Compact wheels and a broad crosshead; shows the conversion from rotation to a straight lift without a rack. |
| C - parallel lifting arms | Eight temporal teeth drive a 14-tooth iron wheel and two radius-3.3 arms. Vertically spaced ground pivots and three-unit vertical couplers at the front and rear keep the tray level. The train reverses after launch. | Linkage visible inside the glass, fixed shaft ports and an arcing cargo path. The tray moves outward to x=10.17 before returning to x=8 at launch; the arms never cross a collinear toggle. |

Glass and brass carry the enclosure and low-impact fittings. Iron carries
compact loaded pins, shafts and guiding rails; copper is too soft for those
wear contacts, and wooden sections would obscure the loading chamber. A has
a fitted carriage-stem slot with leather edge strips.

The `send` animation provides branch handoff at frame 0, lift through 150,
raised dwell through 250, air launch from 180 to 240, and empty return from
250 to 360. Assembly, glass-removed mechanism, wheel-removed drive detail,
installed inventory cutaway and parked pass-through states support review.
The revised board shows the assembly, front branch width and bottom inventory
mouth, with a full cycle board and orthogonal views. The Python viewer opens
this sender package.

The narrower branch is approved. Tests cover cargo against all
parts, working gear/rack meshes, the full-size tray's clearance inside the
narrow walls, supported shaft apertures, glazing and shoulders, block bounds,
the parked mainline and managed output replacement. The receiver's approved shape hash is
also checked. Pressure sealing, load-holding during dwell, reversal and valve
control, real inventory handoff, variable cargo, jam recovery, alternate
orientations and chest access still require engineering before runtime use.

### 12.14 Pneumatic Accumulator: original mechanism comparison

The maintainer requested a solid base, three air inputs and one pneumatic tube
output. **Pneumatic Accumulator** is the proposed display name for the intake.
The maintainer subsequently selected A. Section 12.15 supersedes this initial
study's dimensions and intake centres; B and C are removed from the gallery.
This naming decision does not rename `PneumaticIntakeState`,
`BlockEntityPneumaticAirIntake`, `gearwrightPneumaticIntake`, `outlet`, `storedAir`
or any existing public identifier.

`graphics/review/pneumatic_accumulator.py` owns the fixed review directory
`generated/pneumatic-accumulator-review/current/`. All three candidates have a
basalt plinth and brass manifold. Their air grilles face west, north and south;
the 7-unit tube collar faces east. All connections remain at face centre 8 in a
16-unit block throughout animation. Top and bottom are closed. The intended
grounded placement offers yaw rotations, not side or ceiling mounts.

| Candidate | How the reserve is visible | What to compare |
| --- | --- | --- |
| A - rising leather bag | Four pairs of leather folds open under a brass lid and compact iron weight. The lid rises 5 units on four guides and falls as the reserve drains. | The requested soft reservoir, a broad moving silhouette and direct height cue. The lower clamp and plumbing stay fixed. |
| B - spring piston | Air raises a leather-edged piston inside a fixed glass chamber. It travels 5 units and compresses a visible steel coil against the fixed top. | A fixed outer silhouette with a clear internal boundary and visible elastic load. |
| C - weighted air drum | Air occupies the sector between a fixed divider and a moving vane inside a glass-sided drum. The vane turns 60 degrees and lifts a weight on the same shaft. | A broad round reservoir with an arc cue and directly visible gravity return. |

These are passive buffers. They do not need a temporal gear or mechanical
input. Brass carries low-impact fittings, clamps and the vane; leather supplies
the flexible wall or replaceable seals. Iron supports compact guides, shafts and
the weight. B needs a spring material suitable for repeated flexing; its steel
coil uses the inspected dark iron finish in this review. Stone carries the
static base load without making the whole foundation metal.

The `charge` review animation fills during frames 0-100, holds full to 140,
drains through 240 and holds empty to 260. The jointed leather folds and coil
segments retain their authored lengths. Assembly, glass-removed mechanism and
isolated-port states have common cameras; the stills show empty, half and full.

Charge is a qualitative reserve/available-power cue, not a new unit of pressure.
The foundation currently stores up to 40 transport-air units and caps output
at 15 units per second. It does not yet implement the requested relationship
between reserve, pressure and usable pneumatic power. After model selection,
define a monotonic power response from the authoritative stored reserve and
actual delivered supply, then drive the displayed pose from that state. Never
credit air from a render frame or silently reinterpret old saved quantities.
The new face restriction also needs compatible runtime integration; the legacy
adapter still accepts five faces until then.

The candidates are review-only. Working seals, inlet check valves, relief,
spring/load tuning, runtime registration and engine visual validation remain
implementation work after the revised A is approved.

### 12.15 Selected accumulator A: full height and native bellow fit

The selected basis is `a-leather-bellows/assembly.shape.json`, SHA-256
`10b716626d7c266689f4a6d5ff8dca84beac17befb37a2a18920dd25a03b01a4`.
The maintainer requested full-block expansion, a better measure stick, less
space around the leather and better connections fitted to the Automatic Bellow.
The fixed managed package now retains only A and records that requested revision.

The solid basalt plinth ends at y=2.2, the brass manifold at 2.8, and the fixed
leather seat at 3.4. Five fold pairs expand the bag from 3 to 11.75 units tall.
The lid travels 8.75 units, with its metal top reaching exactly y=16 at full
charge. Wider fixed-length panels bring the folded bag close to the compact
air collectors. The lid and its corner ears clear the collectors throughout
the stroke. Three thin guides and the broad scale guide carry the lid.

The scale is a supported iron guide with brass plate marks: 21 ticks, longer
quarter marks, an empty outline and a filled top mark. A hollow reader cuff
attached to the lid slides around the guide. A dark index line reads its height;
the cuff clears both the guide and its fixed ticks.

The actual Automatic Bellow uses the native
`game:block/wood/mechanics/bellowslarge` body. Its `nozzle2` through `nozzle5`
form a 1.5-by-1.5-unit outer spout, centred at y=10.4 and extending 3 units into
the receiving block. The accumulator's three side inlets now share that height.
Each has a 1.6-square clear leather-lined bore and a depth of 3.15, leaving 0.05
per side and 0.15 beyond the native tip. Brass collars, iron bands and small
corner fasteners replace the original broad grilles. Air turns down through
compact collectors to the manifold. The pneumatic outlet stays at face centre
y=8 with its 7-unit collar and a short glass neck.

The connected-bellow state reads the actual installed game body and approved
automatic drive attachment, oriented into the west socket. Its fixed nozzle is
checked against the new machine. Portable tests use the measured four-wall
interface fixture; no unmodified native shape is committed. Orthogonal views,
empty/half/full poses and a close connection render accompany the animated
Python review. The unanimated rest pose also opens fully assembled.

This remains a review revision. The visual inlet height changes no existing
face or persistence contract. Compatible runtime port validation, live reserve
feedback, seals, check valves and relief remain the next integration work.

## 13. First runtime implementation

The maintainer accepted accumulator A and requested implementation. Its approved
assembly SHA-256 is `811b4e5a252d2d5194eb1270bdfb4786403f0fc5496b2e5785ffbf64f72f801b`.
`graphics/models/pneumatic_transport.py` promotes the three accepted machines and
B's straight/elbow piping. It removes only review cargo; actual ItemStacks supply
the runtime cargo mesh. Hardware and motion tracks are covered by approval hashes.
The approved A brass-sleeve revision replaces inventory-branch glass, adds dark
overlapping inventory spigots, and clears the receiver's rear gate shaft and closed
stops. `PORT_APPROVALS` records the five exact approved reference shapes; the elbow
uses the same terminal fitting rotated onto its canonical upward outlet.

The four block codes are `pneumatic-accumulator`, `pneumatic-tube`,
`pneumatic-sender`, and `pneumatic-receiver`, in the stable `gearwright` domain.
The receiver uses the accepted inline assembly and can finish a line. The follow-up
placement revision permits all six mainline directions and four perpendicular
inventory directions, reorienting the approved mechanisms and cargo paths together.
Endpoint placement takes two clicks: lock position and mainline, then select the
inventory branch by look direction. In both stages, normal placement faces opposite
the view and Shift placement faces along it, including up/down. Hovering an inventory
does not override the branch direction. Placement guidance lives in the handbook;
the first stage sends no chat prompt. Wrench right-click cycles endpoint output; Shift-wrench cycles inventory.
Busy endpoints reject reorientation. Tubes retain wrench output cycling and
Shift-wrench reversal. Client arrows use the same calculations as server placement,
which rechecks claims, reach, current inventory slot and native block occupancy.

The simulation runs one 0.1-second step per callback without hitch catch-up. It
handles at most 1024 loaded parts, 64 cargo stacks and 32 order checks, with a
rotating receiver priority and one-second order backoff. A receiver reserves up
to eight items. Native single normal wooden chests and ordinary non-perishable
solid stacks are supported. Tools, filled containers, transitionable items and
other inventories await dedicated intact-stack/transition adapters.
Any main tube outlet facing a supported chest also orders and delivers into that
chest, including on senders and receivers. Branch roles remain unchanged. A receiver
alternates its usable branch and outlet destinations, with one outstanding batch
per physical endpoint. A sender can load directly into its own outlet chest.

The intake migrates schema 1 to 2 with all literal air quantities intact. The grounded
accumulator restricts new physical delivery to its three horizontal sockets; the
legacy adapter's five-face semantics remain available to its legacy identity.
Capacity is 2400 units. Its outlet rate is `240 * (storedAir / 2400)^2` units per
second, integrated exactly over the finite stored reserve. Three lower-driven
bellows at 1 RPS balance around half full; upper-driven bellows balance around 56%.
The lower bellow nozzle releases at most two bellow units per second, retaining its
schema-1 storage and actual pumped volume. Fixed losses are 0.1 for tubes and 0.5
for endpoints. The displayed reservoir follows `storedAir / 2400`. Each complete local cycle
costs two additional units; movement is debited before any remainder continues.
Tubes need at least 0.5 seconds, and endpoint mechanisms use 1.6-second cycles.
The sender hands ownership onward at frame 224, then returns its empty tray.
Direct delivery into its own outlet chest waits until frame 240, when the stack
has entered the dark sleeve. Other outlet deliveries travel 2.25 model units
beyond the block face before insertion, hiding the disappearing mesh.

Client motion interpolates between authoritative world positions over 0.1 seconds.
A committed handoff transfers the same presentation record to the next host,
avoiding a pause at each joint. Former-host packets cannot rewind or duplicate
the mesh; stopped updates clamp to the last reported position. The client cache
holds positions only, with at most 64 records and five-second stale cleanup.
Vacating an occupied section wakes its waiting upstream neighbour immediately;
other blocked causes retain their one-second backoff.
Each active accumulator emits a pale-blue speck at its outlet connection every
random 0.2–0.6 seconds. Each speck chooses a constant speed from 2.1–2.5 blocks
per second and a transverse offset sampled uniformly within a 0.11-block disc.
The offset turns with bend tangents and carries through each joint. The same
speck follows the complete loaded mainline, including sender and receiver bores,
without a lifetime cutoff or a per-section spacing reservation. It grows in over
0.12 seconds and holds a size of 0.36/16 block and opacity of 42% inside the line.
After crossing an open outlet, it keeps moving while shrinking and fading to zero
over exactly 0.2 seconds. An unpowered tube, inventory, occupied cell or mismatched connection
stops it 0.08 block before that boundary, where it fades. Losing the current cell
to unloading, reorientation or lost air ends the speck immediately.

One client renderer owns emission and movement independently of individual block
draws, so an off-screen accumulator can supply visible downstream sections. Each
speck keeps only its current section, checks its next connection locally, and
carries unused movement time across joints. Block reads are cached per frame.
The cache permits up to 8192 specks, enough for the 1024-node network at minimum
emission intervals, with a 1024-section safety bound on malformed endless routes.
No chunks are loaded and no server or saved state is changed. Mesh and texture
resources are shared and released when the last pneumatic renderer unloads.

### Atomic engine save boundary

Inspection of the installed 1.22.3 implementation established that
`GameDatabase.SetChunks` calls `SQLiteDbConnectionv2.SetMulti`, which wraps the
entire supplied batch in one transaction and calls `Commit` after all rows.
The implemented `IPneumaticTransferStore` therefore replaces section 12.6's
multi-save prepare/acknowledge journal with a stronger single atomic save boundary:
all participating native chunk snapshots commit together. There is no period
where one participant has been durably acknowledged and another has not.

Before enabling extraction the adapter requires a disk-backed rollback journal
or WAL. A stock MEMORY/OFF connection is changed to DELETE journal mode. Every
ownership commit uses `synchronous=FULL`, restored afterward. Server mutation,
native chunk serialization and native batch writes share a gate. Tagged background
snapshots captured before an ownership commit are replaced by the latest committed
snapshot, preventing a delayed save from resurrecting extracted goods. Later
successful snapshots advance that retained checkpoint too. Checkpoints are bounded;
the adapter pauses further transfers rather than retaining an unlimited history.

Chest-to-sender includes source chest, destination chest identity, sender and receiver order metadata in that
single transaction. Host handoff includes both hosts. Receiver insertion includes
receiver and chest. Drop serialization includes the emptied host and spawned item
entity in its current chunk. On an aborted write, native trees restore the memory
state and further extraction pauses. An uncertain successful commit is recognized
by exact database bytes, not guessed from item counts.

Host schema 3 lives at `gearwrightPneumaticTransport`; schema 1 migrates to 2 by adding
`inventoryFace: down` without moving old cargo or changing frozen routes. Schema 2
migrates to 3 with `deliveryOutlet: false`, empty `deliveryFace` and empty
`deliveryInventory`, preserving old branch deliveries. New orders freeze the chosen
face and existing chest identity; replacing the target holds the cargo. Intake
schema 2 supports the larger capacity while schema-1 validation retains its old
40-unit bound. Additive chest instance and
receipt schema 1 lives at `gearwrightPneumaticChest`. Unknown fields survive and
unreadable/newer documents are protected. Existing chest inventory serialization
is untouched. Frozen parcel routes contain every possible transit host; no routing
outside that list is performed in this slice. Outstanding batches reconcile only
after every original route chunk is loaded, preventing unseen cargo from triggering
a replacement. Wrench edits therefore pause affected parcels until connections
are restored. Junctions, filter menus, stock targets and free rerouting are deferred.

Runtime fixtures deliver 64 items through straight and vertical-elbow lines while
reloading native trees during transit; they also exercise native slot attributes,
stopped air, claims, full destinations, aborted transactions and repeated drops.
Separate subprocess fixtures terminate before, during and after the actual native
SQLite batch and reopen its file to check both ownership generations. The store
fixture injects a SQLite row failure and replays delayed background snapshots.
The deterministic renderer remains diagnostic; final in-game lighting, chest-lid
clearance and feel are maintainer visual checks, without desktop automation.

## 14. Selected router A — sealed docking review, 2026-09-30

The maintainer selected A and requested a stronger carriage with one opening
side, sealed tube mouths, visible strongest-inlet selection, supported connection
frames that appear only at connected pipes, and a base mount for the lower
temporal wheel. B/C and the two-door alternatives are retired. The selected
original A reference and hash remain recorded in the review manifest.

The managed package is `generated/pneumatic-router-review/current/`, built by
`graphics/review/pneumatic_router.py`. The one-block machine has four possible
coplanar ports. Short neighbouring pipe sections are review context and extend
outside that footprint. No router class, runtime asset or saved schema exists yet.

### One-door carriage and sealed mouths

A reinforced oak carriage has iron wear rails, leather contact strips, open
high side retainers and a fixed padded rear stop. One sliding door controls both
entry and exit. Its brass rack meshes with a six-tooth iron pinion driven by an
eight-tooth temporal wheel. The supported base indexing wheel retains its 8:14
temporal-to-iron mesh. There are two temporal gears total.

Every connected pipe ends in a sealed glass guillotine shutter. The carriage
door has a projecting shoe that sits beneath the docked shutter’s lifting tab.
The powered door rises 5.22 model units, takes up 0.02 units of play, then lifts
that pipe shutter by 5.2 units. Both openings clear the parcel. The stationary
shutter slides on two guides; enclosed spring cartridges pull it shut as the
shoe lowers. This works without gravity and without a separate port gear.

The stationary tube guides stop at y=12.5 instead of y=16. The carriage guides
stop at about y=12.8 instead of y=15.2. Full-length slider shoes remain engaged
over one-third of their length at maximum lift: 1.75 units at each tube shutter
and about 1.03 units at the carriage door. The raised panels extend above the
shorter guides. Spring cartridges sit outside those shoes on dedicated base feet.
The door rack begins at y=2.5, above the lower temporal wheel’s complete sweep,
and retains tooth coverage at the door pinion through the full stroke.

All other mouths stay shut. The incoming mouth opens only during loading. The
door then closes, the table turns 90 degrees toward the north outlet, and the
same door opens the outlet shutter for delivery. Both doors close before the
empty table resets; the cycle ends with every mouth shut. The parcel rests
against the fixed rear stop during the turn. Cargo ownership remains independent
of animation frames.

### Connected pipe frames and foundation

The review definition accepts matched connected ports, input strengths and
output roles. It emits collars, supports, takeoffs and shutters only at those
ports. `two-connections` shows connected west and north pipes with no frames or
branches at the other faces. Unused header tees have inspection-glass lids.
Runtime must derive connected ports from matching adjacent pneumatic pipes,
including their actual connection direction.

Each connection frame sits on iron posts and brass shoes bolted to the base
crossbeams. These posts clear the bypass and spring plungers. Table support
columns reach the same foundation. The lower temporal wheel’s bearing has a
reinforced oak mount tied into a crossbeam, raised legs and straps; the shaft
turns freely between the legs. The central indexing bearing has an open bed on
its own base beam.

The table’s four columns now sit at cardinal positions on crossed foundation
beams, leaving the diagonal temporal wheel clear. The drive cutaway includes
the foundation so its load path is visible. All gear cuboids are checked against
the rack, table stand and frame at every animation frame; only the driven hub’s
intentional connection to its own shaft is excluded.

### Air selection and distribution

Tube-floor takeoffs, turning vanes, glass inspection necks and a hollow brass
header show the separate air circuit. The sealed mouths prevent air spilling
into the open carriage area while cargo is idle. A weaker input has a closed
butterfly across its neck, a leather seal and a horizontal shut handle. The
strongest input and every configured connected output have open neck valves.

The preview includes three explicit cases:

| State | Input strengths | Header connections |
| --- | --- | --- |
| airflow | Port 1: 80; port 2: 35 | Input 1 supplies outputs 3 and 4; input 2 isolated |
| second-inlet | Port 1: 35; port 2: 80 | Input 2 supplies outputs 3 and 4; input 1 isolated |
| three-outputs | Port 1: 80 | Input 1 supplies outputs 2, 3 and 4 |

These are relative review values, not pressure units. Equal strengths use the
lower numbered port for a stable tie. Zero supply produces no air paths. Cyan
markers trace only the selected inlet and every output. The header never adds
input pressures together.

### Review and remaining implementation

Assembly, mechanism, drive, bypass, airflow, wall-mount, two-connections,
second-inlet and three-outputs states are available. Tests cover complete closed
mouths, cargo travel, free shafts, real tooth meshes, rack engagement, shutter
timing, docking-shoe contact, spring-guide clearance, connection visibility, air
selection, foundation supports and one-block bounds excluding neighbouring context.

Oak carries broad slow loads. Brass carries fittings, bushings and reinforcement;
iron carries compact shafts, springs, pins and wear rails. Leather provides
replaceable contact pads and seals. Glass exposes air passages and closed mouths.
Brass teeth mesh with iron.

The maintainer approved this reference on 2026-09-30, then requested runtime
implementation. Section 15 supersedes the earlier runtime deferrals. Engine
rendering and playback still require the maintainer's interactive Vintage Story
review; deterministic renders and SDK contracts provide agent verification.

## 15. Runtime router, 2026-09-30

`gearwright:pneumatic-router` uses the approved A hardware through
`graphics/models/pneumatic_router.py`. Runtime promotion removes representative
cargo and neighbouring pipe context, adds independent butterfly pivots and caps
unused bypass tees. Connection frames render only beside physically matched
pneumatic sections or accumulator outlets. Scoop vanes and neck chevrons follow
the role inferred from adjoining pipes. Direct router links point away from the
nearest incoming pipe, with stable position ties and a bounded component scan.
The base mounts on all six faces, with
four perpendicular numbered ports; wrench rotation carries the filters with the
physical marks. Shift-wrench changes the mounting face when empty and unreserved.

The server scans committed parcel routes to prepare idle carriages in advance.
Preparation takes 0.4 seconds: close, align, then open. A powered transit takes
one second, twice the previous router duration and the straight-tube duration.
Entry and exit are linear at exactly two blocks per second, matching ordinary
tubes across shared faces. A package enters through the raised door, settles against the
fixed rear stop, then travels with the closed rotating carriage. The same door
reopens at the reserved outlet. Only an aligned docking shoe raises a port
shutter. Busy routers retain arriving cargo upstream; there is no visual carrier
inventory or item duplication. Client motion interpolates transit phase before
evaluating the catch/stop/launch path, avoiding velocity changes when a packet
straddles a stop. The stopped parcel follows the displayed carriage angle.
Mechanism packets interpolate from the currently displayed pose, including
changes to the anticipated input; first preparation packets cannot snap the table.
The 64-item SDK rig measured 39.7 seconds with a router versus 39.2 seconds with
a straight tube; sender and receiver cycles limit throughput in this simple rig.

Air uses one strongest incoming budget, with stable port-order ties. Weaker
incoming budgets vent; they are never added to the winner. The router uses 2 air
units/second plus the common 2-unit parcel transit cost, then divides the remaining
budget equally between loaded compatible outputs. Directed back edges are cut
deterministically so loops cannot circulate the same air repeatedly and exit
branches remain powered. Blue specks follow the low sealed ring from the selected
inlet to an output, then continue under the ordinary obstruction and fade rules.

Empty-hand or Shift right-click with a held placeable item opens the native port
editor and consumes the interaction. The numbered diagram matches
the brass tally marks, and selection outlines the corresponding world connection.
Port roles are inferred rather than editable. Only outputs have filters: eight
rules, positive match-all or match-any, and exclusion vetoes. Outputs have
priorities from 1 to 3, with 3 highest. Supported
predicates are collectible identity, mod, item/block class, material tags, tool
type, nutrition, fuel, smelting result, stackability, minimum remaining durability,
bounded code wildcards and attribute presence. A read-only hotbar/backpack sample
picker works while the editor is open; samples are not consumed.
Material tags include block material and named variants such as `metal:copper`.
Within each value-bearing rule, `AND` binds before `OR`; optional parentheses
override grouping. The material picker inserts `(tag_one OR tag_two)` containing
all sampled tags and also offers individual tags. Quoted JSON string literals
support whitespace, operator words and parentheses in a literal value. Parsing
is bounded to 1024 characters, 64 terms and 16 nested groups. Invalid syntax
keeps the editor open; the server validates the same expressions before saving.
Native durable tools now travel with their stack attributes intact. Perishables,
nested inventories and filled liquid containers still require separate support.

Every search is bound to the requesting receiver. Priority routing prefers
matching outlets that reach it, then finds a weighted path (ordinary section 1,
router 10). Ordinary round robin cycles equal-priority paths to this receiver and
skips busy paths when one is free; forced round robin queues behind its selected
path. A successful atomic order advances the cursor. If priorities in a directed
loop prevent a preferred path, search tries other filtered paths to the same
receiver. Committed cargo keeps a valid route despite priority or turn changes.
Output predicates apply during supplier search and actual router exits; input
filters are removed.

An invalid route is caught inside the closed carriage, even when an exit to a
different receiver is available. The upper temporal gear pulses over 1.5 seconds
from fully dark to twice normal brightness. Once per second, bounded searches
attempt a new loaded and permitted filtered path to the same receiver and chest.
A repair commits the parcel route and receiver reservation together without
changing either identity. Captured cargo can still leave after its old inlet is
removed or reversed, provided another inlet supplies power. Full or replaced
destination inventories never redirect the parcel into a different chest.
Base crosspieces meet the frame and central spine without overlapping faces.

Graph searches spend at most 32768 steps per tick, inspect at most 64 supplier
slots per attempt and rotate search starts to resume fairly. Existing node and
parcel limits remain 1024 and 64. Server claims, native inventory locks, current
capacity, loaded chunks and save readiness remain required. Configuration updates
are reach/claim checked, bounded to 16 KiB, rate limited and revision checked.

Router configuration/pose state uses independent schema 2 at
`gearwrightPneumaticRouter`: schema 1 migrates by adding `lost=false`.
Configuration JSON 1 -> 2 adds `SortingPriority` in 1–3 while preserving literal
legacy `Priority`, `Role`, inactive input rules and unknown fields. Configuration
2 -> 3 adds `ValueExpression`, initialized to a quoted or unambiguous literal
from the original `Value`; saved text containing operators keeps its old meaning.
The literal `Value` field is preserved. Common cargo
remains host schema 3; intake schema 2,
chest schema 1 and all unrelated documents are unchanged. Unknown keys survive;
malformed/newer state is read-only and preserves its original bytes. Cursor
reservation, cargo ownership and inventories commit through the existing native
chunk transaction adapter. Contracts exercise filters, throughput, anticipation,
round robin, forced waiting, material/tool attributes, finite air, loop exits,
reloads, configuration rollback and the oldest supported router fixture.
