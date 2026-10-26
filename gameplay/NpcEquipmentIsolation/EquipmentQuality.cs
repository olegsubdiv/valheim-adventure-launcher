using UnityEngine;

namespace AdventureRpg.NpcEquipmentIsolation;

internal static class EquipmentQuality
{
    internal enum Profile { None, Neutral, Renegade, Overlord }

    // Matches RenegadeVikings 1.4.2 EquipmentAdjustment.Awake, including tamed NPCs.
    internal static Profile ForFaction(int faction) => faction switch
    {
        0 or 10 => Profile.Neutral,
        8 => Profile.Overlord,
        2 or 3 or 4 or 5 or 6 or 7 or 9 => Profile.Renegade,
        _ => Profile.None
    };

    internal static void ApplyToInventory(IEnumerable<ItemDrop.ItemData> inventory,
        GameObject[] weapons, Humanoid.ItemSet[] sets, int weaponQuality, int armorQuality)
    {
        foreach (var item in inventory)
        {
            if (item == null || item.m_dropPrefab == null) continue;
            int? quality = null;
            if (weapons != null && weapons.Any(p => ReferenceEquals(p, item.m_dropPrefab)))
                quality = weaponQuality;
            // Original code applies sets after weapons, so set membership takes precedence.
            if (sets != null && sets.Any(s => s != null && s.m_items != null &&
                s.m_items.Any(p => ReferenceEquals(p, item.m_dropPrefab))))
                quality = armorQuality;
            if (quality.HasValue)
                item.m_quality = quality.Value; // ItemData belongs to this NPC's inventory.
        }
    }
}
