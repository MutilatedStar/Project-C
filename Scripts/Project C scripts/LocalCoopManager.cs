using UnityEngine;

/// <summary>
/// The Player 2 on/off switch, kept in one static place so every script (and PlayerInputSource) can read it:
///
///     LocalCoop.Enabled        true = two players share the keyboard (Player 1 left half, Player 2 right half)
///     LocalCoop.Toggle();      LocalCoop.Set(true);       LocalCoop.Changed += on => { ... };
///
/// OFF: one player, who gets the WHOLE keyboard (extra keys). ON: two players, each with fewer keys.
/// The choice is remembered between runs (PlayerPrefs) unless LocalCoopManager says otherwise.
/// </summary>
public static class LocalCoop
{
    private const string Key = "LocalCoopOn";

    private static bool _loaded;
    private static bool _on;
    private static bool _initialized;

    /// <summary>Save the choice with PlayerPrefs. LocalCoopManager sets this.</summary>
    public static bool RememberChoice = true;

    /// <summary>Raised when the choice changes. The value is the new state.</summary>
    public static event System.Action<bool> Changed;

    public static bool Enabled
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _on = PlayerPrefs.GetInt(Key, 0) == 1;
            }
            return _on;
        }
    }

    public static bool HasSavedChoice { get { return PlayerPrefs.HasKey(Key); } }

    /// <summary>Runs once per game session: uses the saved choice if there is one, otherwise the start value.</summary>
    public static void Initialize(bool startValue, bool remember)
    {
        if (_initialized) return;
        _initialized = true;

        RememberChoice = remember;
        _on = (remember && PlayerPrefs.HasKey(Key)) ? PlayerPrefs.GetInt(Key, 0) == 1 : startValue;
        _loaded = true;
    }

    public static void Set(bool on)
    {
        bool before = Enabled;
        _on = on;

        if (RememberChoice)
        {
            PlayerPrefs.SetInt(Key, on ? 1 : 0);
            PlayerPrefs.Save();
        }
        if (before != on && Changed != null) Changed(on);
    }

    public static void Toggle() { Set(!Enabled); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _loaded = false;
        _initialized = false;
        Changed = null;
    }
}

/// <summary>
/// Put this in each gameplay scene (on its own empty object). It
///   - holds the on-screen Player 2 toggle button (tapped by code, so leave its OnClick empty), and
///   - adds Player 2 when two-player mode turns on and removes them when it turns off.
///
/// Player 2: drag in EITHER the Player 2 PREFAB (it gets spawned / destroyed) OR a Player 2 object that already
/// sits in the scene (it gets switched on / off). Either way, put the same prefab (or object) in Element 1 of the Players list
/// of the scene's PlayerInputHub: that slot gives it the Player 2 keys and controller, so nothing is set on the character.
/// Make the Player 2 prefab by duplicating the Player 1 prefab (a Prefab Variant is fine) and give it its own look and its own
/// audio sources. It no longer needs its own PlayerInputSource or Player Number.
///
/// From code:  LocalCoop.Toggle();   LocalCoop.Enabled
/// </summary>
[DisallowMultipleComponent]
public class LocalCoopManager : MonoBehaviour
{
    [Header("Player 2")]
    [Tooltip("The Player 2 PREFAB (spawned and destroyed as needed) or a Player 2 object already in the scene (switched on and off). It must not be this object or one of its parents.")]
    public GameObject player2;
    [Tooltip("Optional. Where Player 2 appears. Empty = next to Player 1 (if set) or at this object.")]
    public Transform spawnPoint;
    [Tooltip("Optional. Used to place Player 2 next to Player 1 when there is no Spawn Point.")]
    public Transform player1;
    public Vector3 spawnOffset = new Vector3(1f, 0f, 0f);

    [Header("Toggle button (tapped by code, leave OnClick empty)")]
    public DirectButton toggleButton = new DirectButton();
    public string onText = "Players: 2";
    public string offText = "Players: 1";

    [Header("Start")]
    [Tooltip("Two players when the game starts for the first time (or every time, if Remember Choice is off).")]
    public bool startWithTwoPlayers = false;
    [Tooltip("Keep the choice between runs.")]
    public bool rememberChoice = true;

    private GameObject _spawned;
    private bool _warned;

    private void Awake()
    {
        LocalCoop.Initialize(startWithTwoPlayers, rememberChoice);

        if (player2 != null && (player2 == gameObject || transform.IsChildOf(player2.transform)))
        {
            Debug.LogError("LocalCoopManager: the Player 2 object is this object or one of its parents, so switching " +
                           "Player 2 off would switch this manager off too. Put the manager on its own empty object.", this);
            player2 = null;
        }
    }

    private void OnEnable()
    {
        LocalCoop.Changed += Apply;
    }

    private void OnDisable()
    {
        LocalCoop.Changed -= Apply;
    }

    private void Start()
    {
        Apply(LocalCoop.Enabled);   // in Start so Player 1 already exists when Player 2 is placed next to them
    }

    private void Update()
    {
        if (toggleButton.WasTapped()) LocalCoop.Toggle();
    }

    private void Apply(bool on)
    {
        toggleButton.SetLabel(on ? onText : offText);

        if (player2 == null)
        {
            if (on && !_warned)
            {
                _warned = true;
                Debug.LogWarning("LocalCoopManager: two-player mode is ON but no Player 2 is assigned, so nobody appears. " +
                                 "Drag the Player 2 prefab into the Player 2 slot.", this);
            }
            return;
        }

        bool isPrefabAsset = !player2.scene.IsValid();   // assets in the Project window have no scene

        if (isPrefabAsset)
        {
            if (on && _spawned == null) _spawned = Instantiate(player2, SpawnPosition(), Quaternion.identity);
            else if (!on && _spawned != null)
            {
                Destroy(_spawned);
                _spawned = null;
            }
        }
        else
        {
            if (on && !player2.activeSelf && spawnPoint != null) player2.transform.position = spawnPoint.position;
            player2.SetActive(on);
        }
    }

    private Vector3 SpawnPosition()
    {
        if (spawnPoint != null) return spawnPoint.position;
        if (player1 != null) return player1.position + spawnOffset;
        return transform.position;
    }
}
