using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// One door on a floor. A floor has SEVERAL doors (normally one per player), and the team only goes down when every
/// available player is standing on a DIFFERENT door, so every available door has somebody on it. Everyone standing on the
/// same door does not count.
///
/// SET UP:
///   1. Put this script on the door prefab, with a Collider 2D (Is Trigger ticked) sized to where a player stands.
///   2. Put the doors of each floor in the Doors list of THAT floor, in FloorController's Floors list. That list is the
///      linker: the door itself knows nothing about floors, so the same prefab works on every floor, with any name.
///      A door that no floor lists does nothing (FloorController warns about it when the scene starts).
///   3. (Optional) drag the door's sprite(s) into Sprites. Left empty, every SpriteRenderer under the door is tinted.
///
/// THE RULE (checked by FloorController, every frame, for the doors of the floor the team is on; see CheckGroup):
///   - Available players: every player that counts (CoopRules.Counts) and is alive.
///   - Available doors: the doors of this floor that are switched on. Switch a door off to make it unavailable.
///   - Every available player must stand on a door, and the players must fill the doors: with 2 players and 2 doors, one
///     on each. With fewer players than doors (single player mode) only as many doors as there are players are needed.
///     A player standing across two doors is given to the nearest one only.
///
/// COLOUR: a door tints with the colour of whoever stands on it (blue = Player 1, orange = Player 2; Players 3 and 4 have
/// colours too). When the rule is met, every door of the group shows the Ready Colour while the screen fades.
///
/// Action = Shop makes a second group of doors on a floor: when THEY are all filled, the Shop opens instead of going down.
/// </summary>
[DisallowMultipleComponent]
public class FloorDoor : MonoBehaviour
{
    public enum DoorAction { NextFloor, Shop }

    private const int MaxPlayers = 4;

    [Header("What the door does")]
    [Tooltip("Next Floor: a normal door. Shop: the doors set to Shop on a floor form a second group that opens the Shop scene instead.")]
    [SerializeField] private DoorAction action = DoorAction.NextFloor;

    [Header("Colours")]
    [Tooltip("The pictures to tint. Leave EMPTY to tint every SpriteRenderer under this door. Fill it in to leave a frame or shadow untinted.")]
    [SerializeField] private SpriteRenderer[] sprites;
    [SerializeField] private Color player1Color = new Color(0.25f, 0.55f, 1f, 1f);     // blue
    [SerializeField] private Color player2Color = new Color(1f, 0.6f, 0.15f, 1f);      // orange
    [SerializeField] private Color player3Color = new Color(0.35f, 0.9f, 0.4f, 1f);    // green (spare)
    [SerializeField] private Color player4Color = new Color(0.75f, 0.4f, 0.95f, 1f);   // purple (spare)
    [Tooltip("The colour every door of the floor shows once all of them are filled, just before the floor changes. (Not used in single player.)")]
    [SerializeField] private Color readyColor = new Color(1f, 1f, 0.7f, 1f);
    [Tooltip("How strongly the colour covers the door's own picture. 1 = fully the player's colour, 0 = no tint.")]
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.8f;
    [Tooltip("How fast the colour changes. Higher = quicker.")]
    [SerializeField, Min(0.1f)] private float colourSpeed = 10f;

    /// <summary>What this door does when its group is filled.</summary>
    public DoorAction Action { get { return action; } }

    /// <summary>Bit n is set while Player n is the one standing on this door (set by CheckGroup).</summary>
    public int Occupants { get { return occupants; } }

    /// <summary>True while this door can be used: it is switched on and has its trigger.</summary>
    public bool IsAvailable { get { return isActiveAndEnabled && zone != null; } }

    private Collider2D zone;
    private SpriteRenderer[] targets;
    private Color[] originals;
    private Color overlay = new Color(1f, 1f, 1f, 0f);   // rgb = the hue shown, a = how much of it covers the picture
    private readonly Collider2D[] buffer = new Collider2D[24];
    private int occupants;
    private bool showReady;

    // Scratch space for CheckGroup (it runs once per frame, on the main thread only).
    private static readonly List<FloorDoor> group = new List<FloorDoor>();
    private static readonly bool[] available = new bool[MaxPlayers + 1];
    private static readonly int[] doorOf = new int[MaxPlayers + 1];
    private static readonly float[] nearest = new float[MaxPlayers + 1];

    // ------------------------------------------------------------ the floor check

    /// <summary>
    /// The floor checker. Looks at one floor's doors of one action and says whether the team may go through:
    /// every available player is on a door and the players fill the doors, one player per door.
    /// It also tells each door who is standing on it, so the doors can show their colours.
    /// Returns false when there are no available doors in the group or no available players.
    /// </summary>
    public static bool CheckGroup(FloorDoor[] doors, DoorAction action)
    {
        group.Clear();
        if (doors != null)
        {
            for (int i = 0; i < doors.Length; i++)
            {
                FloorDoor d = doors[i];
                if (d != null && d.action == action && d.IsAvailable) group.Add(d);
            }
        }
        if (group.Count == 0) return false;

        // The available players.
        int players = 0;
        for (int n = 1; n <= MaxPlayers; n++)
        {
            CoopHealth p = CoopHealth.Get(n);
            available[n] = p != null && p.IsAlive && CoopRules.Counts(n);
            if (available[n]) players++;
            doorOf[n] = -1;
            nearest[n] = float.MaxValue;
        }

        // Each player goes to the nearest door they are touching (so one player can't fill two doors).
        for (int i = 0; i < group.Count; i++)
        {
            FloorDoor d = group[i];
            d.occupants = 0;
            d.showReady = false;

            int touching = d.ScanOverlaps();
            Vector2 centre = d.zone.bounds.center;
            for (int n = 1; n <= MaxPlayers; n++)
            {
                if (!available[n] || (touching & (1 << n)) == 0) continue;

                float sq = ((Vector2)CoopHealth.Get(n).transform.position - centre).sqrMagnitude;
                if (sq < nearest[n])
                {
                    nearest[n] = sq;
                    doorOf[n] = i;
                }
            }
        }

        int placed = 0;
        for (int n = 1; n <= MaxPlayers; n++)
        {
            if (available[n] && doorOf[n] >= 0)
            {
                group[doorOf[n]].occupants |= 1 << n;
                placed++;
            }
        }

        int filled = 0;
        for (int i = 0; i < group.Count; i++)
        {
            if (group[i].occupants != 0) filled++;
        }

        // Everybody is on a door, and the players cover as many doors as there are (or as many as there are players).
        bool ready = players > 0 && placed == players && filled >= Mathf.Min(group.Count, players);
        if (ready && players > 1)
        {
            for (int i = 0; i < group.Count; i++) group[i].showReady = true;
        }
        return ready;
    }

    /// <summary>Makes every door in the list forget who was on it (colours fade out). Used when the floor changes.</summary>
    public static void ClearAll(FloorDoor[] doors)
    {
        if (doors == null) return;
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] == null) continue;
            doors[i].occupants = 0;
            doors[i].showReady = false;
        }
    }

    // Which players' colliders touch this door right now, as a bit mask (bit n = Player n).
    private int ScanOverlaps()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;   // a player whose only collider is a trigger still counts

        int hits = zone.Overlap(filter, buffer);
        int mask = 0;
        for (int i = 0; i < hits; i++)
        {
            Collider2D c = buffer[i];
            if (c == null) continue;

            CoopHealth who = c.GetComponentInParent<CoopHealth>();
            if (who == null) continue;

            int number = who.PlayerNumber;
            if (number >= 1 && number <= MaxPlayers) mask |= 1 << number;
        }
        return mask;
    }

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
            Debug.LogError("FloorDoor on '" + name + "': it needs a Collider 2D (Is Trigger ticked) to know where players stand. " +
                           "Add one to the door prefab. This door is switched off until you do.", this);
            enabled = false;
            return;
        }
        if (!zone.isTrigger)
        {
            Debug.LogWarning("FloorDoor on '" + name + "': its Collider 2D is not a trigger, so players can't stand 'in' it and it will not notice them. " +
                             "Tick Is Trigger on the collider.", this);
        }

        targets = (sprites != null && sprites.Length > 0) ? sprites : GetComponentsInChildren<SpriteRenderer>();
        originals = new Color[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            originals[i] = targets[i] != null ? targets[i].color : Color.white;
        }
    }

    private void OnDisable()
    {
        occupants = 0;
        showReady = false;
        RestoreColours();
    }

    // ------------------------------------------------------------ colour

    private void Update()
    {
        // Nobody on the door: fade the tint out, keeping the last hue while it goes.
        Color desired = overlay;
        desired.a = 0f;

        if (occupants != 0)
        {
            if (showReady)
            {
                desired = readyColor;
            }
            else
            {
                Vector3 sum = Vector3.zero;
                int count = 0;
                for (int n = 1; n <= MaxPlayers; n++)
                {
                    if ((occupants & (1 << n)) == 0) continue;
                    Color c = ColourOf(n);
                    sum += new Vector3(c.r, c.g, c.b);
                    count++;
                }
                sum /= count;
                desired = new Color(sum.x, sum.y, sum.z, 1f);
            }
            desired.a = tintStrength;
        }

        float k = 1f - Mathf.Exp(-colourSpeed * Time.deltaTime);
        overlay = Color.Lerp(overlay, desired, k);
        ApplyOverlay();
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

    private void ApplyOverlay()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            Color original = originals[i];
            Color mix = Color.Lerp(original, overlay, overlay.a);
            mix.a = original.a;   // never change the picture's own transparency
            targets[i].color = mix;
        }
    }

    private void RestoreColours()
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null) targets[i].color = originals[i];
        }
        overlay = new Color(1f, 1f, 1f, 0f);
    }
}
