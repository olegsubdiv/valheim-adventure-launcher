# Safe Reclaim 1.0.0

Compatibility correction for Recycle_N_Reclaim 1.4.5 and Blacksmithing 1.3.6.
Requires these exact reviewed versions and matching plugin versions on all peers.

Recycle_N_Reclaim sends rounded zero material yields to Inventory.AddItem. The
native method returns null; Blacksmithing's durability postfix dereferences that
null. This interrupts reclaim before removing the input, after previous materials
may already have been issued. Its UI resets the timer after that call, so the next
frame can repeat the same interrupted request.

The adapter skips zero resource additions only at the two reclaim call sites,
guards Blacksmithing's null-result durability postfix, and resets the reclaim UI
timer before inventory changes. It also rejects stale input references and known
analysis impediments before resources can be issued. Positive additions retain
their original native call, metadata, stacking and backpack integrations.

Use with `PreventZeroResourceYields = Off` and keep the existing recycling rate
and minimum-resource settings. A zero material gives no refund; an all-zero item
is still consumed. Native recipe/station filters, rounding, source removal,
resource-addition failure handling and undo remain in Recycle_N_Reclaim.

No new configuration, RPC, per-frame polling, damage hooks, save editing or
suppression of exceptions. This is a targeted compatibility fix, not a general
inventory transaction engine: unrelated mod exceptions after a positive addition
remain errors, although the UI timer prevents their automatic frame-by-frame
replay. The historical friend's log was unavailable; the concrete null failure
is verified against the installed binaries and an isolated reproduction.

Install the server and clients together with games closed and backups. The
original third-party DLLs are not replaced or redistributed. Live reclaim and
multiplayer use require normal play validation after installation.
