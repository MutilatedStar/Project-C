using UnityEngine;
using UnityEngine.Events;
using Ilumisoft.HealthSystem;

/// <summary>
/// A gem (or coin, or anything worth points). A player who touches it adds its points to THEIR score (CoopScore), and the gem is hidden
/// straight away. The colours are FloorDoor's and PlayerGlow's, so a Player 1 gem is blue like Player 1.
///
/// WHO CAN TAKE IT (set per gem, so every gem can be different):
///   Everyone   any player can take it. It shows its OWN colour (Everyone Color), and the points go to whoever took it.
///   Player 1-4 only that player can take it. It is tinted in that player's colour, so everybody can see whose it is.
///   If the gem's player is not in the game (a single player game and a Player 2 gem), anyone may take it instead, so it can't get
///   stuck there. Untick Anyone If Player Missing to keep it for that player only.
///
/// POINTS: Points, 100 by default. Give a big gem 500, a tiny one 10, whatever you like.
///
/// SET UP:
///   1. Put this script on the gem prefab, with a Collider 2D (Is Trigger ticked) sized to where a player can grab it.
///   2. Pick Who Can Collect It and Points on the gem. (For a trigger to fire, the player or the gem needs a Rigidbody 2D. Players normally
///      have one for movement; if a gem is never taken, check that first.)
///   3. (Optional) drag the gem's sprite(s) into Sprites. Left empty, every SpriteRenderer under the gem is tinted.
///   4. (Optional) On Collected: hook up a sound or a sparkle. It gets the number of the player who took the gem.
///
/// THE SCORE: the points go to CoopScore.Add(points, playerNumber), the per-player score kept in CoopRules.cs, so the player who took the
/// gem is the one who earns them. The co-op rules then decide what everyone SEES (Shared Score on = the team total, off = their own), the
/// same as for points from kills. Gem points are fixed: they are not scaled by difficulty the way kill points are.
/// (ScoreController, the older single team score, is only touched if you tick Also Add To Score Controller.)
/// Dead players, and players that don't count in the current co-op rules (CoopRules.Counts), can't pick anything up.
/// </summary>
[DisallowMultipleComponent]
public class ScorePickup : MonoBehaviour
{
    /// <summary>Who may take the gem. The numbers match the player numbers (0 = everyone).</summary>
    public enum Collector { Everyone = 0, Player1 = 1, Player2 = 2, Player3 = 3, Player4 = 4 }

    private const int MaxPlayers = 4;

    [Header("Who and how much")]
    [Tooltip("Everyone: any player can take it and it shows its own colour. Player 1 to 4: only that player can take it, and it is tinted in their colour.")]
    [SerializeField] private Collector whoCanCollectIt = Collector.Everyone;
    [Tooltip("The score the player who takes it gets. 100 by default; make it bigger for a big gem.")]
    [SerializeField, Min(0)] private int points = 100;
    [Tooltip("On: when this gem's player is not in the game (a single player game), anyone can take it, so it can't get stuck there. Off: it stays for that player only.")]
    [SerializeField] private bool anyoneIfPlayerMissing = true;
    [Tooltip("Also add the points to ScoreController, the separate single team score (the one its TrySpend spends). Leave OFF if your score text and shop use CoopScore.")]
    [SerializeField] private bool alsoAddToScoreController = false;

    [Header("Colours")]
    [Tooltip("The pictures to tint. Leave EMPTY to tint every SpriteRenderer under this gem. Fill it in to leave a shine or shadow untinted.")]
    [SerializeField] private SpriteRenderer[] sprites;
    [SerializeField] private Color player1Color = new Color(0.25f, 0.55f, 1f, 1f);     // blue
    [SerializeField] private Color player2Color = new Color(1f, 0.6f, 0.15f, 1f);      // orange
    [SerializeField] private Color player3Color = new Color(0.35f, 0.9f, 0.4f, 1f);    // green (spare)
    [SerializeField] private Color player4Color = new Color(0.75f, 0.4f, 0.95f, 1f);   // purple (spare)
    [Tooltip("How strongly a player's colour covers the gem's own picture. 1 = fully the player's colour, 0 = no tint.")]
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.8f;
    [Tooltip("The colour of a gem anyone can take. Every gem has its own, so different gems can look different.")]
    [SerializeField] private Color everyoneColor = new Color(1f, 0.85f, 0.25f, 1f);    // gold
    [Tooltip("How strongly the Everyone colour covers the gem's own picture. 0 = leave the gem's own picture alone.")]
    [SerializeField, Range(0f, 1f)] private float everyoneTintStrength = 0.8f;

    [Header("When it is taken")]
    [Tooltip("The gem is hidden the moment it is taken. This is how many seconds later the object is removed (lets a sound finish playing).")]
    [SerializeField, Min(0f)] private float destroyAfter = 1f;
    [Tooltip("Runs when a player takes the gem. The number is the player who took it (1 = Player 1...). Hook up a sound or a sparkle here.")]
    [SerializeField] private UnityEvent<int> onCollected;

    /// <summary>Who may take this gem.</summary>
    public Collector WhoCanCollectIt { get { return whoCanCollectIt; } }

    /// <summary>The score it gives.</summary>
    public int Points { get { return points; } }

    /// <summary>True once a player has taken it.</summary>
    public bool IsCollected { get { return collected; } }

    private Collider2D zone;
    private SpriteRenderer[] targets;
    private Color[] originals;
    private bool collected;

    // ------------------------------------------------------------ setup

    private void Reset()
    {
        Collider2D c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    private void Awake()
    {
        zone = GetComponent<Collider2D>();
        if (zone == null)
        {
            Debug.LogError("ScorePickup on '" + name + "': it needs a Collider 2D (Is Trigger ticked) so players can touch it. " +
                           "Add one to the gem prefab. This gem is switched off until you do.", this);
            enabled = false;
            return;
        }
        if (!zone.isTrigger)
        {
            Debug.LogWarning("ScorePickup on '" + name + "': its Collider 2D is not a trigger, so players will bump into it instead of picking it up. " +
                             "Tick Is Trigger on the collider.", this);
        }

        targets = (sprites != null && sprites.Length > 0) ? sprites : GetComponentsInChildren<SpriteRenderer>();
        originals = new Color[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            originals[i] = targets[i] != null ? targets[i].color : Color.white;
        }

        ApplyTint();
    }

    /// <summary>For a spawner: sets who can take the gem and what it is worth, and re-colours it. Call it right after Instantiate.</summary>
    public void Configure(Collector who, int pointValue)
    {
        whoCanCollectIt = who;
        points = Mathf.Max(0, pointValue);
        if (targets != null) ApplyTint();
    }

    // ------------------------------------------------------------ colour

    private void ApplyTint()
    {
        bool forEveryone = whoCanCollectIt == Collector.Everyone;
        Color hue = forEveryone ? everyoneColor : ColourOf((int)whoCanCollectIt);
        float strength = forEveryone ? everyoneTintStrength : tintStrength;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            Color original = originals[i];
            Color mix = Color.Lerp(original, hue, strength);
            mix.a = original.a;   // never change the picture's own transparency
            targets[i].color = mix;
        }
    }

    private Color ColourOf(int playerNumber)
    {
        switch (playerNumber)
        {
            case 1: return player1Color;
            case 2: return player2Color;
            case 3: return player3Color;
            default: return player4Color;
        }
    }

    // ------------------------------------------------------------ taking it

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCollect(other);
    }

    // Also while standing on it: covers a gem that appears under a player, or a player who is revived on top of one.
    private void OnTriggerStay2D(Collider2D other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider2D other)
    {
        if (collected || !isActiveAndEnabled) return;

        CoopHealth who = other.GetComponentInParent<CoopHealth>();
        if (who == null) return;

        int number = who.PlayerNumber;
        if (number < 1 || number > MaxPlayers) return;
        if (!who.IsAlive || !CoopRules.Counts(number)) return;
        if (!CanTake(number)) return;

        Collect(number);
    }

    private bool CanTake(int number)
    {
        if (whoCanCollectIt == Collector.Everyone) return true;
        if ((int)whoCanCollectIt == number) return true;
        return anyoneIfPlayerMissing && !OwnerIsInGame();
    }

    // The gem's own player is in the game when they exist and count in the current co-op rules.
    private bool OwnerIsInGame()
    {
        int owner = (int)whoCanCollectIt;
        return CoopHealth.Get(owner) != null && CoopRules.Counts(owner);
    }

    private void Collect(int number)
    {
        collected = true;

        CoopScore.Add(points, number);   // note the order in CoopRules.cs: (amount, playerNumber)
        if (alsoAddToScoreController) ScoreController.AddScore(points);

        // Hidden at once, so it can't be taken twice or seen lingering; the object itself goes a moment later.
        zone.enabled = false;
        Renderer[] visible = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < visible.Length; i++) visible[i].enabled = false;

        if (onCollected != null) onCollected.Invoke(number);

        Destroy(gameObject, destroyAfter);
    }
}
