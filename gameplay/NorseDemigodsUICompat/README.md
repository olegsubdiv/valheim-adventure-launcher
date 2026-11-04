# Norse Demigods UI Compatibility1.0.0

Client-only adapter for the reviewed NorseDemigods1.9.0 and
NorseDemigodsUI Thunderstore package0.11.6. That UI package embeds BepInPlugin
version0.11.5 and assembly0.11.5.0; the adapter checks both the runtime plugin
versions and the exact shipped DLL SHA256 values. Vendor assemblies are unchanged.

The original UI remains responsible for its panels, settings, keys, meters,
localization and normal formulas. This adapter replaces two UI registry entries:
Holy Sun uses the normal/infused radius in the native1.9 order, and Lightning
Torpedo's displayed cost includes the normal unarmed-skill charge reduction.
Three prefixes target UI methods only: Torpedo's cost range no longer displays
the obsolete second charge; Meteor reads its native infused eitr cost; thirteen
stale description tokens are corrected for Phantom Shroud, own-kill effects,
Sneaky, Vortex and Restoration. Restoration shows its base plus the percentage
per energy point consumed; no fixed current-energy damage is invented.

Corrected entries include English and Russian text. Other languages fall back
to corrected English for those thirteen entries, while their other translations
remain native. Personal translations of these specific stale entries are
superseded to keep the1.9 mechanics accurate. Other settings are never written.

No gameplay method, network/RPC, damage, inventory, character/world/save, stat,
configuration or own per-frame Update hooks are introduced. File reads are only
the startup assembly identity checks. Failed startup unpatches/restores its own
UI changes and rethrows; unloading restores only definitions still owned by it.

Offline checks execute all121 original public UI value definitions with native
1.9 config settings, verify all referenced methods/fields/settings and Harmony
targets, exercise the corrected formulas and translation placeholders, and
inspect production IL. Live Harmony/Mono, visual layout and multiplayer have
not been exercised by these tests; no game or save is launched for verification.

Install on clients; the server does not need this adapter. It changes no demigod
damage, costs, energy or balance in the game itself.
