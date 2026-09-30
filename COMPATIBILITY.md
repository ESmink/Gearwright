# Save and identifier compatibility

Gearwright maintains save compatibility across releases, including pre-1.0 releases, unless the maintainer explicitly authorizes a breaking change and names its scope.

An authorized break applies only to the named subsystem, schemas, or identifiers. It must be recorded in the changelog with the affected data, retain unrelated saved state, and add contracts that prevent the exception from spreading accidentally. A large refactor or pre-1.0 version does not imply permission to break compatibility.

## Permanent contracts

- Mod ID: `gearwright`
- World-state storage key: `gearwright:world-state`
- World-state format marker: `gearwright-world-state`
- A public build makes its asset codes, registered class codes, network channel names, packet field numbers, and enum numeric values permanent unless an explicitly scoped maintainer authorization says otherwise.

New names may be added. Existing names must not be repurposed for different meanings.

## Storage strategy

The first world document is UTF-8 JSON with an integer `schemaVersion`. The loader retains unknown object fields when it saves the document again.

Loading follows these rules:

1. Missing data creates the current default document.
2. Older schemas migrate exactly one version at a time.
3. Current schemas load with defaults for absent optional fields.
4. Newer schemas load in protected/read-only mode; the old mod does not overwrite them.
5. Invalid encoding, invalid JSON, or unknown formats remain untouched and disable writes. Errors are logged and exposed by `/gearwright`.

Future entity, block-entity, item-stack, and player data must follow the same behavior even if they use Vintage Story tree attributes or protobuf instead of JSON.

The pneumatic intake uses an independent schema-2 tree at `gearwrightPneumaticIntake`, with `outlet` as a physical face code and `storedAir` as transport air units. Schema 1 migrates to 2 with exactly the same literal stored amount and outlet; it does not refill the enlarged reservoir. Schema-1 quantities above its original 40-unit limit remain malformed; schema 2 supports 2400 units. An older mod protects schema 2 as newer data. The Pneumatic Accumulator uses three horizontal physical sockets and reserve-dependent output. Unknown fields survive; invalid/newer data remains untouched. The oldest intake fixture is `tests/Gearwright.Contracts/fixtures/pneumatic-intake-schema1.json`; `pneumatic-intake-schema2.json` covers the expanded reserve.

Pneumatic hosts use independent schema-3 state at `gearwrightPneumaticTransport`, including instance identity, owner, directed ports, actual ItemStack cargo, parcel ID, progress, frozen route and outstanding receiver batch. Schema 1 migrates to 2 by adding `inventoryFace: down`, preserving the original placement, cargo and orders. Schema 2 migrates to 3 by adding `deliveryOutlet: false`, `deliveryFace: ""` and `deliveryInventory: ""`: old parcels keep the legacy receiver-branch destination. New orders pin the selected branch or main outlet face and the chest's existing instance identity. Replacing or redirecting a destination holds its batch. Schema 2 and later accept routes up to 1024 nodes, while schema-1 validation retains its 256-node bound. `tests/Gearwright.Contracts/fixtures/pneumatic-host-schema1.json` and `pneumatic-host-schema2.json` freeze prior host documents, with contracts for migration and reload of in-flight cargo. Normal chests retain their additive schema-1 instance/receipt tree at `gearwrightPneumaticChest`; their native inventory format and assets retain their identities. Both documents preserve unknown fields and reject malformed/newer schemas without rewriting the original attribute. Intake schema 2 and `gearwrightLargeBellows` schema 1 are unchanged by outlet receiving. All existing block codes and registered classes remain valid.

The oldest chest receipt fixture is `tests/Gearwright.Contracts/fixtures/pneumatic-chest-schema1.json`; its native-tree round trip also verifies unknown-field retention and protected future schemas.

The additive `gearwright:pneumatic-router` block uses the existing `GearwrightPneumaticTransport` block and entity classes. Its independent `gearwrightPneumaticRouter` tree migrates sequentially from schema 1 to 2, adding the boolean `lost` route alert with a false default. Configuration JSON migrates from version 1 to 2 by adding `SortingPriority` on a 1–3 scale. The three highest distinct legacy priority ranks map to 1, 2 and 3; lower ranks share level 1. Equal legacy values stay equal. Original `Priority` and `Role` values remain saved and retain their literal meanings as retired settings. Pipe directions now determine roles; retained rules on current inputs are inactive and never edited by the input panel. No cargo, identity, route, cursor or pose field is reinterpreted. Actual cargo still uses unchanged host schema 3; intake schema 2, chest schema 1 and unrelated documents are unchanged.

Configuration JSON then migrates from version 2 to 3 by adding `ValueExpression` to each rule. The existing `Value` stays literal and is retained unchanged. Migration quotes values containing spaces, operator words or parentheses, so an old attribute named `batch OR version (old)` keeps its original meaning. New expressions are bounded and validated before a configuration replaces working settings. The router tree remains schema 2; host schema 3, intake schema 2 and chest schema 1 are unchanged. Mechanical timing changes retain normalized progress fields and all hosted cargo.

The oldest fixture is `tests/Gearwright.Contracts/fixtures/pneumatic-router-schema1.json`; `pneumatic-router-schema2.json` covers the next router tree and configuration version, including literal operator text. Contracts load each fixture, migrate, save and reload it while preserving unknown tree/configuration/port/rule fields and legacy settings. They honor explicit output rule removal, reject type coercion and duplicate known JSON keys, and leave malformed or newer documents byte-identical and read-only. Lost parcels survive reloads; route repair commits the host's revised path and the intended receiver's reservation atomically, without changing parcel or chest identity. Router settings and order cursors use the same native transaction adapter. No existing asset, registered class, field or enum value is renamed.

The 1.22.3 transfer-store adapter atomically saves all affected native chunks in one database transaction, with a disk-backed journal and FULL synchronous commit. Background chunk snapshots carry generation tags so delayed pre-transfer saves cannot overwrite a completed ownership change. Cargo routes remain fixed until delivery or destruction; unloaded chunks do not prove an outstanding parcel absent. A save-adapter error pauses new extraction. Existing world, hydraulic and mechanical schemas remain unchanged.

Hydraulic block entities currently use schema 9. Schema 6 migrates additively to 7: existing port and attachment values keep their numeric meaning, the Copper Plate Flange uses the new attachment value 4, and newly placed Irrigator Pipes add orientation, visible-support, and support-plank fields. Schema 7 migrates to 8 with an optional `woodSupportStack` for the whole-pipe wooden addon. Schema 8 migrates to 9 with an optional `woodInsulationStack` for its crate lining. Each fitted addon retains one original plank, including its item attributes; absent stacks mean no addon. Insulation requires a frame. Older pipe fields and unknown fields remain intact. Runtime fixtures load schemas 1, 3, 7 and 8, migrate, save and reload; malformed support or insulation stacks, orphaned insulation and newer documents remain read-only with their original bytes.

The coupled pressure solver changes no storage schema. `contentAmountLitres` still stores liquid litres or gas standard litres, including amounts above nominal pipe volume. Pressure is derived from those contents during simulation; the existing `networkPressure` field remains a local gauge-pressure cache. Driven suction is transient and rebuilds after loading. Existing hydraulic fields, pump schema 1, all asset codes and packet fields retain their meanings.

The Reciprocating Drive Shaft (`gearwright:lateral-crank-*`) and Reciprocating Pump each begin with independent schema 1 tree attributes. Current readers preserve unknown fields. Missing state creates defaults, while malformed, unsupported, or future schema values remain read-only and are written back untouched. Their first-release fixtures live beside the hydraulic compatibility fixtures.

The pump's swept volume is now 4 L with 0.05 L clearance. Saved `amountLitres` still means literal liquid litres or gas standard litres; existing amounts above the smaller chamber volume remain stored and can discharge normally. The derived `lastVolumeLitres` cache is bounded to the current piston range. Capacity tuning changes no fields or schemas, and runtime save/load contracts retain an older 8 L fill and unknown fields. Pipes retain their 10 L nominal volume.

## Change checklist

Before releasing a persistence change:

1. Copy an anonymized state produced by the oldest supported release into `tests/fixtures/`.
2. Add the next schema number; never skip or change an old migration.
3. Load the fixture, migrate it, validate gameplay meaning, save it, and load it again.
4. Confirm unknown fields survive.
5. Confirm the previous release sees the newer schema and does not write.
6. Confirm asset/registration aliases still resolve anything placed in a world.
7. Document the change in `CHANGELOG.md` and the wiki.

Migrations must be deterministic and idempotent at the document boundary. A migration that needs world blocks, inventories, or loaded chunks must use a resumable job.

## Removing a feature

Disabling a feature does not justify deleting its saved data. Keep its reader and stable identifiers. Mark it inactive, preserve its configuration, and offer a reversible conversion or salvage path. Only remove compatibility code when there has been an announced support window and a tested intermediate release that performs the migration, or when the maintainer explicitly includes that feature in an authorized breaking change.
