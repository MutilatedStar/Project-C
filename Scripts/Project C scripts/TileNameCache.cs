using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers the "letters and digits only, lower case" form of tile and sprite names, so the floor scripts (FloorManager: lava, water, spikes;
/// FloorController: the healing fountain and its Console tile log) stop rebuilding the same strings every frame for every tile under every player.
///
/// WHY: reading Object.name makes a new string every time, and Normalize used to make a StringBuilder and another string on top of that, several
/// times per tile per player per frame. A tile or sprite asset keeps its name for as long as it exists, so the answer is worked out ONCE per asset
/// and looked up after that (no allocation).
///
/// WHAT IT DOES NOT CHANGE: the rule is the same one both scripts always used (keep letters and digits, lower-case them, ignore everything else).
/// Each script still decides what counts as a match: FloorManager and FloorController do NOT share one matching rule (FloorController also has an
/// "Exact" mode), so only the normalizing is shared here, never the comparing.
///
/// SAFE WITH RUNTIME-MADE TILES: entries are keyed by the asset OBJECT, not by its name. A tile made while playing (FloorController.ChangeTileSprite
/// names its new tile BEFORE it is placed) is a different object, so it gets its own entry. Nothing in the project renames a tile or sprite after it
/// has been in use.
///
/// The key is the object reference on purpose, not GetInstanceID(): newer Unity versions made GetInstanceID() an error (EntityId replaced it), and
/// HazardTileFx already keys its own tile / sprite dictionaries by reference the same way.
/// </summary>
public static class TileNameCache
{
    private struct Entry
    {
        public string normalized;   // letters and digits only, lower case ("" for no name)
        public int trailingNumber;  // the number at the very end of the RAW name ("spike-trap-16px_strip8_2" -> 2), -1 if there is none
    }

    private static readonly Dictionary<Object, Entry> assets = new Dictionary<Object, Entry>();
    private static readonly Dictionary<string, string> wanted = new Dictionary<string, string>();

    // Statics survive between Play sessions when "Reload Domain" is switched off, and would keep references to objects of the last session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        assets.Clear();
        wanted.Clear();
    }

    /// <summary>Capitals, spaces, dashes and underscores are ignored: "Lava-16px_strip12_3" becomes "lava16pxstrip123".</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i])) sb.Append(char.ToLowerInvariant(text[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Normalize for a name typed in the Inspector (Lava Name, Spike Name, Fountain Tile Name...). Remembered by its text, so editing the field
    /// in Play mode just works: a new text is normalized once and the old one is simply never asked for again.
    /// </summary>
    public static string NormalizeWanted(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        string normalized;
        if (!wanted.TryGetValue(raw, out normalized))
        {
            normalized = Normalize(raw);
            wanted[raw] = normalized;
        }
        return normalized;
    }

    /// <summary>The normalized name of a tile asset or a sprite. "" for null (or a destroyed object), exactly like a missing name.</summary>
    public static string NormalizedName(Object asset)
    {
        return asset == null ? "" : Lookup(asset).normalized;
    }

    /// <summary>The number at the very end of a tile's or sprite's name ("spike-trap-16px_strip8_2" -> 2), or -1 when there is none (or no asset).</summary>
    public static int TrailingNumber(Object asset)
    {
        return asset == null ? -1 : Lookup(asset).trailingNumber;
    }

    private static Entry Lookup(Object asset)
    {
        Entry entry;
        if (!assets.TryGetValue(asset, out entry))
        {
            string raw = asset.name;
            entry.normalized = Normalize(raw);
            entry.trailingNumber = ParseTrailingNumber(raw);
            assets[asset] = entry;
        }
        return entry;
    }

    // The number at the very end of a name: "spike-trap-16px_strip8_2" -> 2. Read from the RAW name (the normalized one has lost its underscores,
    // so "strip8_2" would read as 82).
    private static int ParseTrailingNumber(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;

        int end = name.Length;
        int start = end;
        while (start > 0 && char.IsDigit(name[start - 1])) start--;
        if (start == end) return -1;

        int value;
        return int.TryParse(name.Substring(start), out value) ? value : -1;
    }
}
