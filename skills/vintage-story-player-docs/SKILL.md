---
name: vintage-story-player-docs
description: Audit, create, or rewrite player documentation for Vintage Story mods, especially cross-linked GitHub Wiki reference pages and concise in-game handbook entries. Use for documentation reviews, Wiki portals, item and mechanic pages, research indexes, handbook extraSections, and changes that must be checked against current mod code, assets, recipes, configuration, tests, and maintainer direction.
---

# Write Vintage Story player documentation

Write for players who want to understand an item, mechanic, or technology. Derive current-behavior claims from the project rather than plans, changelogs, or implementation notes.

## Establish the source of truth

1. Read the repository instructions and the maintainer's current request.
2. Inventory player-visible blocks, items, recipes, controls, status text, and handbook registrations.
3. Trace uncertain behavior through current assets, code, configuration, and focused tests.
4. Treat existing documentation as claims to verify, not as authority.
5. Use the installed vanilla handbook and official Vintage Story Wiki as structural and terminology references when helpful.
6. Do not assume external documentation targets the same game version.

Never present proposed features, test fixtures, renderer details, migration history, or development workflow as current player behavior.

## Build a subject map

For every current item or mechanic, identify:

- purpose;
- acquisition or crafting;
- placement and orientation;
- controls and configuration;
- inputs, outputs, and visible state;
- limits, hazards, and failure behavior;
- survival, creative-only, legacy, or placeholder status;
- related items and mechanics.

For every design direction, identify its relationship to the mod's technologies and label it clearly as planned or exploratory.

## Separate documentation surfaces

Use the GitHub Wiki as an interconnected reference. Keep the homepage to an introduction and broad lists of technologies, activities, catalogues, and project notes. Do not let the current release or one implemented subsystem determine the homepage hierarchy.

Prefer compact item and mechanic pages that reference each other. Put crafting, controls, operating ranges, and failure behavior on the page for the object or mechanic that owns them. Create a tutorial only when the maintainer explicitly requests one.

Use the in-game handbook for actions needed while playing: placement, controls, inputs, outputs, the most important limit or hazard, and a pointer to related parts. Rely on generated recipe and ingredient sections instead of restating them in prose.

Keep design research and future ideas in a clearly labeled, secondary Wiki section when the maintainer wants them retained. Keep developer tooling, implementation rationale, changelog material, save-schema history, and graphics notes outside the player Wiki and handbook.

## Structure the Wiki

Build the homepage as a portal:

1. Introduce the mod's identity and full intended scope without leading with version status.
2. Group links by broad technology, workshop activity, land or material system, equipment catalogue, and project notes.
3. Keep detailed item links in catalogue pages instead of crowding the homepage or sidebar.
4. Keep research and ideas accessible through one secondary entry.

For an item or block page, prefer:

1. status;
2. purpose;
3. crafting or acquisition;
4. behavior and controls;
5. important limits;
6. related pages.

For a mechanic or technology page, prefer:

1. status;
2. concise definition;
3. participating equipment or processes;
4. important rules;
5. intended extensions;
6. related pages.

Use status labels such as **Available**, **Creative-only**, **Legacy**, **Available foundation with planned extensions**, or **Design direction**. Cross-link nouns where they are discussed. Avoid duplicating the same rule across several pages.

## Write a handbook entry

Write one compact, item-specific entry. Lead with a direct verb: place, attach, fill, connect, hold, or right-click. Keep the entry to the few facts a player needs while holding, placing, connecting, or troubleshooting that item.

Prioritize, in this order:

1. the required action, control, or tool;
2. where the item can be placed or attached;
3. the inputs, outputs, or compatible fittings that determine whether it works;
4. one important limit, hazard, or destructive behavior;
5. links to the few items needed to use it.

Do not create handbook entries for broad technologies, mechanics, progression, or system explanations unless the maintainer explicitly requests them. Put those subjects in the Wiki. The handbook should remain a set of concise item and block references.

Use Vintage Story markup consistently:

- `<hk>rightmouse</hk>` and other hotkey tokens for configured controls;
- `<strong>...</strong>` for short action labels;
- `<br><br>` between paragraphs;
- `handbook://` or `handbooksearch://` links only after verifying the target format.

Include exact numbers only when they change a player's immediate use of the item. Omit formulas, secondary performance details, particle behavior, audio tuning, visual descriptions, tick phases, caches, schemas, asset-code history, development status, and other implementation details unless the player must react to them.

Link the names of important related items directly to their handbook pages. Verify whether each target is an `item-` or `block-` page, include the asset domain for mod content, and choose one real variant for grouped vanilla or mod entries. Do not add a link merely to make the entry feel connected.

Exclude nonfunctional test items from the handbook when compatibility permits. For deprecated items that can exist in old worlds, give one sentence naming the supported replacement.

## Edit with evidence

For each factual paragraph, identify the supporting asset, recipe, code path, test, or maintainer direction. If sources disagree, follow runtime behavior and flag the stale source.

Preserve public identifiers and save compatibility. A documentation request does not authorize renaming assets, changing handbook registrations, or altering runtime behavior. When runtime files are out of scope, put exact handbook replacements and registration recommendations in a temporary suggestion file.

## Review the result

Remove or rewrite passages that mainly explain:

- why an algorithm was chosen;
- what changed between schema versions;
- how visuals or sounds were tuned;
- which agent or development step produced a feature.

Make sure future features carry a design-direction label rather than current-behavior wording.

Verify that the homepage represents the full mod direction, every Wiki page has an incoming link, all internal links resolve, available and planned behavior are distinguishable, every named current part exists, and exact pressure or range numbers match code or tests. For handbook entries, also verify that every linked page ID names a real item or block variant and that the text contains no broad mechanic explanation.

Run link checks, spelling checks, `git diff --check`, and documentation-safe verification commands. Do not run build or installation steps when the maintainer restricted the main project to read-only work.
