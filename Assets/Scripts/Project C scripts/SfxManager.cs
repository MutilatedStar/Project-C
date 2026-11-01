using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Universal sound-effect player. Needs DirectButton.cs in the project.
///
/// - Put it on its own empty GameObject and fill in the "Sounds" list (a name + one or more clips each).
/// - Toggle Button: turns ALL sound effects on/off. Detected by code (DirectButton), so leave OnClick EMPTY.
/// - Play a sound from ANY script, no references needed:
///       SfxManager.Play("jump");
///       SfxManager.Play("coin", 0.5f);        // optional volume multiplier
///       SfxManager.Play(someAudioClip);       // play a clip that isn't in the list
/// - While SFX are off, every Play call is silently ignored (and anything playing is cut).
/// - "Button Sounds": optional list of buttons that play a sound when tapped, also with no OnClick.
/// </summary>
[DisallowMultipleComponent]
public class SfxManager : MonoBehaviour
{
    public static SfxManager Instance { get; private set; }

    [System.Serializable]
    public class Sound
    {
        public string name;
        [Tooltip("More than one clip = a random one is picked each time (great for footsteps, hits...).")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random pitch wobble, +/- this amount. 0 = always the same pitch.")]
        [Range(0f, 0.5f)] public float pitchVariation = 0f;
    }

    [System.Serializable]
    public class ButtonSound
    {
        public DirectButton button = new DirectButton();
        public string soundName;
    }

    [Header("Sound library")]
    public List<Sound> sounds = new List<Sound>();

    [Header("On/Off button (tapped directly by code - leave OnClick empty)")]
    public DirectButton toggleButton = new DirectButton();
    public string sfxOnText = "SFX: ON";
    public string sfxOffText = "SFX: OFF";

    [Header("Optional: buttons that play a sound when tapped")]
    public List<ButtonSound> buttonSounds = new List<ButtonSound>();

    [Header("Settings")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Tooltip("How many sounds can overlap. When all are busy the oldest one gets reused.")]
    [Min(1)] public int voiceCount = 12;
    public bool startOn = true;
    [Tooltip("Saves the on/off choice with PlayerPrefs so it survives restarts.")]
    public bool rememberOnOff = true;
    public string prefsKey = "SfxOn";
    [Tooltip("Keeps this object across scene loads. Put a copy in each scene: the first one survives, later copies hand over their buttons and remove themselves.")]
    public bool persistAcrossScenes = false;
    public AudioMixerGroup outputGroup;
    [Tooltip("On: sounds still play while PauseManager pauses the game's audio, so pause-menu clicks are heard. (Sounds already playing when you pause finish instead of freezing.)")]
    public bool playWhilePaused = true;

    public static bool IsEnabled { get { return Instance != null && Instance._isOn; } }
    public bool IsOn { get { return _isOn; } }

    private readonly List<AudioSource> _sources = new List<AudioSource>();
    private readonly Dictionary<string, Sound> _lookup =
        new Dictionary<string, Sound>(System.StringComparer.OrdinalIgnoreCase);
    private int _cursor;
    private bool _isOn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    // ------------------------------------------------------------------ static API (call from anywhere)

    /// <summary>Plays a sound from the library by name. Safe to call even if no SfxManager exists.</summary>
    public static void Play(string soundName, float volumeScale = 1f)
    {
        if (Instance != null) Instance.PlayByName(soundName, volumeScale);
    }

    /// <summary>Plays any AudioClip through the SFX system (respects the on/off toggle).</summary>
    public static void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (Instance != null && Instance._isOn && clip != null)
            Instance.Emit(clip, volumeScale, 1f);
    }

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

        for (int i = 0; i < voiceCount; i++)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f; // 2D
            s.ignoreListenerPause = playWhilePaused;
            if (outputGroup != null) s.outputAudioMixerGroup = outputGroup;
            _sources.Add(s);
        }

        RebuildLookup();
        _isOn = rememberOnOff ? PlayerPrefs.GetInt(prefsKey, startOn ? 1 : 0) == 1 : startOn;
    }

    private void Start()
    {
        RefreshLabel();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (toggleButton.WasTapped()) ToggleSfx();

        for (int i = 0; i < buttonSounds.Count; i++)
        {
            ButtonSound b = buttonSounds[i];
            if (b != null && b.button != null && b.button.WasTapped())
                PlayByName(b.soundName, 1f);
        }
    }

    // ------------------------------------------------------------------ instance API

    public void ToggleSfx() { SetOn(!_isOn); }

    public void SetOn(bool on)
    {
        if (on == _isOn) return;
        _isOn = on;

        if (!on)
        {
            for (int i = 0; i < _sources.Count; i++) _sources[i].Stop();
        }

        if (rememberOnOff)
        {
            PlayerPrefs.SetInt(prefsKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }
        RefreshLabel();
    }

    /// <summary>Adds (or replaces) a named sound at runtime.</summary>
    public void AddSound(string soundName, AudioClip clip, float soundVolume = 1f)
    {
        if (string.IsNullOrEmpty(soundName) || clip == null) return;
        sounds.Add(new Sound { name = soundName, clips = new[] { clip }, volume = soundVolume });
        RebuildLookup();
    }

    // ------------------------------------------------------------------ internals

    private void PlayByName(string soundName, float volumeScale)
    {
        if (!_isOn || string.IsNullOrEmpty(soundName)) return;

        Sound s;
        if (!_lookup.TryGetValue(soundName, out s))
        {
            Debug.LogWarning("SfxManager: no sound named '" + soundName + "'.");
            return;
        }
        if (s.clips == null || s.clips.Length == 0) return;

        AudioClip clip = s.clips[s.clips.Length == 1 ? 0 : Random.Range(0, s.clips.Length)];
        if (clip == null) return;

        float pitch = 1f + Random.Range(-s.pitchVariation, s.pitchVariation);
        Emit(clip, s.volume * volumeScale, pitch);
    }

    private void Emit(AudioClip clip, float vol, float pitch)
    {
        AudioSource s = NextSource();
        s.Stop();
        s.clip = clip;
        s.pitch = pitch;
        s.volume = Mathf.Clamp01(vol * masterVolume);
        s.Play();
    }

    // First idle source; if everything is busy, reuse them round-robin (oldest first).
    private AudioSource NextSource()
    {
        int count = _sources.Count;
        for (int i = 0; i < count; i++)
        {
            int idx = (_cursor + i) % count;
            if (!_sources[idx].isPlaying)
            {
                _cursor = (idx + 1) % count;
                return _sources[idx];
            }
        }
        AudioSource s = _sources[_cursor];
        _cursor = (_cursor + 1) % count;
        return s;
    }

    private void RebuildLookup()
    {
        _lookup.Clear();
        for (int i = 0; i < sounds.Count; i++)
        {
            Sound s = sounds[i];
            if (s == null || string.IsNullOrEmpty(s.name)) continue;
            if (_lookup.ContainsKey(s.name))
                Debug.LogWarning("SfxManager: duplicate sound name '" + s.name + "' - the last one wins.");
            _lookup[s.name] = s;
        }
    }

    private void TakeButtonsFrom(SfxManager other)
    {
        if (other.toggleButton.HasTarget) toggleButton = other.toggleButton;
        if (other.buttonSounds.Count > 0) buttonSounds = other.buttonSounds;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        toggleButton.SetLabel(_isOn ? sfxOnText : sfxOffText);
    }
}
