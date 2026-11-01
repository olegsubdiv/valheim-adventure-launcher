using BepInEx.Configuration;
using UnityEngine;

namespace AdventureRpg.SharedControls;

internal enum MigrationResult { Pending, Applied, AlreadyApplied }

internal static class ControlMigration
{
    internal const string EpicSection = "8 - Abilities";
    internal static readonly string[] SetKeys = { "Overwhelming Launch Hotkey", "Earthshaker Hotkey", "Eitr Infusion Hotkey", "Shield Throw Hotkey" };
    internal static readonly string[] KeyboardMovement = { "Forward", "Backward", "Left", "Right" };
    internal static readonly string[] GamepadMovement = { "JoyLStickUp", "JoyLStickDown", "JoyLStickLeft", "JoyLStickRight" };

    internal static MigrationResult Apply(ConfigFile epic, ConfigFile auto, ConfigFile own, ConfigEntry<int> marker)
    {
        if (marker.Value >= 1) return MigrationResult.AlreadyApplied;
        if (marker.Value != 0) throw new InvalidOperationException("Invalid Shared Controls migration marker.");
        var changes = new List<Change>();
        bool Need<T>(ConfigFile config, string section, string key, T wanted)
        {
            // TryGetEntry throws on a wrong registered type: do not silently
            // manufacture entries or alter orphaned text before the owner binds it.
            if (!config.TryGetEntry<T>(section, key, out var entry)) return false;
            changes.Add(new Change(entry, entry.BoxedValue, wanted!));
            return true;
        }
        bool ready = true;
        foreach (var key in SetKeys) ready &= Need(epic, EpicSection, key, KeyCode.G);
        ready &= Need(epic, EpicSection, "Spirit Animal Hotkey", KeyCode.Z);
        ready &= Need(epic, EpicSection, "Ability Hotkey 1", "u");
        ready &= Need(auto, "Keyboard", "Toggle auto-attack key", new KeyboardShortcut(KeyCode.F));
        ready &= Need(auto, "General", "Cancel on weapon change", true);
        foreach (var key in KeyboardMovement) ready &= Need(auto, "Keyboard pause buttons", key, false);
        foreach (var key in GamepadMovement) ready &= Need(auto, "Gamepad pause buttons", key, false);
        if (!ready) return MigrationResult.Pending;

        var files = new[] { epic, auto, own };
        var autoSave = files.Select(x => x.SaveOnConfigSet).ToArray();
        foreach (var config in files) config.SaveOnConfigSet = false;
        try
        {
            foreach (var change in changes) change.Entry.BoxedValue = change.Wanted;
            epic.Save();
            auto.Save();
            // This is the commit record. A future startup may preserve personal
            // remaps only after both owner configuration saves completed.
            marker.Value = 1;
            own.Save();
            return MigrationResult.Applied;
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var change in changes.AsEnumerable().Reverse())
            {
                try
                {
                    if (Equals(change.Entry.BoxedValue, change.Wanted)) change.Entry.BoxedValue = change.Before;
                    else if (!Equals(change.Entry.BoxedValue, change.Before))
                        rollbackErrors.Add(new InvalidOperationException("Shared Controls preserved a concurrent edit during rollback: " + change.Entry.Definition));
                }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            }
            try { if (marker.Value == 1) marker.Value = 0; }
            catch (Exception ex) { rollbackErrors.Add(ex); }
            // Keep unrelated/current owner values; never restore a whole old CFG
            // over an edit made by another owner/event handler.
            foreach (var config in files)
                try { config.Save(); } catch (Exception ex) { rollbackErrors.Add(ex); }
            if (rollbackErrors.Count != 0)
                throw new AggregateException("Shared Controls migration failed; rollback needs attention. The migration was not committed.", new[] { failure }.Concat(rollbackErrors));
            throw new InvalidOperationException("Shared Controls migration failed and its values were restored; marker remains 0.", failure);
        }
        finally
        {
            for (int i = 0; i < files.Length; i++) files[i].SaveOnConfigSet = autoSave[i];
        }
    }

    private sealed class Change
    {
        internal readonly ConfigEntryBase Entry;
        internal readonly object Before;
        internal readonly object Wanted;
        internal Change(ConfigEntryBase entry, object before, object wanted) { Entry = entry; Before = before; Wanted = wanted; }
    }
}
