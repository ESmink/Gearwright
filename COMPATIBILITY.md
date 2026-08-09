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

Hydraulic block entities currently use schema 7. Schema 6 migrates additively: existing port and attachment values keep their numeric meaning, the Copper Plate Flange uses the new attachment value 4, and newly placed Irrigator Pipes add orientation, visible-support, and support-plank fields. Older pipe fields and unknown fields remain intact.

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
