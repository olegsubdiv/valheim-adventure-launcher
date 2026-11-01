# Shared Controls 1.0.0

Client only; no server requirement, RPC, game loop or world/save changes.
Requires the reviewed EpicLoot 0.15.5 and orax.AutoAttack 0.1.0. The distribution
includes the separately verified AutoAttack client at its existing plugin path.

On the first successful startup in each profile, the plugin changes only:

- EpicLoot's four set-action keys to G, Spirit Animal to Z and ability slot 1 to U.
- AutoAttack toggle to F, cancel on weapon change on, and four keyboard plus four
  left-stick movement pause flags off.

This first application intentionally replaces previous values for these settings.
All other settings remain. Afterward personal remaps are preserved, including on
future pack updates. Values are changed through their owner's registered typed
ConfigEntry objects. The late AutoAttack dynamic configuration is awaited via
initialization callbacks, without polling or creating substitute entries.

Local `BepInEx/config/arpg.sharedcontrols.cfg`, `[Migration] AppliedVersion=1`, is
saved LAST, after both owner CFGs are saved. Never include this marker file in a
shared package. Failure leaves marker 0 and attempts to restore only this
migration's values, preserving concurrent edits and reporting rollback errors.
Owner CFGs use their native serializer, which can refresh comments/formatting.

While this profile uses G/F, conflicting native keyboard OpenRadial/GP bindings
are disabled only if still bound to G/F. Other keys, gamepad and emotes remain.
Only overrides this plugin actually owns are restored when it unloads. A scoped
native Save hook temporarily exposes the original native bindings during the
game's own preference serialization, then reapplies the runtime override even if
Save throws. A fresh player/other-mod native remap is preserved. The plugin never
calls native Save or writes PlayerPrefs/registry itself.

Offline production-policy, rollback and native binding fixtures plus pinned
binary API checks are in `SharedControls.Tests`. Live game loading/input remains
to be checked during the next ordinary client startup.
