# Recipe From Item 1.0.0

Client-side discovery extension for the pinned Valheim 1.0.20 collection.

An enabled, currently available crafting recipe may be learned when its output
item has previously been obtained, even if not every ingredient is known yet.
The ordinary all-ingredients discovery route remains available. Obtaining the
finished item bypasses ingredient and station knowledge for recipe discovery.

Only Player.HaveRequirements(Recipe, bool, int, int)'s discovery result can change. Actual crafting,
upgrading, resource consumption, required crafting stations/levels, disabled/seasonal recipe checks,
building pieces and EpicLoot drop limits retain their normal rules. Discovering
one item does not mark its ingredients as discovered or reveal their other recipes.
Items without an enabled recipe do not gain one.
Missing DLC remains unavailable. Revealing a recipe does not build its station or
allow it to be crafted at the wrong station.

The existing character discovery log is used, including items learned before
installing the plugin. The normal inventory/recipe refresh reveals those older
items' recipes too; moving an item after joining is sufficient. No new save fields, RPCs, polling loops or configuration
files are added. Learned recipes are ordinary persistent character knowledge;
removing the plugin does not erase recipes already learned.

Install on clients while the game is closed; a dedicated server copy is unnecessary.
No character saves are edited offline. Build and assembly-contract checks do not
launch Valheim or load saves. Live gameplay is not yet verified.
