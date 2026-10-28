using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

// Holds every setting. Menus change the PENDING values; the Apply button calls Apply(),
// which copies pending -> applied, tells everything to update, and schedules a save.
// The save is written SaveDelaySeconds after the LAST Apply, so if a setting crashes the
// game right away, the bad value never reaches the file and the next boot is safe.
public static class GameSettings
{
    public const int MinRes = 240;
    public const int MaxRes = 16384; // "16k"

    public static float SaveDelaySeconds = 10f;
    const string FileName = "settings.txt"; // lives in Application.persistentDataPath

    // ---------- APPLIED (what the game is actually using) ----------
    public static bool MusicOn { get; private set; } = true;
    public static int TrackMode { get; private set; }   // 0 = random, 1+ = that track number
    public static bool SoundOn { get; private set; } = true;
    public static int ResWidth { get; private set; }
    public static int ResHeight { get; private set; }

    // ---------- PENDING (what the menus are showing, not applied yet) ----------
    public static bool PendingMusicOn = true;
    public static int PendingTrackMode;
    public static bool PendingSoundOn = true;
    public static int PendingResWidth;
    public static int PendingResHeight;

    // Handy for sounds that don't use an AudioSource on a SoundSwitch object
    // (for example AudioSource.PlayClipAtPoint): multiply your volume by this.
    public static float SoundVolume { get { return SoundOn ? 1f : 0f; } }

    // ---------- EVENTS ----------
    public static event Action BeforeApply; // menus commit any half-typed text here
    public static event Action OnApplied;   // music/sound/etc. react to new settings here
    public static event Action OnSaved;     // fired after the file is written

    static SettingsRunner runner;
    static Coroutine saveRoutine;

    static string FilePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }

    public static int ClampRes(int value)
    {
        return Mathf.Clamp(value, MinRes, MaxRes);
    }

    // Runs automatically before the first scene loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        // Clear leftovers (matters if "Enter Play Mode" has domain reload turned off)
        BeforeApply = null;
        OnApplied = null;
        OnSaved = null;
        runner = null;
        saveRoutine = null;

        // Defaults
        MusicOn = true;
        TrackMode = 0;
        SoundOn = true;
        ResWidth = ClampRes(Screen.width);
        ResHeight = ClampRes(Screen.height);

        bool loaded = Load();

        PendingMusicOn = MusicOn;
        PendingTrackMode = TrackMode;
        PendingSoundOn = SoundOn;
        PendingResWidth = ResWidth;
        PendingResHeight = ResHeight;

        if (loaded) ApplyResolution();
    }

    // ---------- APPLY ----------
    public static void Apply()
    {
        if (BeforeApply != null) BeforeApply();

        bool resolutionChanged = PendingResWidth != ResWidth || PendingResHeight != ResHeight;

        MusicOn = PendingMusicOn;
        TrackMode = Mathf.Max(0, PendingTrackMode);
        SoundOn = PendingSoundOn;
        ResWidth = ClampRes(PendingResWidth);
        ResHeight = ClampRes(PendingResHeight);

        if (resolutionChanged) ApplyResolution();

        if (OnApplied != null) OnApplied();
        ScheduleSave();
    }

    static void ApplyResolution()
    {
        Screen.SetResolution(ResWidth, ResHeight, Screen.fullScreenMode);
    }

    // ---------- SAVE / LOAD ----------
    static void ScheduleSave()
    {
        if (runner == null)
        {
            GameObject go = new GameObject("[GameSettingsRunner]");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<SettingsRunner>();
        }

        // Applying again restarts the countdown, so the file only ever holds settings
        // that survived a full delay without the game dying.
        if (saveRoutine != null) runner.StopCoroutine(saveRoutine);
        saveRoutine = runner.StartCoroutine(SaveAfterDelay());
    }

    static IEnumerator SaveAfterDelay()
    {
        yield return new WaitForSecondsRealtime(SaveDelaySeconds);
        SaveNow();
    }

    public static void SaveNow()
    {
        saveRoutine = null;
        try
        {
            string text =
                "MusicOn=" + (MusicOn ? 1 : 0) + "\n" +
                "TrackMode=" + TrackMode + "\n" +
                "SoundOn=" + (SoundOn ? 1 : 0) + "\n" +
                "ResWidth=" + ResWidth + "\n" +
                "ResHeight=" + ResHeight + "\n";

            // Write to a temp file first so a crash mid-write can't leave a half-written file.
            string path = FilePath;
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);

            Debug.Log("[Settings] Saved to " + path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Settings] Couldn't save settings: " + e.Message);
            return;
        }

        if (OnSaved != null) OnSaved();
    }

    // Returns true if a settings file was found and read. Bad lines are skipped.
    static bool Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return false;

            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                int n;
                if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;

                switch (key)
                {
                    case "MusicOn": MusicOn = n != 0; break;
                    case "TrackMode": TrackMode = Mathf.Max(0, n); break;
                    case "SoundOn": SoundOn = n != 0; break;
                    case "ResWidth": ResWidth = ClampRes(n); break;
                    case "ResHeight": ResHeight = ClampRes(n); break;
                }
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Settings] Couldn't read settings file, using defaults: " + e.Message);
            return false;
        }
    }
}

// Invisible object that exists only so the delayed save has something to run its timer on.
public class SettingsRunner : MonoBehaviour { }
