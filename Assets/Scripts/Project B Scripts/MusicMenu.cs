using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// Menu controls for music. Nothing here takes effect until the Apply button is pressed.
//
//  Repeat Track button: Random -> 1 -> 2 -> ... -> last track -> Random
//  Song number box:     a valid number picks that track, a number past the end picks the LAST track,
//                       anything invalid (blank, letters, 0, negatives, decimals) means Random
//  Music button:        toggles music On / Off (On by default)
public class MusicMenu : MonoBehaviour
{
    [Header("Repeat Track button (cycles Random, 1, 2, ... then back to Random)")]
    [SerializeField] Button trackButton;
    [SerializeField] TextRef trackLabel;
    [SerializeField] string randomText = "Repeat Track: Random";
    [Tooltip("{0} becomes the track number.")]
    [SerializeField] string trackTextFormat = "Repeat Track: {0}";

    [Header("Type a song number")]
    [SerializeField] InputRef trackInput;

    [Header("Music on / off button")]
    [SerializeField] Button musicButton;
    [SerializeField] TextRef musicLabel;
    [SerializeField] string musicOnText = "Music: On";
    [SerializeField] string musicOffText = "Music: Off";

    [Header("Only used if no MusicPlayer exists in the current scene")]
    [SerializeField] int fallbackTrackCount = 7;

    int MaxTracks
    {
        get { return MusicPlayer.Instance != null ? MusicPlayer.Instance.TrackCount : fallbackTrackCount; }
    }

    void OnEnable()
    {
        // Start from what is actually applied, so closing the menu without Apply discards changes
        GameSettings.PendingMusicOn = GameSettings.MusicOn;
        GameSettings.PendingTrackMode = Mathf.Min(GameSettings.TrackMode, MaxTracks);

        if (trackButton != null) trackButton.onClick.AddListener(OnTrackButton);
        if (musicButton != null) musicButton.onClick.AddListener(OnMusicButton);
        trackInput.AddEndEditListener(OnTrackTyped);
        GameSettings.BeforeApply += CommitTyped;

        Refresh();
    }

    void Start()
    {
        // The MusicPlayer may not have existed yet during OnEnable, so re-check the track count now
        GameSettings.PendingTrackMode = Mathf.Min(GameSettings.PendingTrackMode, MaxTracks);
        Refresh();
    }

    void OnDisable()
    {
        if (trackButton != null) trackButton.onClick.RemoveListener(OnTrackButton);
        if (musicButton != null) musicButton.onClick.RemoveListener(OnMusicButton);
        trackInput.RemoveEndEditListener(OnTrackTyped);
        GameSettings.BeforeApply -= CommitTyped;
    }

    void OnTrackButton()
    {
        int next = GameSettings.PendingTrackMode + 1;
        if (next > MaxTracks) next = 0; // past the last track -> back to random
        GameSettings.PendingTrackMode = next;
        Refresh();
    }

    void OnMusicButton()
    {
        GameSettings.PendingMusicOn = !GameSettings.PendingMusicOn;
        Refresh();
    }

    void OnTrackTyped(string raw)
    {
        GameSettings.PendingTrackMode = ParseTrack(raw);
        Refresh();
    }

    // Runs right before Apply, in case the player is still typing in the box
    void CommitTyped()
    {
        GameSettings.PendingTrackMode = ParseTrack(trackInput.Value);
        Refresh();
    }

    // 0 = random, otherwise a track number from 1 to MaxTracks
    int ParseTrack(string raw)
    {
        int max = MaxTracks;
        string s = raw == null ? "" : raw.Trim();
        if (s.Length == 0 || max <= 0) return 0;

        // Digits only. Letters, minus signs, decimals, etc. are invalid -> random.
        for (int i = 0; i < s.Length; i++)
            if (s[i] < '0' || s[i] > '9') return 0;

        int n;
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n))
            return max; // all digits but too big for a number -> past the end of the list -> last track

        if (n <= 0) return 0;      // "0" is not a song -> random
        return Mathf.Min(n, max);  // 99999 -> last track
    }

    void Refresh()
    {
        int mode = GameSettings.PendingTrackMode;

        trackLabel.Set(mode == 0 ? randomText : string.Format(trackTextFormat, mode));
        trackInput.Value = mode == 0 ? "" : mode.ToString();
        musicLabel.Set(GameSettings.PendingMusicOn ? musicOnText : musicOffText);
    }
}
