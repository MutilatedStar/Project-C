using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// A door that takes the team to the next floor. It replaces the stairs.
///
/// SET UP (once, on the door PREFAB, so every copy of the door works and the copies can have any names):
///   1. Put this script on the door prefab.
///   2. Give the door a Collider 2D with Is Trigger ticked, sized to the spot players should stand on.
///   3. (Optional) drag the door's sprite(s) into Sprites. Left empty, every SpriteRenderer under the door is tinted.
/// Nothing in the scene needs to be told about the door: it finds the FloorController by itself.
///
/// WHAT IT DOES
///   - Whoever stands on the door tints it with their colour: blue for Player 1, orange for Player 2 (Players 3 and 4
///     have colours too, for later). Two players but not everybody: the colours are mixed.
///   - When EVERY player that counts is on the door (CoopRules.Counts, so in single player mode it is just the one
///     player, and a dead player is not waited for), the door tells FloorController to go to the next floor.
///     The door then shows the Everyone Colour while the screen fades.
///   - Where each player lands is set on FloorController, in its Floors list (one empty object per player per floor).
///     It is NOT set on the door, so the same prefab works on every floor.
///
/// A door only fires when everyone has just ARRIVED on it. Players who land on a door after a floor change (or start the
/// scene standing on one) have to step off it first, so a door can't send them straight on again.
///
/// Action = Shop makes a door that opens the Shop scene instead of going down (this is what the stairs' Shop button did).
/// </summary>
[DisallowMultipleComponent]
public class FloorDoor : MonoBehaviour
{
    public enum DoorAction { NextFloor, Shop }

    private const int MaxPlayers = 4;

    [Header("What the door does")]
    [Tooltip("Next Floor: the normal door. Shop: a door that opens the Shop scene instead (set on the door in the scene, not the prefab).")]
    [SerializeField] private DoorAction action = DoorAction.NextFloor;

    [Header("Colours")]
    [Tooltip("The pictures to tint. Leave EMPTY to tint every SpriteRenderer under this door. Fill it in to leave a frame or shadow untinted.")]
    [SerializeField] private SpriteRenderer[] sprites;
    [SerializeField] private Color player1Color = new Color(0.25f, 0.55f, 1f, 1f);     // blue
    [SerializeField] private Color player2Color = new Color(1f, 0.6f, 0.15f, 1f);      // orange
    [SerializeField] private Color player3Color = new Color(0.35f, 0.9f, 0.4f, 1f);    // green (spare)
    [SerializeField] private Color player4Color = new Color(0.75f, 0.4f, 0.95f, 1f);   // purple (spare)
    [Tooltip("The colour while every player that counts is on the door, just before the floor changes. (Not used in single player.)")]
    [SerializeField] private Color everyoneColor = new Color(1f, 1f, 0.7f, 1f);
    [Tooltip("How strongly the colour covers the door's own picture. 1 = fully the player's colour, 0 = no tint.")]
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.8f;
    [Tooltip("How fast the colour changes. Higher = quicker.")]
    [SerializeField, Min(0.1f)] private float colourSpeed = 10f;

    private Collider2D zone;
    private SpriteRenderer[] targets;
    private Color[] originals;
    private Color overlay = new Color(1f, 1f, 1f, 0f);   // rgb = the hue shown, a = how much of it covers the picture

    private readonly Collider2D[] buffer = new Collider2D[24];
    private readonly bool[] present = new bool[MaxPlayers + 1];   // index = player number
    private FloorController controller;
    private float nextFind;
    private bool armed;   // false until the door has been seen without everybody on it

    // ------------------------------------------------------------ setup

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

    private void OnEnable()
    {
        FloorController.FloorChanged += HandleFloorChanged;
    }

    private void OnDisable()
    {
        FloorController.FloorChanged -= HandleFloorChanged;
        RestoreColours();
    }

    // Everyone was just moved: this door must see the floor empty before it can fire again.
    private void HandleFloorChanged(int floor)
    {
        armed = false;
    }

    // ------------------------------------------------------------ every frame

    private void Update()
    {
        if (zone == null) return;

        int required;
        int here;
        Scan(out required, out here);
        bool everyoneHere = required > 0 && here == required;

        if (!everyoneHere) armed = true;
        else if (armed && TryUse()) armed = false;

        UpdateColour(required, here, everyoneHere);
    }

    // Fills present[] with the players standing in the door, and counts how many players the door is waiting for.
    private void Scan(out int required, out int here)
    {
        System.Array.Clear(present, 0, present.Length);

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;   // a player whose only collider is a trigger still counts

        int hits = zone.Overlap(filter, buffer);
        for (int i = 0; i < hits; i++)
        {
            Collider2D c = buffer[i];
            if (c == null) continue;

            CoopHealth who = c.GetComponentInParent<CoopHealth>();
            if (who == null) continue;

            int number = who.PlayerNumber;
            if (number >= 1 && number <= MaxPlayers) present[number] = true;
        }

        required = 0;
        here = 0;
        for (int n = 1; n <= MaxPlayers; n++)
        {
            CoopHealth p = CoopHealth.Get(n);
            if (p == null || !p.IsAlive || !CoopRules.Counts(n))
            {
                present[n] = false;   // not in the game, dead, or not counting: not waited for, and does not tint the door
                continue;
            }

            required++;
            if (present[n]) here++;
        }
    }

    private bool TryUse()
    {
        if (controller == null)
        {
            if (Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 0.5f;
                controller = FindAnyObjectByType<FloorController>();
            }
            if (controller == null) return false;
        }
        return controller.UseDoor(action);   // false while a floor change or a menu is running: it simply asks again next frame
    }

    // ------------------------------------------------------------ colour

    private void UpdateColour(int required, int here, bool everyoneHere)
    {
        // Nobody on the door: fade the tint out, keeping the last hue while it goes.
        Color desired = overlay;
        desired.a = 0f;

        if (here > 0)
        {
            if (everyoneHere && required > 1)
            {
                desired = everyoneColor;
            }
            else
            {
                Vector3 sum = Vector3.zero;
                int count = 0;
                for (int n = 1; n <= MaxPlayers; n++)
                {
                    if (!present[n]) continue;
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
