using UnityEngine;

namespace AdventureRpg.FirecastLifecycle;

internal sealed class FirecastPolicy
{
    internal const string EffectName = "fx_firecast_WarfareWizardry_TW";
    private readonly List<Change> changes;
    private bool applied;

    private FirecastPolicy(List<Change> changes) => this.changes = changes;

    internal static FirecastPolicy Prepare(GameObject effect, GameObject staff, GameObject monsterWeapon)
    {
        // Validate the complete narrow asset contract before changing any row.
        if (!effect || effect.name != EffectName)
            throw new InvalidOperationException("Reviewed firecast prefab is missing.");
        var view = effect.GetComponent<ZNetView>();
        var lifetime = effect.GetComponent<TimedDestruction>();
        if (!view || view.m_persistent || !effect.GetComponent<ZSyncTransform>() || !lifetime ||
            lifetime.m_timeout != 2f || !lifetime.m_triggerOnAwake || !lifetime.m_forceTakeOwnershipAndDestroy)
            throw new InvalidOperationException("Firecast network/lifetime contract changed; no effect data was modified.");
        var staffData = Data(staff, "StaffPlains_TW");
        var monsterData = Data(monsterWeapon, "TMMonsterItemGoblinMage_FireballAttack");
        var changes = new List<Change>();
        Add(staffData.m_attack.m_startEffect, effect, new[] { "RightHand", "LeftHand" }, changes);
        Add(monsterData.m_startEffect, effect, new[] { "R_Hand" }, changes);
        return new FirecastPolicy(changes);
    }

    private static ItemDrop.ItemData.SharedData Data(GameObject prefab, string name)
    {
        if (!prefab || prefab.name != name)
            throw new InvalidOperationException("Reviewed firecast consumer is missing: " + name);
        var data = prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
        return data ?? throw new InvalidOperationException("Reviewed ItemDrop is missing: " + name);
    }

    private static void Add(EffectList list, GameObject effect, string[] bones, List<Change> changes)
    {
        var rows = list?.m_effectPrefabs?.Where(row => row != null && row.m_prefab == effect).ToArray();
        if (rows == null || rows.Length != bones.Length ||
            !rows.Select(row => row.m_childTransform).OrderBy(x => x).SequenceEqual(bones.OrderBy(x => x)))
            throw new InvalidOperationException("Reviewed firecast attachment rows changed; nothing was modified.");
        foreach (var row in rows)
        {
            if (!row.m_enabled || row.m_variant != 0 || row.m_inheritParentRotation || row.m_inheritParentScale ||
                row.m_multiplyParentVisualScale || row.m_randomRotation || row.m_scale || (!row.m_attach && !row.m_follow))
                throw new InvalidOperationException("Reviewed firecast effect settings changed; nothing was modified.");
            changes.Add(new Change(row));
        }
    }

    internal int Apply()
    {
        if (applied) return 0;
        if (changes.Any(change => change.Row.m_attach != change.Attach || change.Row.m_follow != change.Follow))
            throw new InvalidOperationException("Firecast effect settings changed after preflight.");
        int count = 0;
        foreach (var change in changes)
        {
            if (!change.Attach && change.Follow) continue;
            change.Row.m_attach = false;
            change.Row.m_follow = true;
            change.Changed = true;
            count++;
        }
        applied = true;
        return count;
    }

    internal void Restore()
    {
        foreach (var change in changes)
            if (change.Changed && !change.Row.m_attach && change.Row.m_follow)
            {
                change.Row.m_attach = change.Attach;
                change.Row.m_follow = change.Follow;
            }
        applied = false;
    }

    private sealed class Change
    {
        internal readonly EffectList.EffectData Row;
        internal readonly bool Attach, Follow;
        internal bool Changed;
        internal Change(EffectList.EffectData row) { Row = row; Attach = row.m_attach; Follow = row.m_follow; }
    }
}
