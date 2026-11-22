using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ilumisoft.HealthSystem;

/// <summary>
/// The three co-op rules, kept in one static place (same idea as LocalCoop) so any script can read them:
///
///     CoopRules.SharedDamage       true = a hit on one player hurts every player that counts
///     CoopRules.SharedScore        true = one team score that everyone sees; false = each player keeps their own
///     CoopRules.SinglePlayerMode   true = only ONE player counts (SinglePlayerTarget, normally Player 1)
///     CoopRules.SharedWeapons      true = swords (and sword upgrades) are shared: one player unlocks it, everybody has it.
///                                  false (the default) = every player has their own sword collection
///
/// "Counts" is the key idea: in single player mode the other player takes no damage, earns no score and is
/// ignored by anything that asks CoopRules.Counts(playerNumber). Single player is also forced on whenever
/// Player 2 is switched off in LocalCoopManager, so one-player games behave exactly as before.
///
/// Put a CoopRulesManager in each gameplay scene to set the starting values and (optionally) get on-screen buttons.
/// </summary>
public static class CoopRules
{
    private static bool _sharedDamage;
    private static bool _sharedScore;
    private static bool _singlePlayer;
    private static bool _sharedWeapons;
    private static int _singleTarget = 1;
    private static bool _initialized;

    /// <summary>Raised whenever any rule changes (or Player 2 is switched on or off).</summary>
    public static event Action Changed;

    public static bool SharedDamage
    {
        get { return _sharedDamage; }
        set { if (_sharedDamage == value) return; _sharedDamage = value; NotifyChanged(); }
    }

    public static bool SharedScore
    {
        get { return _sharedScore; }
        set { if (_sharedScore == value) return; _sharedScore = value; NotifyChanged(); }
    }

    public static bool SharedWeapons
    {
        get { return _sharedWeapons; }
        set { if (_sharedWeapons == value) return; _sharedWeapons = value; NotifyChanged(); }
    }

    public static bool SinglePlayerMode
    {
        get { return _singlePlayer; }
        set { if (_singlePlayer == value) return; _singlePlayer = value; NotifyChanged(); }
    }

    /// <summary>Which player is the one that counts in single player mode (1 or 2).</summary>
    public static int SinglePlayerTarget
    {
        get { return _singleTarget; }
        set
        {
            value = Mathf.Clamp(value, 1, 4);
            if (_singleTarget == value) return;
            _singleTarget = value;
            NotifyChanged();
        }
    }

    /// <summary>True when only one player is being looked at: the toggle is on, or Player 2 is switched off.</summary>
    public static bool OnePlayerOnly { get { return _singlePlayer || !LocalCoop.Enabled; } }

    /// <summary>The player that counts while only one is looked at. Always Player 1 when Player 2 is switched off.</summary>
    public static int TrackedPlayer { get { return LocalCoop.Enabled ? _singleTarget : 1; } }

    /// <summary>Does this player take damage, earn score and count towards winning / losing right now?</summary>
    public static bool Counts(int playerNumber)
    {
        return OnePlayerOnly ? playerNumber == TrackedPlayer : true;
    }

    public static void NotifyChanged()
    {
        if (Changed != null) Changed();
    }

    /// <summary>Runs once per game session, so a rule set in a menu scene isn't wiped by the next scene's Inspector values.</summary>
    public static void Initialize(bool sharedDamage, bool sharedScore, bool singlePlayer, int singleTarget, bool sharedWeapons = false)
    {
        if (_initialized) return;
        _initialized = true;

        _sharedWeapons = sharedWeapons;
        _sharedDamage = sharedDamage;
        _sharedScore = sharedScore;
        _singlePlayer = singlePlayer;
        _singleTarget = Mathf.Clamp(singleTarget, 1, 4);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _sharedDamage = false;
        _sharedScore = false;
        _singlePlayer = false;
        _sharedWeapons = false;
        _singleTarget = 1;
        _initialized = false;
        Changed = null;
    }
}

/// <summary>
/// Score per player. Scores live here (not on the player objects), so Player 2 keeps their points when they are
/// switched off and on again, and the scores survive a scene change.
///
///     CoopScore.AddNear(foeScore, transform.position);    // foe died: nearest living player gets the points
///     CoopScore.Add(foeScore, killerPlayerNumber);        // ...or, if you know who landed the kill
///     CoopScore.Of(1)                                     // what Player 1's score reads as right now
///     CoopScore.Total                                     // the one team number (win screen, high score)
///     CoopScore.ResetAll();                               // new run: back to zero
///     CoopScore.Reset(2);                                 // only Player 2 loses their points (CoopDowned does this when a player blows up)
///
/// Of(n) follows the rules: Shared Score ON gives everyone the team total, OFF gives that player's own points,
/// and a player who doesn't count (single player mode) reads 0.
///
/// WHEN THE SCORE GOES BACK TO ZERO (all of it automatic):
///   - one player blows up (CoopDowned, Reset Score On Blow Up): only THAT player's points. With Shared Score ON the team number drops by what
///     that player had earned.
///   - the whole team is wiped and picks Retry on the Lights Out screen (Reset Score On Retry), or leaves through Level Select / Menu.
///   - any scene is loaded fresh (leaving for another scene and coming back, a restart, the menu...), EXCEPT the trip to the Shop and back:
///     FloorController calls BeginShopTrip, so the points earned before the Shop are still there when the dungeon loads again.
///     CoopRulesManager has a tick (Reset Score On Scene Change) to switch the scene rule off.
/// </summary>
public static class CoopScore
{
    private const int MaxPlayers = 4;
    private static readonly int[] _own = new int[MaxPlayers + 1];   // index = player number, 0 unused

    /// <summary>Raised whenever points are added or the scores are reset.</summary>
    public static event Action Changed;

    /// <summary>ON (default): loading a scene sets the score back to zero (not for the Shop trip). CoopRulesManager sets this from its tick.</summary>
    public static bool ResetOnSceneChange = true;

    // A trip to the Shop under way: the scene we left, and whether the Shop itself has loaded yet.
    private static bool _shopTripPending;
    private static bool _inShop;
    private static string _shopScene;
    private static string _homeScene;

    /// <summary>
    /// Set by a player's weapon for the instant its hit is applied (and put back to 0 right after), so a foe that dies
    /// from that hit can credit the right player. 0 = nobody (a burn, a trap...).
    /// </summary>
    public static int CreditedPlayer;

    /// <summary>Give points to one player. Ignored for a player who doesn't count (single player mode).</summary>
    public static void Add(int amount, int playerNumber)
    {
        if (amount == 0 || playerNumber < 1 || playerNumber > MaxPlayers) return;
        if (!CoopRules.Counts(playerNumber)) return;

        _own[playerNumber] += amount;
        if (Changed != null) Changed();
    }

    /// <summary>Give points to whichever living player is closest to this position (a foe that just died, say).</summary>
    public static void AddNear(int amount, Vector3 position)
    {
        CoopHealth nearest = CoopHealth.Nearest(position, true);
        if (nearest == null) nearest = CoopHealth.Nearest(position, false);

        int who = nearest != null ? nearest.PlayerNumber : (CoopRules.OnePlayerOnly ? CoopRules.TrackedPlayer : 1);
        Add(amount, who);
    }

    /// <summary>
    /// For a foe's death: the points go to the player whose hit is being applied (CreditedPlayer), or, when nobody is
    /// credited (burning ground, a trap), to the living player nearest to the foe.
    /// </summary>
    public static void AddCredited(int amount, Vector3 position)
    {
        int who = CreditedPlayer;
        if (who >= 1 && who <= MaxPlayers && CoopRules.Counts(who)) Add(amount, who);
        else AddNear(amount, position);
    }

    /// <summary>The score this player should SEE: the team total if Shared Score is on, otherwise their own.</summary>
    public static int Of(int playerNumber)
    {
        if (playerNumber < 1 || playerNumber > MaxPlayers) return 0;
        if (!CoopRules.Counts(playerNumber)) return 0;
        return CoopRules.SharedScore ? Total : _own[playerNumber];
    }

    /// <summary>What this player personally earned, whatever the Shared Score setting (for "P1 earned..." end screens).</summary>
    public static int Earned(int playerNumber)
    {
        if (playerNumber < 1 || playerNumber > MaxPlayers) return 0;
        return _own[playerNumber];
    }

    /// <summary>Everything earned by the players that count. Use this for the single team number.</summary>
    public static int Total
    {
        get
        {
            int sum = 0;
            for (int n = 1; n <= MaxPlayers; n++)
            {
                if (CoopRules.Counts(n)) sum += _own[n];
            }
            return sum;
        }
    }

    public static void ResetAll()
    {
        for (int n = 0; n <= MaxPlayers; n++) _own[n] = 0;
        if (Changed != null) Changed();
    }

    /// <summary>Sets ONE player's own points back to zero (the other players keep theirs).</summary>
    public static void Reset(int playerNumber)
    {
        if (playerNumber < 1 || playerNumber > MaxPlayers) return;
        if (_own[playerNumber] == 0) return;

        _own[playerNumber] = 0;
        if (Changed != null) Changed();
    }

    /// <summary>
    /// Call right before loading the Shop scene. The score is kept for the trip there and for the way back to the scene that is being left;
    /// any other scene load resets it as usual. FloorController does this for you.
    /// </summary>
    public static void BeginShopTrip(string shopSceneName)
    {
        _shopTripPending = true;
        _inShop = false;
        _shopScene = shopSceneName;
        _homeScene = SceneManager.GetActiveScene().name;
    }

    // Runs after every scene's Awake / OnEnable (so a CoopRulesManager in the new scene has already set ResetOnSceneChange).
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;   // an extra scene added on top is not a scene change

        if (_shopTripPending)
        {
            _shopTripPending = false;
            if (scene.name == _shopScene)
            {
                _inShop = true;   // arrived in the Shop: keep everything
                return;
            }
            // Something other than the Shop loaded instead: treat it like any other scene change.
        }
        else if (_inShop)
        {
            _inShop = false;
            if (scene.name == _homeScene) return;   // back from the Shop: keep everything
            // Left the Shop for somewhere else (the menu, say): a new run.
        }

        if (ResetOnSceneChange) GameReset.ResetScores();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        for (int n = 0; n <= MaxPlayers; n++) _own[n] = 0;
        Changed = null;

        ResetOnSceneChange = true;
        _shopTripPending = false;
        _inShop = false;
        _shopScene = null;
        _homeScene = null;

        // Taken off first so a play session started with "Reload Domain" switched off never listens twice.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
}

/// <summary>
/// Put this in each gameplay scene (on its own empty object, next to LocalCoopManager if you like).
/// It sets the starting values of the three rules and can show three on-screen toggle buttons.
/// You can also tick the checkboxes below while the game is running to try each rule out.
///
/// The starting values are only used the FIRST time a manager wakes up in a play session, so a rule changed with a
/// button isn't reset when the next scene loads. Buttons are tapped by code: leave their OnClick empty.
/// </summary>
[DisallowMultipleComponent]
public class CoopRulesManager : MonoBehaviour
{
    [Header("Rules (starting values; also live while playing)")]
    [Tooltip("ON: a hit on one player hurts every player. OFF: each player only loses their own health.")]
    public bool sharedDamage = false;
    [Tooltip("ON: one team score that both players see. OFF: each player keeps their own score.")]
    public bool sharedScore = false;
    [Tooltip("ON: only ONE player counts (the one below). The other takes no damage and earns no score. Always on when Player 2 is switched off.")]
    public bool singlePlayerMode = false;
    [Tooltip("Which player counts while Single Player Mode is on.")]
    [Range(1, 4)] public int singlePlayerTarget = 1;
    [Tooltip("ON: swords and sword upgrades are shared, so one player unlocking a sword unlocks it for everybody. OFF (default): each player has their own collection.")]
    public bool sharedWeapons = false;

    [Header("Score")]
    [Tooltip("ON (default): the score goes back to 0 whenever a scene is loaded fresh (leaving for another scene and coming back, a restart, the menu). " +
             "The trip to the Shop and back is the one exception, so the money earned before the Shop is still there afterwards. " +
             "OFF: the score is kept between scenes, as it was before.")]
    public bool resetScoreOnSceneChange = true;

    [Header("Optional on-screen buttons (tapped by code, leave OnClick empty)")]
    public DirectButton sharedDamageButton = new DirectButton();
    public DirectButton sharedScoreButton = new DirectButton();
    public DirectButton singlePlayerButton = new DirectButton();
    public DirectButton sharedWeaponsButton = new DirectButton();
    public string sharedDamageLabel = "Shared Damage";
    public string sharedScoreLabel = "Shared Score";
    public string singlePlayerLabel = "Single Player Mode";
    public string sharedWeaponsLabel = "Shared Weapons";

    private void Awake()
    {
        CoopScore.ResetOnSceneChange = resetScoreOnSceneChange;   // before the scene-loaded callback, which is what reads it
        CoopRules.Initialize(sharedDamage, sharedScore, singlePlayerMode, singlePlayerTarget, sharedWeapons);
        CopyToFields();   // show what the rules really are (they may have been set in an earlier scene)
    }

    private void OnEnable()
    {
        CoopRules.Changed += Refresh;
        LocalCoop.Changed += CoopToggled;
    }

    private void OnDisable()
    {
        CoopRules.Changed -= Refresh;
        LocalCoop.Changed -= CoopToggled;
    }

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        // A checkbox ticked in the Inspector while playing. (Every change made in code or by a button is copied back
        // into these fields straight away, so a difference here can only be an Inspector edit.)
        CoopRules.SharedDamage = sharedDamage;
        CoopRules.SharedScore = sharedScore;
        CoopRules.SinglePlayerMode = singlePlayerMode;
        CoopRules.SinglePlayerTarget = singlePlayerTarget;
        CoopRules.SharedWeapons = sharedWeapons;

        if (sharedDamageButton.WasTapped()) CoopRules.SharedDamage = !CoopRules.SharedDamage;
        if (sharedScoreButton.WasTapped()) CoopRules.SharedScore = !CoopRules.SharedScore;
        if (singlePlayerButton.WasTapped()) CoopRules.SinglePlayerMode = !CoopRules.SinglePlayerMode;
        if (sharedWeaponsButton.WasTapped()) CoopRules.SharedWeapons = !CoopRules.SharedWeapons;
    }

    // Player 2 switched on or off: anything watching CoopRules.Changed (score text, health bars) should re-read.
    private void CoopToggled(bool on)
    {
        CoopRules.NotifyChanged();
    }

    private void CopyToFields()
    {
        sharedDamage = CoopRules.SharedDamage;
        sharedScore = CoopRules.SharedScore;
        singlePlayerMode = CoopRules.SinglePlayerMode;
        singlePlayerTarget = CoopRules.SinglePlayerTarget;
        sharedWeapons = CoopRules.SharedWeapons;
    }

    private void Refresh()
    {
        CopyToFields();

        sharedDamageButton.SetLabel(sharedDamageLabel + (CoopRules.SharedDamage ? ": On" : ": Off"));
        sharedScoreButton.SetLabel(sharedScoreLabel + (CoopRules.SharedScore ? ": On" : ": Off"));
        singlePlayerButton.SetLabel(singlePlayerLabel + (CoopRules.SinglePlayerMode ? ": On" : ": Off"));
        sharedWeaponsButton.SetLabel(sharedWeaponsLabel + (CoopRules.SharedWeapons ? ": On" : ": Off"));
    }
}
