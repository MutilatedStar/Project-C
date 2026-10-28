using System.Collections.Generic;
using UnityEngine;

// Put this on a ROOT object in the Investigation scene (with or without an AudioSource).
//
// RANDOM mode (Track Mode 0): plays a random track, never the same one twice in a row.
//   Each song slowly fades to silence over its last "Fade Out Seconds", then the next starts.
// PICKED mode (Track Mode 1+): loops that one track ("Repeat Track"), with no fade.
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
        if (!currentIsPicked && fadeOutSeconds > 0f && source.clip != null)
        {
            float length = source.clip.length;
            float fade = Mathf.Min(fadeOutSeconds, length);
            float remaining = length - source.time;
            source.volume = volume * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remaining / fade));
        }
    }

    // Called at start and every time the Apply button is pressed.
    void ApplySettings()
    {
        if (Instance != this) return;

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
        source.volume = volume;
        source.time = 0f;
        source.Play();

        currentTrack = index;
        currentIsPicked = picked;
        lastPlayed = index;
        paused = false;
        ignoreEndUntil = Time.unscaledTime + 0.25f;
    }
}
