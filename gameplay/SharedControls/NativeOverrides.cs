namespace AdventureRpg.SharedControls;

// Owns at most two keyboard overrides in one ZInput registry. No PlayerPrefs,
// PlatformPrefs, ZInput.Save, gamepad actions or gameplay state is written here.
internal sealed class NativeOverrides
{
    private ZInput? input;
    private readonly Dictionary<string, Override> owned = new();
    private bool wantG, wantF;
    private int saves;

    internal void Refresh(ZInput instance, bool useG, bool useF)
    {
        wantG = useG; wantF = useF;
        if (saves != 0) return;
        if (!ReferenceEquals(input, instance)) { Restore(); input = instance; }
        Set("OpenRadial", "<Keyboard>/g", useG);
        Set("GP", "<Keyboard>/f", useF);
    }

    private void Set(string name, string path, bool needed)
    {
        if (owned.TryGetValue(name, out var current))
        {
            if (current.Button.GetActionPath() != "")
            {
                // A fresh native remap belongs to the player, including a remap
                // back to G/F. Do not immediately take it over again.
                owned.Remove(name);
                return;
            }
            if (!needed) { current.Button.Rebind(current.Original); owned.Remove(name); }
            return;
        }
        var button = input?.GetButtonDef(name);
        if (!needed || button == null || !button.Rebindable || button.Source != ZInput.InputSource.KeyboardMouse ||
            !string.Equals(button.GetActionPath(), path, StringComparison.OrdinalIgnoreCase)) return;
        var change = new Override(name, button, button.GetActionPath());
        button.Rebind("");
        owned.Add(name, change);
    }

    internal SaveScope BeforeSave(ZInput instance)
    {
        var scope = new SaveScope(this, ReferenceEquals(input, instance));
        if (!scope.Active) return scope;
        saves++;
        // A nested native Save sees the originals already restored by its outer
        // scope; ownership must survive until that outer scope's finalizer.
        if (saves != 1) return scope;
        try
        {
            foreach (var pair in owned.ToArray())
            {
                var change = pair.Value;
                if (change.Button.GetActionPath() != "") { owned.Remove(pair.Key); continue; }
                scope.Changes.Add(change);
                change.Button.Rebind(change.Original);
            }
            return scope;
        }
        catch
        {
            AfterSave(scope);
            throw;
        }
    }

    internal void AfterSave(SaveScope? scope)
    {
        if (scope == null || !scope.Active || scope.Finished || !ReferenceEquals(scope.Owner, this)) return;
        scope.Finished = true;
        try
        {
            foreach (var change in scope.Changes)
            {
                if (!owned.TryGetValue(change.Name, out var stillOwned) || !ReferenceEquals(change, stillOwned)) continue;
                bool wanted = change.Name == "OpenRadial" ? wantG : wantF;
                if (change.Button.GetActionPath() == change.Original && wanted) change.Button.Rebind("");
                else owned.Remove(change.Name); // preserve a user/other mod's fresh remap
            }
        }
        finally { saves--; }
    }

    internal void Restore()
    {
        foreach (var change in owned.Values)
            if (change.Button.GetActionPath() == "") change.Button.Rebind(change.Original);
        owned.Clear(); input = null;
    }

    internal sealed class SaveScope
    {
        internal readonly NativeOverrides Owner;
        internal readonly bool Active;
        internal readonly List<Override> Changes = new();
        internal bool Finished;
        internal SaveScope(NativeOverrides owner, bool active) { Owner = owner; Active = active; }
    }
    internal sealed class Override
    {
        internal readonly string Name, Original;
        internal readonly ZInput.ButtonDef Button;
        internal Override(string name, ZInput.ButtonDef button, string original) { Name = name; Button = button; Original = original; }
    }
}
