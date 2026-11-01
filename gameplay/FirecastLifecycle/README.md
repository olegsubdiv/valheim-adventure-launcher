# Firecast Lifecycle 1.0.0

Requires the reviewed Wizardry 1.2.3, on the server and all clients.

The networked `fx_firecast_WarfareWizardry_TW` prefab is attached to a character's
hand by three effect rows: StaffPlains_TW's right/left hands and the goblin mage's
fireball attack. Destroying the parent can destroy its networked child before
the effect's own two-second TimedDestruction calls ZNetScene.Destroy. ZNetView's
native OnDestroy does not remove that child from ZNetScene's instance registry.
Subsequent zone unloading can then throw at the destroyed component's gameObject;
native RemoveObjects resets its ZDO before that access and cannot finish cleanup.

At plugin initialization, this adapter uses Wizardry's own cached asset bundle to
change only these three rows: m_attach=false, m_follow=true. Native EffectList
then uses a ParentConstraint to follow the same named hand without making the
effect a child of the character. Its own native two-second lifetime can complete
independently of its former parent's lifetime. The effect remains networked.

All assets/rows are validated before any change. Already-following rows remain
untouched, and unload restores only values this adapter actually changed that
still match its result. Unexpected versions/contracts fail with a clear error.

The adapter has no Harmony hooks, custom Update loop, polling, new component,
RPC, config, damage changes, direct object destruction, registry cleanup or save
writes. Wizardry's original DLL/bundle, effects, particles, timers, ownership and
weapon statistics are retained. Native following may slightly alter visual
orientation/scale tracking compared with parenting; visual/input/multiplayer
verification remains for ordinary play.

Evidence is the read-only live ObjectUnloadProbe report and the pinned asset and
native-code analysis. Offline regression fixtures cover the parent-destruction
failure mechanism and the exact data change. They do not replace a live Unity
test or prove all sources of base FPS loss have been eliminated. Retain the
temporary personal unload probe until normal play confirms no repeated unload
exceptions. Never suppress the exception or delete world ZDOs as a workaround.
