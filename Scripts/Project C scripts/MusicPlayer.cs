using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Put this on a ROOT object in the Investigation scene (with or without an AudioSource).
//
// RANDOM mode (Track Mode 0): plays a random track, never the same one twice in a row.
//   Each song slowly fades to silence over its last "Fade Out Seconds", then the next starts.
// PICKED mode (Track Mode 1+): loops that one track ("Repeat Track"), with no fade.
//
// HOLD / RELEASE (used by the Lights Out screen): Hold(seconds) fades the music out and keeps it silent, Release() brings it back
// (if the Music setting is On). The saved Music On/Off setting is never changed. The song that was playing is paused, not dropped, so
// Release carries on from where it stopped (it does not pick a track again) and fades back in over the seconds it is given.
public class MusicPlayer : MonoBehaviour
{
    public static MusicPlayer Instance { get; private set; }

    [Tooltip("Track 1 is Element 0, track 2 is Element 1, and so on. Add as many as you like.")]
    [SerializeField] AudioClip[] tracks;
    [SerializeField, Range(0f, 1f)] float volume = 0.7f;
    [Tooltip("Random mode only: how long the slow fade at the end of each song takes. " +
             "Bigger = the fade starts earlier. 0 = no fade.")]
    [SerializeField] float fadeOutSeconds = 10f;
    [Tooltip("Keep playing when other scenes load. Only one MusicPlayer ever exists; extra copies delete themselves.")]
    [SerializeField] bool persistAcrossScenes = true;

    public int TrackCount { get { return tracks != null ? tracks.Length : 0; } }

    static int lastPlayed = -1; // the track that played most recently (random never picks it again right away)

    AudioSource source;
    int currentTrack = -1;      // index into tracks, -1 = nothing started yet
    bool currentIsPicked;       // true = looping a chosen track, false = random mode
    bool paused;
    bool held;                  // Hold() is in effect: silent until Release()
    Coroutine holdFade;
    Coroutine fadeIn;           // the fade back in after Release(seconds)
    float fadeInGain = 1f;      // 0..1, multiplies the volume while the fade back in runs
    float ignoreEndUntil;       // short grace period right after Play()

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (persistAcrossScenes)
        {
            transform.SetParent(null); // DontDestroyOnLoad only works on root objects
            DontDestroyOnLoad(gameObject);
        }

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D sound
        source.ignoreListenerPause = true; // the pause menu pauses the game's sounds (AudioListener.pause); the music must keep playing so its settings can be heard
    }

    void OnEnable()
    {
        GameSettings.OnApplied += ApplySettings;
    }

    void OnDisable()
    {
        GameSettings.OnApplied -= ApplySettings;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        ApplySettings(); // starts the first random song (or the saved pick) if music is on
    }

    void Update()
    {
        if (Instance != this || paused || currentTrack < 0) return;

        // Song finished on its own -> next random song. (Picked tracks loop, so they never get here.)
        if (!source.isPlaying)
        {
            if (!currentIsPicked && Time.unscaledTime > ignoreEndUntil) PlayRandom();
            return;
        }

        // Slow fade-out toward the end of a random song
        if (!currentIsPicked && fadeOutSeconds > 0f && source.clip != null) source.volume = TrackLevel();
    }

    // The volume the current song should have right now: the music volume, times the fade back in after a Hold, times (in random mode)
    // the slow fade toward the end of the song.
    float TrackLevel()
    {
        float level = volume * fadeInGain;

        if (!currentIsPicked && fadeOutSeconds > 0f && source.clip != null)
        {
            float length = source.clip.length;
            float fade = Mathf.Min(fadeOutSeconds, length);
            float remaining = length - source.time;
            level *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remaining / fade));
        }
        return level;
    }

    /// <summary>True between Hold and Release.</summary>
    public bool IsHeld { get { return held; } }

    /// <summary>Fades the music out over this many seconds (real time) and keeps it silent until Release(). Safe to call twice.</summary>
    public void Hold(float fadeSeconds)
    {
        if (held) return;
        held = true;
        paused = true; // Update() stops advancing songs and touching the volume

        if (holdFade != null) { StopCoroutine(holdFade); holdFade = null; }
        if (fadeIn != null) { StopCoroutine(fadeIn); fadeIn = null; }   // a fade back in that was still running: fade out from wherever it got to
        fadeInGain = 1f;
        if (source != null && source.isPlaying) holdFade = StartCoroutine(FadeAndPause(fadeSeconds));
    }

    /// <summary>
    /// Ends Hold(): if the Music setting is On, the song that was playing carries on from where it stopped and fades back in over this many
    /// seconds (real time; 0 = at once). It is NOT picked again: nothing is chosen from the settings unless no song had started yet.
    /// </summary>
    public void Release(float fadeSeconds = 0f)
    {
        if (!held) return;
        held = false;

        if (holdFade != null) { StopCoroutine(holdFade); holdFade = null; }
        if (fadeIn != null) { StopCoroutine(fadeIn); fadeIn = null; }

        bool fading = fadeSeconds > 0f;
        fadeInGain = fading ? 0f : 1f;
        if (source != null) source.volume = volume * fadeInGain;

        if (currentTrack >= 0 && source != null && source.clip != null && TrackCount > 0 && GameSettings.MusicOn)
        {
            Resume();          // the same song, from where it stopped
        }
        else
        {
            ApplySettings();   // nothing was playing yet (starts a song if the setting is On), or the setting is Off (stays paused)
        }

        if (fading) fadeIn = StartCoroutine(FadeIn(fadeSeconds));
    }

    IEnumerator FadeIn(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            fadeInGain = Mathf.Clamp01(t / seconds);
            if (source != null) source.volume = TrackLevel();
            yield return null;
        }

        fadeInGain = 1f;
        if (source != null) source.volume = TrackLevel();
        fadeIn = null;
    }

    IEnumerator FadeAndPause(float seconds)
    {
        float start = source.volume;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            source.volume = Mathf.Lerp(start, 0f, t / seconds);
            yield return null;
        }
        source.volume = 0f;
        source.Pause();
        holdFade = null;
    }

    // Called at start and every time the Apply button is pressed.
    void ApplySettings()
    {
        if (Instance != this) return;
        if (held) return; // Release() applies the settings when the hold ends

        if (TrackCount == 0)
        {
            Debug.LogWarning("[MusicPlayer] No tracks assigned.", this);
            return;
        }

        if (!GameSettings.MusicOn)
        {
            if (source.isPlaying) source.Pause();
            paused = true;
            return;
        }

        int want = Mathf.Min(GameSettings.TrackMode, TrackCount); // 0 = random

        if (want <= 0)
        {
            // Random: only (re)start if we weren't already in random mode
            if (currentTrack < 0 || currentIsPicked) PlayRandom();
            else Resume();
        }
        else
        {
            int index = want - 1;
            if (currentTrack != index || !currentIsPicked) PlayPicked(index);
            else Resume();
        }
    }

    void Resume()
    {
        if (!paused) return;
        source.UnPause();
        paused = false;
    }

    void PlayRandom()
    {
        int index = PickRandomTrack();
        if (index >= 0) StartTrack(index, false);
    }

    void PlayPicked(int index)
    {
        if (index < 0 || index >= tracks.Length || tracks[index] == null)
        {
            Debug.LogWarning("[MusicPlayer] Track " + (index + 1) + " has no clip assigned.", this);
            return;
        }
        StartTrack(index, true);
    }

    int PickRandomTrack()
    {
        List<int> options = new List<int>();
        for (int i = 0; i < tracks.Length; i++)
            if (tracks[i] != null && i != lastPlayed) options.Add(i);

        // Only one usable track exists, so repeating it can't be avoided
        if (options.Count == 0)
            for (int i = 0; i < tracks.Length; i++)
                if (tracks[i] != null) options.Add(i);

        if (options.Count == 0) return -1;
        return options[Random.Range(0, options.Count)];
    }

    void StartTrack(int index, bool picked)
    {
        source.Stop();
        source.clip = tracks[index];
        source.loop = picked;
        source.volume = volume * fadeInGain;
        source.time = 0f;
        source.Play();

        currentTrack = index;
        currentIsPicked = picked;
        lastPlayed = index;
        paused = false;
        ignoreEndUntil = Time.unscaledTime + 0.25f;
    }
}
