using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomComparisons.Engine;

/// <summary>
/// LiveSplit identifies comparisons purely by name, so a custom comparison that
/// reuses a name would silently shadow or break another one.
/// </summary>
public static class ComparisonNames
{
    /// <summary>
    /// Names LiveSplit reserves or ships, whether or not the user has them switched on.
    /// "[Race]" prefixes are used by race integrations.
    /// </summary>
    private static readonly HashSet<string> reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Personal Best",
        "None",
        "Best Segments",
        "Best Split Times",
        "Average Segments",
        "Median Segments",
        "Worst Segments",
        "Balanced PB",
        "Latest Run",
        "Golf HCP",
    };

    public static IReadOnlyCollection<string> Reserved => reserved;

    public const int MaxLength = 64;

    /// <summary>
    /// Returns null if <paramref name="name"/> can be used, otherwise a message for the user.
    /// </summary>
    /// <param name="takenNames">Names already used by other comparisons (exclude the one being renamed).</param>
    public static string Validate(string name, IEnumerable<string> takenNames)
    {
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return "Give the comparison a name.";
        }

        if (trimmed.Length > MaxLength)
        {
            return $"Keep the name under {MaxLength} characters.";
        }

        if (trimmed.StartsWith("[Race]", StringComparison.OrdinalIgnoreCase))
        {
            return "Names starting with [Race] are used by LiveSplit's race features.";
        }

        if (reserved.Contains(trimmed))
        {
            return $"'{trimmed}' is one of LiveSplit's own comparisons. Pick another name.";
        }

        // Case-insensitive on purpose: two comparisons differing only in case are
        // indistinguishable in LiveSplit's menus.
        if ((takenNames ?? Enumerable.Empty<string>()).Any(t => string.Equals(t?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"There's already a comparison called '{trimmed}'.";
        }

        return null;
    }
}
