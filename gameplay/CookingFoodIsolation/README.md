# Cooking Food Isolation 1.0.0

Corrects Cooking1.2.5 food shared-data lifetime. Every cooking-marked food item gets
an isolated SharedData definition. Each Load starts from its current authored
prefab baseline, then runs Cooking's existing metadata parsing and four skill
multiplier formulas exactly once. Inherited clones also start from their baseline,
so the original skip-inherited-multiplier gate is replaced. Repeated loads cannot
compound stats, and another item cannot contaminate the canonical food template.

Unload restores the exact baseline and removes the retired shared definition from
Cooking's active registry. The registry receives the parsed cooking skill after
Load, preserving Cooking's live factor-change behavior. Baseline maps have weak
keys. All other copied shared fields, item identity/stack/quality/custom metadata,
perfect-food flag, recipes, skill progression and configured factors are retained.

No arbitrary food-stat cap or balance nerf. No new configuration, RPC, polling,
tooltip/damage hook or save edits. Requires reviewed Cooking1.2.5 and matching
adapter versions on the server and clients. Install with clients/server closed.
Existing items are rebuilt from prefab plus their saved cooking metadata on the
next normal load; Valheim recalculates consumed food values normally afterwards.
Shared food stats are not stored in item saves. Live food/multiplayer behavior is
not yet tested.
