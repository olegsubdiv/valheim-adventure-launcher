using System.Text.RegularExpressions;

namespace AdventureLauncher.Core;

internal static class ConfigMerge
{
    internal static string Apply(string fresh, string old, IReadOnlyCollection<string> preservedKeys)
    {
        var wanted = new HashSet<string>(preservedKeys, StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return fresh;
        var oldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var line in old.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) section = trimmed[1..^1];
            else if (!trimmed.StartsWith('#') && !trimmed.StartsWith(';') && line.IndexOf('=') is var split && split > 0)
            {
                var key = section + "/" + line[..split].Trim();
                if (wanted.Contains(key)) oldValues[key] = line[(split + 1)..].Trim();
            }
        }
        section = "";
        var result = new List<string>();
        var newline = fresh.Contains("\r\n") ? "\r\n" : "\n";
        foreach (var line in fresh.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) section = trimmed[1..^1];
            if (!trimmed.StartsWith('#') && !trimmed.StartsWith(';') && line.IndexOf('=') is var split && split > 0 &&
                oldValues.TryGetValue(section + "/" + line[..split].Trim(), out var value))
                result.Add(line[..(split + 1)] + " " + value);
            else result.Add(line);
        }
        return string.Join(newline, result);
    }
}
