using System.Globalization;
using UnityEngine;

/// <summary>The seven ranks, worst to best (a higher number is better). P is PERFECT.</summary>
public enum LevelRank
{
    F = 0,
    D = 1,
    C = 2,
    B = 3,
    A = 4,
    S = 5,
    P = 6
}

/// <summary>
/// The rank rules, as plain functions with no scene needed. RankSettings holds the numbers; this decides what they mean.
///
///   Room rank   Compute(...)   P = every gem + fast + high score, S = fast + high score (gems missed), A = fast + medium score,
///                              then B / C / D / F by (time tier + score tier).
///   Overall     Overall(...)   the average of the room ranks; P only if every room is P; capped when a room was never cleared.
/// </summary>
public static class RankCalc
{
    /// <summary>Saved data uses this for "no rank yet".</summary>
    public const int NoRank = -1;

    public const int TopTier = 3;

    // ------------------------------------------------------------ tiers

    /// <summary>3 = fast, 2 = good, 1 = slow, 0 = too slow.</summary>
    public static int TimeTier(RankTargets t, float seconds)
    {
        if (seconds <= t.fastSeconds) return 3;
        if (seconds <= t.mediumSeconds) return 2;
        if (seconds <= t.slowSeconds) return 1;
        return 0;
    }

    /// <summary>3 = high, 2 = medium, 1 = low, 0 = none.</summary>
    public static int ScoreTier(RankTargets t, int score)
    {
        if (score >= t.highScore) return 3;
        if (score >= t.mediumScore) return 2;
        if (score >= t.lowScore) return 1;
        return 0;
    }

    public static string TimeTierName(int tier)
    {
        switch (tier)
        {
            case 3: return "FAST";
            case 2: return "GOOD";
            case 1: return "SLOW";
            default: return "TOO SLOW";
        }
    }

    public static string ScoreTierName(int tier)
    {
        switch (tier)
        {
            case 3: return "HIGH";
            case 2: return "MEDIUM";
            case 1: return "LOW";
            default: return "NONE";
        }
    }

    // ------------------------------------------------------------ one room

    /// <summary>
    /// The rank of one room. total is how many gems the room has and collected how many were taken (total 0 = no gems, so "all of them" is true).
    /// </summary>
    public static LevelRank Compute(RankSettings settings, RankTargets targets, float seconds, int score, int collected, int total,
                                    out int timeTier, out int scoreTier)
    {
        timeTier = TimeTier(targets, seconds);
        scoreTier = ScoreTier(targets, score);

        bool allGems = total <= 0 || collected >= total;
        float share = total <= 0 ? 1f : Mathf.Clamp01(collected / (float)total);
        bool fast = timeTier >= TopTier;

        if (fast && scoreTier >= TopTier)
        {
            if (allGems) return LevelRank.P;
            if (share >= settings.sMinCollectFraction) return LevelRank.S;
            return LevelRank.A;   // too few gems for an S, but still fast with a high score
        }
        if (fast && scoreTier >= 2) return LevelRank.A;

        int points = timeTier + scoreTier;
        if (points >= settings.bPoints) return LevelRank.B;
        if (points >= settings.cPoints) return LevelRank.C;
        if (points >= settings.dPoints) return LevelRank.D;
        return LevelRank.F;
    }

    // ------------------------------------------------------------ the whole run

    /// <summary>
    /// One rank for the team from the rank of every room. roomRanks holds each room's rank as a number, or NoRank (-1) for a room that was never cleared.
    ///   - the average of the room ranks (a missing room counts as F), rounded to the nearest rank, never above S...
    ///   - ...unless EVERY room is P, which is the only way to a P overall;
    ///   - if any room is missing, the result is capped at settings.capRankWhenMissing (B by default): P, S and A are impossible.
    /// missing is how many rooms were never cleared; capped is true when the cap actually lowered the rank.
    /// </summary>
    public static LevelRank Overall(RankSettings settings, int[] roomRanks, out int missing, out bool capped)
    {
        missing = 0;
        capped = false;
        if (roomRanks == null || roomRanks.Length == 0) return LevelRank.F;

        int sum = 0;
        bool allPerfect = true;
        for (int i = 0; i < roomRanks.Length; i++)
        {
            int r = roomRanks[i];
            if (r < 0)
            {
                missing++;
                allPerfect = false;
                continue;
            }
            sum += r;
            if (r != (int)LevelRank.P) allPerfect = false;
        }

        float mean = sum / (float)roomRanks.Length;
        LevelRank rank = allPerfect
            ? LevelRank.P
            : (LevelRank)Mathf.Clamp(Mathf.FloorToInt(mean + 0.5f), 0, (int)LevelRank.S);

        if (missing > 0 && rank > settings.capRankWhenMissing)
        {
            rank = settings.capRankWhenMissing;
            capped = true;
        }
        return rank;
    }

    // ------------------------------------------------------------ names, colours, formatting

    public static string Letter(int rank)
    {
        return rank < 0 ? "-" : ((LevelRank)Mathf.Clamp(rank, 0, 6)).ToString();
    }

    public static string Word(int rank)
    {
        switch (rank)
        {
            case 6: return "PERFECT";
            case 5: return "WONDERFUL";
            case 4: return "GREAT";
            case 3: return "GOOD";
            case 2: return "OKAY";
            case 1: return "POOR";
            case 0: return "FAILED";
            default: return "NOT CLEARED";
        }
    }

    public static Color ColorOf(int rank)
    {
        switch (rank)
        {
            case 6: return new Color(1.00f, 0.84f, 0.28f, 1f);   // gold
            case 5: return new Color(0.96f, 0.52f, 1.00f, 1f);   // pink-violet
            case 4: return new Color(0.35f, 0.88f, 0.55f, 1f);   // green
            case 3: return new Color(0.34f, 0.64f, 1.00f, 1f);   // blue
            case 2: return new Color(1.00f, 0.66f, 0.24f, 1f);   // orange
            case 1: return new Color(0.82f, 0.56f, 0.42f, 1f);   // rust
            case 0: return new Color(1.00f, 0.36f, 0.36f, 1f);   // red
            default: return new Color(0.62f, 0.69f, 0.82f, 0.8f);   // none yet: dim
        }
    }

    public static string Hex(int rank)
    {
        return ColorUtility.ToHtmlStringRGB(ColorOf(rank));
    }

    /// <summary>The rank letter with its colour as TextMeshPro rich text: &lt;color=#RRGGBB&gt;A&lt;/color&gt;.</summary>
    public static string Rich(int rank)
    {
        return "<color=#" + Hex(rank) + ">" + Letter(rank) + "</color>";
    }

    /// <summary>1:05.3 (minutes, seconds, tenths).</summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) return "-";
        int tenths = Mathf.RoundToInt(seconds * 10f);
        int minutes = tenths / 600;
        int rest = tenths % 600;
        return minutes.ToString(CultureInfo.InvariantCulture) + ":" +
               (rest / 10).ToString("00", CultureInfo.InvariantCulture) + "." +
               (rest % 10).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>12,300</summary>
    public static string FormatScore(int score)
    {
        return score.ToString("N0", CultureInfo.InvariantCulture);
    }
}
