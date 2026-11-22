using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ilumisoft.HealthSystem;

/// <summary>
/// One score button (or any score text) per player, set up exactly like CoopHealthbarBinder: YOU make the button and the text the way you
/// like, and this script feeds it that player's Coop Score, shows it only while that player is in the game (and counts), and lets the
/// score layout (CoopScoreLayout: Free / Mirror / Auto Snap) place it, the same way the health bars are placed.
///
/// SET UP, once for each player:
///   1. Make an empty parent object in the Canvas (this script goes here, and it must stay switched ON).
///   2. Put your score button (the object that holds the score text) as a CHILD of it.
///   3. Drag that child into Score Object (or leave it empty: the first child with a text on it is found by itself) and set Player Number.
///   For Player 2, either duplicate Player 1's parent object (Ctrl+D) and set Player Number 2, or make a second empty parent and drag
///   the SAME button into its Score Object: it is copied under the second parent by itself.
///   4. Add ONE CoopScoreLayout to the scene (it places the two score buttons like CoopHealthbarLayout places the health bars).
///   5. The auto-built score plates (CoopScorePanel) are still there, so tick Hide Auto Score on both of them, or delete them.
///      Otherwise each player has two scores on screen (the Console warns about it).
///
/// THE TEXT: Format decides the words ("Score: {0}"). The number is Coop Score: with Shared Score OFF each player's button shows THEIR
/// points, with Shared Score ON both show the team total (and Shared Format is used). A player who doesn't count reads 0 and hides.
///
/// If your score text was also fed by ScoreController (the old single team score), this script wins: it writes the text after everything
/// else every frame, so the number you see is always Coop Score. You can clear ScoreController's text slot to be tidy.
///
/// The button itself is not touched (its clicks, colours and animations keep working); only its text changes.
///
/// IF THE BUTTON NEVER APPEARS: no CoopHealth with this Player Number is in the game (the player prefab needs CoopHealth and the
/// PlayerInputSource Player Number must match), or Player 2 is switched off (LocalCoopManager). A message in the Console says which, after 3 seconds.
/// </summary>
[DisallowMultipleComponent]
public class CoopScoreBinder : CoopHudPanel
{
    [Header("Your score button")]
    [Tooltip("Your score button or text: the object that holds it. It must be a CHILD object, not this one: this object stays on while the " +
             "button is hidden. Leave empty to use the first child that has a text on it.")]
    public GameObject scoreObject;
    [Tooltip("The TextMeshPro text that shows the number. Leave empty to use the first one under the Score Object.")]
    public TMP_Text scoreText;
    [Tooltip("An old-style Legacy text instead. Only used if there is no TextMeshPro text.")]
    public Text legacyText;

    [Header("What it says")]
    [Tooltip("The words around the number. {0} becomes the score. Examples: \"Score: {0}\"   \"P1  {0}\"   \"{0}\". Empty = just the number.")]
    public string format = "Score: {0}";
    [Tooltip("Used instead of Format while Shared Score is ON (both players then show the one team number). Empty = the same as Format.")]
    public string sharedFormat = "Team Score: {0}";

    public override HudGroup Group { get { return HudGroup.Score; } }

    /// <summary>True while the button is showing (that player is in the game).</summary>
    public override bool IsShown { get { return scoreObject != null && scoreObject.activeSelf; } }

    /// <summary>Why the button is hidden: it only shows for a player that has a CoopHealth and counts.</summary>
    public override string HiddenReason
    {
        get
        {
            if (CoopHealth.Get(playerNumber) == null)
            {
                return "no CoopHealth with Player Number " + playerNumber + " is in the game (the player prefab needs CoopHealth, " +
                       "and its PlayerInputSource Player Number must match).";
            }

            if (!CoopRules.Counts(playerNumber))
            {
                return "CoopRules says Player " + playerNumber + " does not count right now (Single Player Mode is on for the other player, " +
                       "or Player 2 is switched off in LocalCoopManager).";
            }

            return "shown normally";
        }
    }

    // The button's own rectangle is its size, unless a Footprint is set.
    protected override RectTransform DefaultFootprint { get { return scoreObject != null ? scoreObject.transform as RectTransform : null; } }
    protected override Transform MeasureRoot { get { return scoreObject != null ? scoreObject.transform : transform; } }

    // The layout moves the button itself when it is not inside this object, so it can't be left behind.
    public override Transform SlotTransform
    {
        get { return (scoreObject != null && !scoreObject.transform.IsChildOf(transform)) ? scoreObject.transform : transform; }
    }

    /// <summary>The score binder for this player number, or null.</summary>
    public static CoopScoreBinder Get(int playerNumber)
    {
        for (int i = 0; i < All.Count; i++)
        {
            CoopScoreBinder binder = All[i] as CoopScoreBinder;
            if (binder != null && binder.playerNumber == playerNumber) return binder;
        }
        return null;
    }

    private float warnAt;
    private bool everShown;
    private bool warned;
    private float shownAt = -1f;
    private bool checkedDuplicate;

    private string label;
    private int shownScore;
    private bool shownShared;
    private bool dirty = true;   // rewrite on the next frame (first frame, or a setting was changed in the Inspector)

    private void Awake()
    {
        warnAt = Time.unscaledTime + 3f;

        if (scoreObject == null) scoreObject = FindScoreChild();

        if (scoreObject == null)
        {
            Debug.LogError("CoopScoreBinder on '" + name + "': there is no score button to feed. Put your score button (the object with the score text) " +
                           "as a CHILD of this object, or drag it into Score Object.", this);
            enabled = false;
            return;
        }

        // Hiding the button would hide this script too if the button is this object or one of its parents, and the button would then
        // vanish for good. Refuse, and say so.
        if (scoreObject == gameObject || transform.IsChildOf(scoreObject.transform))
        {
            Debug.LogError("CoopScoreBinder on '" + name + "': the Score Object is this object (or a parent of it). This object has to stay switched on " +
                           "while the button is hidden, so the button must be a CHILD. Make an empty parent, put this script on the parent and the " +
                           "button under it. The button is left as it is.", this);
            enabled = false;
            return;
        }

        AdoptScoreObject();

        if (scoreText == null && legacyText == null)
        {
            scoreText = scoreObject.GetComponentInChildren<TMP_Text>(true);
            if (scoreText == null) legacyText = scoreObject.GetComponentInChildren<Text>(true);
        }

        if (scoreText == null && legacyText == null)
        {
            Debug.LogWarning("CoopScoreBinder (Player " + playerNumber + ") on '" + name + "': no text was found under '" + scoreObject.name +
                             "', so there is nowhere to write the score. Put a TextMeshPro text under the button, or drag one into Score Text.", this);
        }

        // Hidden until the player exists.
        scoreObject.SetActive(false);
    }

    // A setting changed in the Inspector (also while playing): rewrite on the next frame.
    private void OnValidate()
    {
        dirty = true;
    }

    // The first child that has a text under it (or simply the first child).
    private GameObject FindScoreChild()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponentInChildren<TMP_Text>(true) != null || child.GetComponentInChildren<Text>(true) != null) return child.gameObject;
        }
        return transform.childCount > 0 ? transform.GetChild(0).gameObject : null;
    }

    // The score button has to be a live object in the HUD canvas, under this object. Mix-ups are repaired here (the Console says which):
    //   - the slot holds the button PREFAB from the Project window: an asset is never drawn, so a copy is made under this object;
    //   - the slot holds the button of ANOTHER score binder: this player gets a copy of its own (this is how one button serves both players);
    //   - the slot holds a button in the HUD canvas that is simply not under this object: it is moved in.
    private void AdoptScoreObject()
    {
        bool isAsset = !scoreObject.scene.IsValid();
        if (!isAsset && scoreObject.transform.IsChildOf(transform)) return;   // set up the way it is meant to be

        CoopScoreBinder owner = scoreObject.GetComponentInParent<CoopScoreBinder>();
        bool belongsToOther = owner != null && owner != this;

        Canvas holder = scoreObject.GetComponentInParent<Canvas>();
        Canvas hud = holder != null ? holder.rootCanvas : null;
        bool inHudCanvas = !isAsset && !belongsToOther && hud != null && hud.renderMode == RenderMode.ScreenSpaceOverlay;

        string what;
        if (inHudCanvas)
        {
            scoreObject.transform.SetParent(transform, false);
            what = "The button was in the HUD canvas but not under this object, so it was moved under it.";
        }
        else
        {
            GameObject original = scoreObject;
            scoreObject = Instantiate(original, transform);
            scoreObject.name = original.name;

            // The copy's text is the copy's own, never the original's.
            scoreText = null;
            legacyText = null;

            Transform made = scoreObject.transform;
            made.localPosition = Vector3.zero;
            made.localRotation = Quaternion.identity;

            if (isAsset) what = "The Score Object slot held the PREFAB from the Project window, not an object in the scene (an asset is never drawn), so a copy was made under this object.";
            else if (belongsToOther) what = "The Score Object slot holds the button of another score binder ('" + PathOf(owner.transform) + "', Player " + owner.playerNumber + "), so this player got a copy of its own under this object.";
            else what = "The Score Object slot held a button that is not in the Screen Space - Overlay HUD canvas, so a copy was made under this object. The original is left alone.";
        }

        Debug.LogWarning("CoopScoreBinder (Player " + playerNumber + ") on '" + PathOf(transform) + "': " + what +
                         " Set it up once for good by making the button a CHILD of this object in the Hierarchy.", this);
    }

    private void Update()
    {
        if (scoreObject == null) return;   // destroyed at runtime

        bool show = CoopHealth.Get(playerNumber) != null && CoopRules.Counts(playerNumber);
        if (scoreObject.activeSelf != show) scoreObject.SetActive(show);

        if (show)
        {
            everShown = true;
            if (shownAt < 0f) shownAt = Time.unscaledTime;
            if (!checkedDuplicate && Time.unscaledTime >= shownAt + 1.5f) WarnIfDoubled();
        }
        else if (!everShown && !warned && Time.unscaledTime >= warnAt)
        {
            ExplainMissing();
        }
    }

    // After everything else has had its say this frame (ScoreController, the old team score, may write the same text).
    private void LateUpdate()
    {
        if (scoreObject == null || !scoreObject.activeSelf) return;

        int score = CoopScore.Of(playerNumber);
        bool shared = CoopRules.SharedScore;

        if (dirty || label == null || score != shownScore || shared != shownShared)
        {
            dirty = false;
            shownScore = score;
            shownShared = shared;

            string pattern = shared && !string.IsNullOrEmpty(sharedFormat) ? sharedFormat : format;
            label = string.IsNullOrEmpty(pattern) ? score.ToString() : pattern.Replace("{0}", score.ToString());
        }

        if (scoreText != null)
        {
            if (scoreText.text != label) scoreText.text = label;
        }
        else if (legacyText != null)
        {
            if (legacyText.text != label) legacyText.text = label;
        }
    }

    // Two scores for one player on screen: this button and the auto-built plate. Say so once, and say what to tick.
    private void WarnIfDoubled()
    {
        checkedDuplicate = true;

        CoopScorePanel auto = CoopScorePanel.Get(playerNumber);
        if (auto == null || !auto.IsShown) return;

        Debug.LogWarning("CoopScoreBinder (Player " + playerNumber + "): Player " + playerNumber + " now has TWO scores on screen, this button and the auto-built " +
                         "score plate on '" + PathOf(auto.transform) + "'. Tick Hide Auto Score on that CoopScorePanel (or delete it) to keep only your button.", auto);
    }

    private void ExplainMissing()
    {
        warned = true;

        // Player 2 hidden because two-player mode is off, or a single-player rule is on: that is the design, not a fault.
        CoopHealth player = CoopHealth.Get(playerNumber);
        if (player != null && !CoopRules.Counts(playerNumber)) return;
        if (player == null && playerNumber > 1 && !LocalCoop.Enabled) return;

        if (player == null)
        {
            Debug.LogWarning("CoopScoreBinder (Player " + playerNumber + "): no CoopHealth with Player Number " + playerNumber + " is in the game, so the score " +
                             "button stays hidden. Check that the player prefab has CoopHealth and that the PlayerInputHub gave a character Player " +
                             playerNumber + "'s slot" + (playerNumber > 1 ? ", and that Player 2 is switched on (LocalCoopManager)." : "."), this);
        }
    }

    private static string PathOf(Transform t)
    {
        if (t == null) return "(none)";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
