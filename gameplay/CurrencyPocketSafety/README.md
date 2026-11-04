# Adventure RPG — Currency Pocket Safety 1.0.0

Compatibility for the reviewed CurrencyPocket 1.0.15, EpicLoot 0.15.6 and
ExtraSlots 1.2.23 collection. Install on server and clients; Jotunn requires the
same patch version. No new configuration file, update loop, RPC or save hook.

## Wallet behavior

CurrencyPocket keeps the coin balance in the character's existing
`CoinPocket_CoinCount` custom-data entry. Characters have separate balances.
Picked-up coins go to the wallet. Drag existing coins onto the balance to deposit
them. The shared collection disables conversion of valuables: this is a coin
wallet, not automatic selling. The vendor's valuable-item switches are local
configuration entries, so this is a distributed default, not an anti-cheat rule.

Click the extract button, choose an amount and confirm. Coins now appear directly
in the real ordinary inventory, rather than hanging on a temporary cursor stack.
The split dialog still uses CurrencyPocket's preview; one confirmation is limited
to one current coin stack. For larger balances, repeat the operation. A full
ordinary inventory can accept a withdrawal into a compatible coin stack with
enough space; otherwise the balance is unchanged. Reserved equipment, food, ammo
and quick-slot cells are not used for withdrawal. The mod does not impose a coin
stack size of its own.

Wallet money stays with the character on normal death; withdrawn physical coins
follow the game's normal inventory/tombstone rules. Removing the wallet mod does
not delete its custom-data entry, but withdraw first if you want physical coins
available without the mod.

## Narrow corrections

1. The original CurrencyPocket string-removal postfix charges the full price
   after native inventory removal. For mixed funds this can charge twice. Its
   body is skipped and replaced with before/after measurement of carried coins.
   Native removal arguments, return behavior and other inventories are preserved.
   Another hook's already-paid wallet amount is not charged again.
2. The original withdrawal confirmation debits the wallet before creating a drag
   from a non-saved temporary inventory. Cancelling it can lose coins, and a
   cancelled dialog can affect later regular coin splits. Two narrow hooks capture
   the preview's owner and replace only CurrencyPocket's confirmation prefix.
   Confirmed output is placed through the native explicit-cell add method, with
   the requested funds reserved and only an unplaced remainder refunded. Native
   split continuation closes without a temporary coin drag. Its exceptions remain
   visible. No save format, world, character file or save callback is modified.
3. A coin-only provider uses EpicLoot's public inventory-provider API. Its usual
   carried inventory → enchanting storage → providers order is retained. A scoped
   prefix/finalizer prevents early CurrencyPocket debit inside that operation.
   No virtual item list, token/material provider, exact-item callback or save
   handler is registered. The provider unregisters on plugin unload.

TradersExtended 2.0.6 pays physical item references through a different native
overload, then debits the planned wallet remainder itself. This adapter does not
intercept that overload; TradersExtended retains its normal snapshot rollback.

The native explicit-cell withdrawal path avoids AdventureBackpacks' automatic
storage hooks on other AddItem overloads. Empty cells are selected through the
native selector and bounded to ExtraSlots' ordinary inventory geometry.

## Validation and limits

The isolated tests execute the actual CurrencyPocket debit IL to reproduce the
mixed-payment error, test the production payment/provider/withdrawal code, inspect
TradersExtended and EpicLoot call order, and resolve actual client/server APIs.
They cover cancelled/stale dialogs, character changes, full inventories with free
reserved slots, multiple stack-sized withdrawals, partial/refused/exceptional
placement, provider cleanup, and exception scope cleanup. They do not launch the
game, load worlds, or execute the live Unity/Mono/Harmony UI. In-game UI and
multiplayer still need validation during the user's normal play session.
