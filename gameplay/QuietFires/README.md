# Quiet Fires 1.0.0

Client-only fire-loop mute for vanilla Hearth, hanging/standing braziers and
BoneAppetit Smokeless Hearth / Smokeless Hanging Brazier:
`hearth`, `piece_brazierceiling01`, `piece_brazierfloor01`,
`piece_brazierfloor02`, `rk_hearth`, `rk_brazier`.

One `Fireplace.Awake` prefix mutes looping AudioSources in each selected
instance, including inactive flame states. Native ZSFX volume changes and
fire-state toggles retain the mute flag. Ordinary fire pits, bonfires,
torches, unrelated mod pieces and nonlooping sources are unchanged. Construction,
fuel and destruction effects use separate native effects and remain audible.
Heat, comfort, smoke, lighting, fuel, cooking, damage and saves are unchanged.

No configuration, network requirement, RPC, custom update loop, inventory scan,
gameplay-field edits or save writes. Install with the client closed. Existing
pieces become quiet when loaded normally, including after portals. Removing the
mod restores the sounds on the next normal load. A dedicated server does not
need this mod. Offline tests cover the production prefix, exact prefab scope,
inactive sources, native audio API and pinned asset loops; live audio is untested.
