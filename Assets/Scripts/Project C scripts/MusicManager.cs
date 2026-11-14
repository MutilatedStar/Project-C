using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Universal music player. Needs DirectButton.cs in the project.
///
/// - Put it on its own empty GameObject and drag your clips into "Tracks".
/// - Toggle Button: turns the music on/off (fades out and pauses, fades back in and resumes).
/// - Next Button: Track 1 -> 2 -> ... -> last -> SHUFFLE -> back to Track 1.
/// - Both buttons are detected by code (DirectButton), so leave their OnClick lists EMPTY.
/// - Default fade in / fade out is 10 seconds. Changing tracks crossfades.
///
/// From other scripts:  MusicManager.Instance.NextTrack();  .ToggleMusic();  .SetOn(false);  .AddTrack(clip);
///                      .SelectTrack(3);  .SetShuffle(true);  .TrackCount;  .SelectedTrack;   (the pause menu uses these)
///
/// HOLD / RELEASE (used by the Lights Out screen): Hold(seconds) fades the music out and keeps it silent, whatever the buttons do, and
/// Release(seconds) brings it back. It never touches the saved On/Off choice, so the player's setting is exactly as it was afterwards.
/// </summary>
[DisallowMultipleComponent]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Playlist")]
    [Tooltip("Order matters: the Next button goes 1 -> N, then Shuffle, then back to 1.")]
    public List<AudioClip> tracks = new List<AudioClip>();
    [Min(1)] public int startTrackNumber = 1;

    [Header("Buttons (tapped directly by code - leave OnClick empty)")]
    public DirectButton toggleButton = new DirectButton();
    public DirectButton nextButton = new DirectButton();
    [Tooltip("When the music is OFF and Next is tapped:\nOn = also turn the music on.\nOff = just change the selected track silently.")]
    public bool nextTurnsMusicOn = false;

    [Header("Optional button text (only used if that button's Label is assigned)")]
    public string musicOnText = "Music: ON";
    public string musicOffText = "Music: OFF";
    [Tooltip("{0} becomes the track number.")]
    public string trackText = "Track {0}";
    public string shuffleText = "Shuffle";

    [Header("Fading & volume")]
    [Min(0f)] public float fadeInSeconds = 10f;
    [Min(0f)] public float fadeOutSeconds = 10f;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("On: fades keep running even if Time.timeScale = 0 (pause menus).")]
    public bool useUnscaledTime = true;

    [Header("Behaviour")]
    public bool startOn = true;
    [Tooltip("Saves the on/off choice with PlayerPrefs so it survives restarts.")]
    public bool rememberOnOff = true;
    public string prefsKey = "MusicOn";
    [Tooltip("On: a numbered track repeats until Next is tapped.\nOff: it plays once, then continues down the same cycle (1..N, Shuffle) by itself.\nShuffle always moves on by itself.")]
    public bool repeatSelectedTrack = true;
    [Tooltip("Keeps the music playing across scene loads. Put a copy of this object in each scene: the first one survives, later copies hand over their buttons and remove themselves.")]
    public bool persistAcrossScenes = false;
    public AudioMixerGroup outputGroup;
    [Tooltip("On: the music keeps playing while PauseManager pauses the game's audio.")]
    public bool playWhilePaused = true;

    public bool IsOn { get { return _isOn; } }
    public bool IsShuffling { get { return _shuffling; } }

    /// <summary>True between Hold and Release: the music is kept silent no matter what the buttons say.</summary>
    public bool IsHeld { get { return _held; } }

    // One Voice = one AudioSource + its fade state. Several can overlap during a crossfade.
    private class Voice
    {
        public AudioSource src;
        public float level;          // 0..1 fade level
        public float target;         // 0 or 1
        public float rate;           // level change per second
        public bool inUse;
        public bool paused;
        public bool pauseWhenSilent; // true = pause (so it can resume), false = stop and free the voice
        public float startedAt;
    }

    private readonly List<Voice> _voices = new List<Voice>();
    private Voice _current;
    private bool _isOn;
    private bool _held;   // Hold() is in effect: nothing may start or come back until Release()
    private bool _started;
    private bool _shuffling;
    private int _trackIndex;

    private readonly List<int> _bag = new List<int>();
    private int _bagPos;
    private int _lastPlayed = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A persistent copy already exists: give it this scene's buttons, then remove this duplicate.
            Instance.TakeButtonsFrom(this);
            enabled = false;
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (persistAcrossScenes)
        {
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        int removed = tracks.RemoveAll(t => t == null);
        if (removed > 0) Debug.LogWarning("MusicManager: ignored " + removed + " empty slot(s) in Tracks.");

        _trackIndex = Mathf.Clamp(startTrackNumber - 1, 0, Mathf.Max(0, tracks.Count - 1));
        _isOn = rememberOnOff ? PlayerPrefs.GetInt(prefsKey, startOn ? 1 : 0) == 1 : startOn;
    }

    private void Start()
    {
        _started = true;
        RefreshLabels();
        if (_isOn && !_held) StartSelected();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (toggleButton.WasTapped()) ToggleMusic();
        if (nextButton.WasTapped()) NextTrack();

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        for (int i = 0; i < _voices.Count; i++) Tick(_voices[i], dt);

        WatchForTrackEnd();
    }

    // ------------------------------------------------------------------ public API

    public void ToggleMusic() { SetOn(!_isOn); }

    public void SetOn(bool on)
    {
        if (on == _isOn) return;
        _isOn = on;

        if (rememberOnOff)
        {
            PlayerPrefs.SetInt(prefsKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        if (on && _held)
        {
            // The choice is saved above, but the music stays silent until Release() (it comes back then, because the setting is On).
            RefreshLabels();
            return;
        }

        if (on)
        {
            if (_current != null)
            {
                // Still fading out or paused: bring the same track back.
                if (_current.paused) { _current.src.UnPause(); _current.paused = false; }
                _current.target = 1f;
                _current.rate = RateFor(fadeInSeconds);
                _current.pauseWhenSilent = false;
            }
            else
            {
                StartSelected();
            }
        }
        else if (_current != null)
        {
            _current.target = 0f;
            _current.rate = RateFor(fadeOutSeconds);
            _current.pauseWhenSilent = true;
        }

        RefreshLabels();
    }

    /// <summary>Same thing the Next button does: Track 1..N, then Shuffle, then back to Track 1.</summary>
    public void NextTrack()
    {
        if (tracks.Count < 2) return;

        AdvanceSelection();
        ReleaseCurrent();

        if (_isOn) { if (!_held) StartSelected(); }
        else if (nextTurnsMusicOn) SetOn(true);

        RefreshLabels();
    }

    /// <summary>How many usable tracks are in the playlist.</summary>
    public int TrackCount { get { return tracks.Count; } }

    /// <summary>The selected track number, starting at 1. (While shuffling this is the track Shuffle will go back to when it is switched off.)</summary>
    public int SelectedTrack { get { return _trackIndex + 1; } }

    /// <summary>
    /// Picks a track by number (1 = the first one) and turns Shuffle off. A number past the end picks the LAST track. The old track fades out
    /// and the new one fades in, the same as the Next button. Does nothing if that track is already the selected one. Used by the pause menu.
    /// </summary>
    public void SelectTrack(int number)
    {
        if (tracks.Count == 0) return;

        int index = Mathf.Clamp(number - 1, 0, tracks.Count - 1);
        if (!_shuffling && index == _trackIndex) return;

        _shuffling = false;
        _trackIndex = index;
        ReleaseCurrent();

        if (_isOn && !_held) StartSelected();
        RefreshLabels();
    }

    /// <summary>
    /// Turns Shuffle on or off. Off goes back to the selected track (SelectedTrack) and repeats it. Does nothing if Shuffle is already in that
    /// state. Used by the pause menu.
    /// </summary>
    public void SetShuffle(bool shuffle)
    {
        if (tracks.Count == 0 || shuffle == _shuffling) return;

        _shuffling = shuffle;
        ReleaseCurrent();

        if (_isOn && !_held) StartSelected();
        RefreshLabels();
    }

    /// <summary>
    /// Fades the music out over this many seconds and keeps it silent until Release(). Safe to call twice.
    /// The On/Off setting (and its saved PlayerPrefs value) is NOT changed.
    /// </summary>
    public void Hold(float fadeSeconds)
    {
        if (_held) return;
        _held = true;

        for (int i = 0; i < _voices.Count; i++)
        {
            Voice v = _voices[i];
            if (!v.inUse || v.paused) continue;

            v.target = 0f;
            v.rate = RateFor(fadeSeconds);
            v.pauseWhenSilent = v == _current;   // the current track pauses (so it can come back); a track already fading out just ends
        }
    }

    /// <summary>
    /// Ends Hold(): if the music setting is On, the track that was playing fades back in over this many seconds (or the selected track starts,
    /// if it was dropped meanwhile). If the setting is Off, nothing plays. Safe to call twice.
    /// </summary>
    public void Release(float fadeSeconds)
    {
        if (!_held) return;
        _held = false;

        if (!_isOn) return;

        if (_current != null)
        {
            if (_current.paused) { _current.src.UnPause(); _current.paused = false; }
            _current.target = 1f;
            _current.rate = RateFor(fadeSeconds);
            _current.pauseWhenSilent = false;
        }
        else
        {
            StartSelected();
        }
    }

    /// <summary>Adds a clip to the end of the playlist at runtime.</summary>
    public void AddTrack(AudioClip clip)
    {
        if (clip == null) return;
        tracks.Add(clip);
        _bag.Clear();
        _bagPos = 0;
        if (_started && _isOn && !_held && _current == null) StartSelected();
        RefreshLabels();
    }

    // ------------------------------------------------------------------ selection logic

    private void AdvanceSelection()
    {
        if (_shuffling)
        {
            _shuffling = false;
            _trackIndex = 0;
        }
        else if (_trackIndex + 1 < tracks.Count)
        {
            _trackIndex++;
        }
        else
        {
            _shuffling = true;
        }
    }

    private void StartSelected()
    {
        if (tracks.Count == 0) return;
        PlayIndex(_shuffling ? NextShuffleIndex() : Mathf.Clamp(_trackIndex, 0, tracks.Count - 1));
    }

    // Shuffle "bag": every track plays once in random order, then it reshuffles (never repeating back-to-back).
    private int NextShuffleIndex()
    {
        if (_bagPos >= _bag.Count)
        {
            _bag.Clear();
            for (int i = 0; i < tracks.Count; i++) _bag.Add(i);

            for (int i = _bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int tmp = _bag[i]; _bag[i] = _bag[j]; _bag[j] = tmp;
            }

            if (_bag.Count > 1 && _bag[0] == _lastPlayed)
            {
                int k = Random.Range(1, _bag.Count);
                int tmp = _bag[0]; _bag[0] = _bag[k]; _bag[k] = tmp;
            }
            _bagPos = 0;
        }
        return _bag[_bagPos++];
    }

    private void WatchForTrackEnd()
    {
        if (_held || !_isOn || _current == null || _current.paused) return;

        AudioSource s = _current.src;
        if (s.loop || s.clip == null) return;

        // Start the next track early so the fade-out of this one and fade-in of the next overlap.
        float fade = Mathf.Min(fadeOutSeconds, s.clip.length * 0.5f);
        bool nearEnd = s.time >= s.clip.length - fade;
        bool stopped = !s.isPlaying && !AudioListener.pause && (Time.unscaledTime - _current.startedAt) > 1f;

        if (nearEnd || stopped) AdvanceAutomatically();
    }

    private void AdvanceAutomatically()
    {
        if (!_shuffling) AdvanceSelection();
        ReleaseCurrent();
        StartSelected();
        RefreshLabels();
    }

    // ------------------------------------------------------------------ voices & fading

    private void PlayIndex(int idx)
    {
        Voice v = GetFreeVoice();
        bool instant = fadeInSeconds <= 0f;

        v.level = instant ? 1f : 0f;
        v.target = 1f;
        v.rate = RateFor(fadeInSeconds);
        v.inUse = true;
        v.paused = false;
        v.pauseWhenSilent = false;
        v.startedAt = Time.unscaledTime;

        v.src.clip = tracks[idx];
        v.src.loop = repeatSelectedTrack && !_shuffling;
        v.src.volume = v.level * volume;
        v.src.time = 0f;
        v.src.Play();

        _current = v;
        _lastPlayed = idx;
    }

    // Lets the current track fade out and free itself (or drops it instantly if it was paused).
    private void ReleaseCurrent()
    {
        if (_current == null) return;

        Voice v = _current;
        _current = null;

        if (v.paused)
        {
            StopVoice(v);
            return;
        }
        v.target = 0f;
        v.rate = RateFor(fadeOutSeconds);
        v.pauseWhenSilent = false;
    }

    private void Tick(Voice v, float dt)
    {
        if (!v.inUse || v.paused) return;

        if (v.level != v.target)
            v.level = Mathf.MoveTowards(v.level, v.target, v.rate * dt);
        v.src.volume = v.level * volume;

        if (v.level <= 0f && v.target <= 0f)
        {
            if (v.pauseWhenSilent)
            {
                v.src.Pause();
                v.paused = true;
            }
            else
            {
                StopVoice(v);
            }
        }
    }

    private void StopVoice(Voice v)
    {
        v.src.Stop();
        v.inUse = false;
        v.paused = false;
    }

    private Voice GetFreeVoice()
    {
        for (int i = 0; i < _voices.Count; i++)
            if (!_voices[i].inUse) return _voices[i];

        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = false;
        s.spatialBlend = 0f; // 2D
        s.ignoreListenerPause = playWhilePaused;
        if (outputGroup != null) s.outputAudioMixerGroup = outputGroup;

        Voice v = new Voice { src = s };
        _voices.Add(v);
        return v;
    }

    private static float RateFor(float seconds)
    {
        return seconds > 0f ? 1f / seconds : 1000f;
    }

    // ------------------------------------------------------------------ buttons / labels

    private void TakeButtonsFrom(MusicManager other)
    {
        if (other.toggleButton.HasTarget) toggleButton = other.toggleButton;
        if (other.nextButton.HasTarget) nextButton = other.nextButton;
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        toggleButton.SetLabel(_isOn ? musicOnText : musicOffText);
        nextButton.SetLabel(_shuffling ? shuffleText : string.Format(trackText, _trackIndex + 1));
    }
}
