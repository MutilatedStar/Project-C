using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One finished try at a room, a stage or a whole run. Saved so the Level Select cards and the results panel can show prior scores.</summary>
[Serializable]
public class RunAttempt
{
    /// <summary>The rank as a number (0 = F ... 6 = P).</summary>
    public int rank;
    /// <summary>The team's score: every player added together.</summary>
    public int score;
    public int p1;
    public int p2;
    public float seconds;
    public int collected;
    public int total;
    public string when = "";

    public RunAttempt Copy()
    {
        return (RunAttempt)MemberwiseClone();
    }
}

/// <summary>Everything saved about one room / stage / run.</summary>
[Serializable]
public class LevelRecord
{
    public string id = "";
    /// <summary>The best rank ever earned (RankCalc.NoRank = -1 if never cleared).</summary>
    public int bestRank = RankCalc.NoRank;
    public int bestScore;
    /// <summary>The fastest clear in seconds (-1 = never).</summary>
    public float bestTime = -1f;
    public int plays;
    /// <summary>The single best try (best rank, then score, then time): the overall rank adds up THESE.</summary>
    public RunAttempt bestRun;
    /// <summary>The last few tries, oldest first.</summary>
    public List<RunAttempt> history = new List<RunAttempt>();

    public bool Cleared { get { return bestRank >= 0 && bestRun != null; } }
}

/// <summary>
/// Saves the best result of every room, stage and run (and the last few tries) on the player's computer (PlayerPrefs), so the Level Select
/// cards and the results screens can show prior scores, and "NEW BEST" can be told.
///
/// Ids:  a Level Select stage is saved under its own id (stage-3);  a dungeon room under "dungeon/room-3";  the whole dungeon run under
/// "dungeon";  the whole Level Select set under "all-stages".
/// Wipe everything with the menu  Tools > Stages > Clear Saved Ranks  (or LevelRecords.ClearAll()).
/// </summary>
public static class LevelRecords
{
    public const string DungeonId = "dungeon";
    public const string StagesOverallId = "all-stages";
    public const int HistoryLength = 8;

    private const string PrefsKey = "halflight.ranks.v1";

    [Serializable]
    private class SaveData
    {
        public List<LevelRecord> levels = new List<LevelRecord>();
    }

    /// <summary>What a Submit changed: the bests from BEFORE this try, and which of them this try beat.</summary>
    public struct Outcome
    {
        public bool hadRecord;
        public int previousBestRank;
        public int previousBestScore;
        public float previousBestTime;
        public bool newBestRank;
        public bool newBestScore;
        public bool newBestTime;
        public LevelRecord record;

        public bool AnyNewBest { get { return newBestRank || newBestScore || newBestTime; } }
    }

    private static SaveData data;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        data = null;
    }

    public static string DungeonRoomId(int room)
    {
        return DungeonId + "/room-" + room;
    }

    // ------------------------------------------------------------ reading

    /// <summary>The saved record, or null if nothing was ever saved under this id.</summary>
    public static LevelRecord Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Load();
        for (int i = 0; i < data.levels.Count; i++)
        {
            if (data.levels[i] != null && data.levels[i].id == id) return data.levels[i];
        }
        return null;
    }

    /// <summary>Every id that has a saved record (used by the Level Select log to show where a rank actually went).</summary>
    public static List<string> SavedIds()
    {
        Load();
        List<string> ids = new List<string>();
        for (int i = 0; i < data.levels.Count; i++)
        {
            if (data.levels[i] != null && !string.IsNullOrEmpty(data.levels[i].id)) ids.Add(data.levels[i].id);
        }
        return ids;
    }

    /// <summary>The best rank saved under this id, or RankCalc.NoRank.</summary>
    public static int BestRank(string id)
    {
        LevelRecord r = Get(id);
        return r != null && r.Cleared ? r.bestRank : RankCalc.NoRank;
    }

    // ------------------------------------------------------------ writing

    /// <summary>Saves one finished try and returns what it beat. The try must have a rank 0 to 6.</summary>
    public static Outcome Submit(string id, RunAttempt attempt)
    {
        Outcome outcome = new Outcome();
        outcome.previousBestRank = RankCalc.NoRank;
        outcome.previousBestTime = -1f;
        if (string.IsNullOrEmpty(id) || attempt == null) return outcome;

        Load();
        LevelRecord record = Get(id);
        bool isNew = record == null;
        if (isNew)
        {
            record = new LevelRecord();
            record.id = id;
            data.levels.Add(record);
        }

        outcome.hadRecord = !isNew && record.Cleared;
        outcome.previousBestRank = record.bestRank;
        outcome.previousBestScore = record.bestScore;
        outcome.previousBestTime = record.bestTime;

        RunAttempt stored = attempt.Copy();
        if (string.IsNullOrEmpty(stored.when)) stored.when = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        record.plays++;

        outcome.newBestRank = stored.rank > record.bestRank;
        if (outcome.newBestRank) record.bestRank = stored.rank;

        outcome.newBestScore = stored.score > record.bestScore;
        if (outcome.newBestScore) record.bestScore = stored.score;

        outcome.newBestTime = record.bestTime < 0f || stored.seconds < record.bestTime;
        if (outcome.newBestTime) record.bestTime = stored.seconds;

        // A first clear sets every best; only call it "new" when there was something to beat.
        if (!outcome.hadRecord)
        {
            outcome.newBestRank = false;
            outcome.newBestScore = false;
            outcome.newBestTime = false;
        }

        if (record.bestRun == null || IsBetter(stored, record.bestRun)) record.bestRun = stored.Copy();

        record.history.Add(stored);
        while (record.history.Count > HistoryLength) record.history.RemoveAt(0);

        Save();
        outcome.record = record;
        return outcome;
    }

    /// <summary>
    /// Makes the bests saved under toId at least as good as the bests under fromId (a dungeon room that was cleared also counts for its Level Select
    /// card). Only the bests move, never the play count or the history, so calling it again changes nothing. True if anything changed.
    /// </summary>
    public static bool RaiseBests(string toId, string fromId)
    {
        if (string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(fromId) || toId == fromId) return false;

        LevelRecord from = Get(fromId);
        if (from == null || !from.Cleared) return false;

        LevelRecord to = Get(toId);
        bool changed = false;
        if (to == null)
        {
            to = new LevelRecord();
            to.id = toId;
            data.levels.Add(to);
            changed = true;
        }

        if (from.bestRank > to.bestRank) { to.bestRank = from.bestRank; changed = true; }
        if (from.bestScore > to.bestScore) { to.bestScore = from.bestScore; changed = true; }
        if (from.bestTime >= 0f && (to.bestTime < 0f || from.bestTime < to.bestTime)) { to.bestTime = from.bestTime; changed = true; }
        if (to.bestRun == null || IsBetter(from.bestRun, to.bestRun)) { to.bestRun = from.bestRun.Copy(); changed = true; }
        if (to.plays < 1) { to.plays = 1; changed = true; }

        if (changed) Save();
        return changed;
    }

    /// <summary>Forgets every saved rank, score and time.</summary>
    public static void ClearAll()
    {
        data = new SaveData();
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------ internals

    // Best rank first, then the higher score, then the faster time.
    private static bool IsBetter(RunAttempt a, RunAttempt b)
    {
        if (a.rank != b.rank) return a.rank > b.rank;
        if (a.score != b.score) return a.score > b.score;
        return a.seconds < b.seconds;
    }

    private static void Load()
    {
        if (data != null) return;
        data = new SaveData();

        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            SaveData loaded = JsonUtility.FromJson<SaveData>(json);
            if (loaded != null && loaded.levels != null) data = loaded;
        }
        catch (Exception e)
        {
            Debug.LogWarning("LevelRecords: the saved ranks couldn't be read, starting empty. " + e.Message);
        }
    }

    private static void Save()
    {
        try
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning("LevelRecords: the ranks couldn't be saved. " + e.Message);
        }
    }
}
