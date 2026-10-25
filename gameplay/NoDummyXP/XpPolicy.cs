namespace AdventureRpg.NoDummyXp;

// Pure policy: no timers, proximity checks, persistent state or saved skill edits.
public sealed class XpPolicy
{
    public bool Strict { get; }
    private readonly Dictionary<int, (bool Dummy, bool Other)> votes = new();
    public XpPolicy(bool strict = false) { Strict = strict; }
    public void Record(int skill, bool dummy)
    {
        votes.TryGetValue(skill, out var prior);
        votes[skill] = (prior.Dummy || dummy, prior.Other || !dummy);
    }
    public bool Blocks(int skill)
    {
        if (Strict) return true;
        votes.TryGetValue(skill, out var exact);
        votes.TryGetValue(-1, out var any);
        return (exact.Dummy || any.Dummy) && !(exact.Other || any.Other);
    }
}
