using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Ilumisoft.HealthSystem;
using Ilumisoft.HealthSystem.UI;

/// <summary>
/// One health bar per player. Player 2 is usually spawned from a prefab, and a bar in the scene can't point at a
/// prefab that doesn't exist yet, so this finds the player by number at runtime instead:
///   - the bar appears when that player is in the game (and counts), and hides when they aren't
///   - it re-connects by itself if the player is destroyed and spawned again
///
/// SET UP, once for each player:
///   1. Make an empty parent object in the Canvas (this script goes here, and it must stay switched ON).
///   2. Put the bar (the object with Healthbar or PlayerHealthbar on it) as a CHILD of it. Leave the bar's Health
///      slot (and PlayerHealthbar's Player slot) empty.
///   3. Drag that child into Healthbar below and set Player Number (1 or 2).
/// Works for Player 1 as well, so both bars behave the same.
///
/// IF THE BAR NEVER APPEARS, in this order (a message in the Console names the one that applies after 3 seconds):
///   1. The Healthbar slot is empty, or the bar is this object / a parent of it (must be a CHILD).
///   2. No CoopHealth with this Player Number is in the game: the player prefab needs CoopHealth (not plain Health)
///      and PlayerInputSource.Player Number must match. Player 2 also needs LocalCoop switched on.
///   3. The canvas is not Screen Space - Overlay, or its Sort Order is at or below the split-screen picture
///      (CoopHudPanel warns about both when the bar is enabled).
///
/// WHERE the bars sit on screen is handled by CoopHealthbarLayout (free / mirror / auto-snap). This script moves
/// nothing itself: it only shows and hides the bar and lets the layout script find it (through CoopHudPanel).
/// The bar's size is measured automatically, so leave the Footprint slot empty.
/// </summary>
public class CoopHealthbarBinder : CoopHudPanel
{
    [Tooltip("The Healthbar to feed. It must be a CHILD object, not this one: this object stays on while the bar is hidden.")]
    public Healthbar healthbar;

    public override HudGroup Group { get { return HudGroup.Health; } }

    /// <summary>True while the bar is showing (that player is in the game).</summary>
    public override bool IsShown { get { return healthbar != null && healthbar.gameObject.activeSelf; } }

    /// <summary>Why the bar is hidden: it only shows for a player that has a CoopHealth and counts.</summary>
    public override string HiddenReason
    {
        get
        {
            CoopHealth player = CoopHealth.Get(playerNumber);
            if (player == null)
            {
                string found = "";
                for (int i = 0; i < CoopHealth.Players.Count; i++)
                {
                    found += (i > 0 ? ", " : "") + "Player " + CoopHealth.Players[i].PlayerNumber;
                }

                return "no CoopHealth with Player Number " + playerNumber + " is in the game. CoopHealth components found: " +
                       (CoopHealth.Players.Count == 0 ? "none" : found) +
                       ". The player prefab needs CoopHealth (not the plain Health component), and its PlayerInputSource Player Number must match.";
            }

            if (!CoopRules.Counts(playerNumber))
            {
                return "CoopRules says Player " + playerNumber + " does not count right now (Single Player Mode is on for the other player, " +
                       "or Player 2 is switched off in LocalCoopManager).";
            }

            return "shown normally";
        }
    }

    // The bar's own rectangle is its size, unless a Footprint is set. (Leave Footprint empty: if the bar's rectangle turns out to be
    // stretched across the screen, the pictures and text drawn inside the bar are measured instead.)
    protected override RectTransform DefaultFootprint { get { return healthbar != null ? healthbar.transform as RectTransform : null; } }
    protected override Transform MeasureRoot { get { return healthbar != null ? healthbar.transform : transform; } }

    // The layout moves the bar itself when it is not inside this object, so it can't be left behind.
    public override Transform SlotTransform
    {
        get { return (healthbar != null && !healthbar.transform.IsChildOf(transform)) ? healthbar.transform : transform; }
    }

    /// <summary>The binder for this player number, or null.</summary>
    public static CoopHealthbarBinder Get(int playerNumber)
    {
        return Get(HudGroup.Health, playerNumber) as CoopHealthbarBinder;
    }

    [Tooltip("ON: about 1.5 seconds after the bar first appears, print one 'HEALTH BAR REPORT' (a yellow warning) to the Console: what the bar is " +
             "showing and, if it is invisible, the likely reasons. You can also right-click this component's header > Print health bar report.")]
    public bool printReport = true;

    private float warnAt;
    private bool everShown;
    private bool warned;

    private float shownAt = -1f;
    private bool reported;
    private int switches;   // how many times this script has switched the bar on or off (about 1 is normal)

    private void Awake()
    {
        warnAt = Time.unscaledTime + 3f;

        // The bar is meant to be a child of this object: find it by itself instead of needing it dragged in.
        if (healthbar == null) healthbar = GetComponentInChildren<Healthbar>(true);

        if (healthbar == null)
        {
            Debug.LogError("CoopHealthbarBinder on '" + name + "': the Healthbar slot is empty, so nothing can be shown. " +
                           "Drag the bar (a CHILD of this object) into it.", this);
            enabled = false;
            return;
        }

        // Hiding the bar would hide this script too if the bar is this object or one of its parents, and the bar would
        // then vanish for good. Refuse to hide it and say so, instead of silently disappearing.
        if (healthbar.gameObject == gameObject || transform.IsChildOf(healthbar.transform))
        {
            Debug.LogError("CoopHealthbarBinder on '" + name + "': the Healthbar is this object (or a parent of it). This object has to " +
                           "stay switched on while the bar is hidden, so the bar must be a CHILD. Make an empty parent, put this script " +
                           "on the parent and the bar under it. The bar is left as it is.", this);
            enabled = false;
            return;
        }

        // The bar has to be a live object in the HUD canvas. Two easy mix-ups are repaired here (the Console says which).
        AdoptBar();

        // A bar in the screen HUD never turns to face the camera (that is for bars floating above characters): turned the way
        // the camera looks, a HUD bar can end up edge-on, which draws nothing at all.
        healthbar.AlignsWithCamera = false;

        // Hidden until the player exists, so the bar never runs without a health to read.
        healthbar.gameObject.SetActive(false);
    }

    // The layout can only place (and you can only see) a bar that lives in the HUD canvas, under this object. Two mix-ups put it
    // somewhere else, and then the layout "snaps" a bar that is not on the screen at all:
    //   - the slot holds the Healthbar PREFAB from the Project window instead of an object in the scene: that is an asset, it is never drawn;
    //   - the slot holds a WORLD bar (the Healthbar that floats above a character, in a World Space canvas): the layout would move it to
    //     the corner of the screen measured in WORLD units, a long way from anything the camera looks at.
    // A bar that is in the HUD canvas but not under this object is simply moved in. Anything else gets a HUD copy, and the original is left alone.
    private void AdoptBar()
    {
        bool isAsset = !healthbar.gameObject.scene.IsValid();
        if (!isAsset && healthbar.transform.IsChildOf(transform)) return;   // set up the way it is meant to be

        // A bar sitting under ANOTHER player's binder is that player's bar: taking it would leave them without one.
        CoopHealthbarBinder owner = healthbar.GetComponentInParent<CoopHealthbarBinder>();
        bool belongsToOther = owner != null && owner != this;

        Canvas holder = healthbar.GetComponentInParent<Canvas>();
        Canvas hud = holder != null ? holder.rootCanvas : null;
        bool inHudCanvas = !isAsset && !belongsToOther && hud != null && hud.renderMode == RenderMode.ScreenSpaceOverlay;

        string what;
        if (inHudCanvas)
        {
            healthbar.transform.SetParent(transform, false);
            what = "The bar was in the HUD canvas but not under this object, so it was moved under it.";
        }
        else
        {
            Healthbar original = healthbar;
            healthbar = Instantiate(original, transform);
            healthbar.name = original.name;

            Transform made = healthbar.transform;
            made.localPosition = Vector3.zero;
            made.localRotation = Quaternion.identity;

            if (isAsset)
            {
                what = "The Healthbar slot held the Healthbar PREFAB from the Project window, not an object in the scene (an asset is never drawn), so a copy was made under this object.";
            }
            else if (belongsToOther)
            {
                what = "The Healthbar slot holds the bar of another binder ('" + PathOf(owner.transform) + "', Player " + owner.playerNumber + "), so this player got a copy of its own under this object.";
            }
            else
            {
                what = "The Healthbar slot held a bar that is not in the HUD canvas (a world bar above a character, say: the layout would have moved it to the corner of the screen measured in WORLD units), so a copy was made under this object. The original is left alone.";
            }
            what += FitToHud();
        }

        Debug.LogWarning("CoopHealthbarBinder (Player " + playerNumber + ") on '" + PathOf(transform) + "': " + what +
                         " Set it up once for good by making the bar a CHILD of this object in the Hierarchy.", this);
    }

    // A bar made for the game world is about a metre wide: as a screen bar that is a dot. Give such a bar a normal HUD size (about 300 px wide).
    private string FitToHud()
    {
        Vector2 min, max;
        if (!MeasureBar(out min, out max)) return "";

        float width = max.x - min.x;
        if (float.IsNaN(width) || float.IsInfinity(width) || width >= 40f) return "";

        float factor = 300f / Mathf.Max(width, 0.0001f);
        healthbar.transform.localScale = healthbar.transform.localScale * factor;
        return " It was only " + Mathf.Max(1, Mathf.RoundToInt(width)) + " px wide on screen, so it was scaled up x" + Mathf.RoundToInt(factor) + " to about 300 px.";
    }

    private static readonly Vector3[] corners = new Vector3[4];

    // The area (screen pixels, Overlay canvas) covered by the bar's pictures, whether or not the bar is switched on.
    private bool MeasureBar(out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        bool any = false;
        Graphic[] all = healthbar.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < all.Length; i++)
        {
            all[i].rectTransform.GetWorldCorners(corners);
            for (int c = 0; c < 4; c++)
            {
                min = Vector2.Min(min, new Vector2(corners[c].x, corners[c].y));
                max = Vector2.Max(max, new Vector2(corners[c].x, corners[c].y));
            }
            any = true;
        }
        return any;
    }

    private void Update()
    {
        if (healthbar == null) return;   // destroyed at runtime

        CoopHealth player = CoopHealth.Get(playerNumber);
        bool show = player != null && CoopRules.Counts(playerNumber);

        if (show && healthbar.Health != player)
        {
            // A respawned player is a NEW object. Switch the bar off first so it re-reads the new health when it comes back on,
            // and connect BEFORE it is switched on: its Start reads the health straight away.
            if (healthbar.gameObject.activeSelf) { healthbar.gameObject.SetActive(false); switches++; }
            healthbar.Health = player;
        }

        if (healthbar.gameObject.activeSelf != show) { healthbar.gameObject.SetActive(show); switches++; }

        if (show)
        {
            everShown = true;
            if (shownAt < 0f) shownAt = Time.unscaledTime;

            // Give the bar a moment to start up and draw before looking at it.
            if (printReport && !reported && Time.unscaledTime >= shownAt + 1.5f)
            {
                reported = true;
                Debug.LogWarning(BuildReport(), this);
            }
        }
        else if (!everShown && !warned && Time.unscaledTime >= warnAt) ExplainMissingBar(player);
    }

    [ContextMenu("Print health bar report")]
    private void PrintReportNow()
    {
        Debug.LogWarning(BuildReport(), this);
    }

    // One message, after three seconds, naming the real reason the bar has not appeared (instead of guessing).
    private void ExplainMissingBar(CoopHealth player)
    {
        warned = true;

        // Player 2 hidden because two-player mode is off, or a single-player rule is on: that is the design, not a fault.
        if (player != null && !CoopRules.Counts(playerNumber)) return;
        if (player == null && playerNumber > 1 && !LocalCoop.Enabled) return;

        if (player == null)
        {
            Debug.LogWarning("CoopHealthbarBinder (Player " + playerNumber + "): no CoopHealth with Player Number " + playerNumber +
                             " is in the game. Check that the player prefab has CoopHealth (not the plain Health component), that the PlayerInputHub " +
                             "gave a character Player " + playerNumber + "'s slot (the console says which character got which slot)" +
                             (playerNumber > 1 ? ", and that Player 2 is switched on (LocalCoopManager)." : "."), this);
        }
    }

    // ------------------------------------------------------------ "why is nothing drawn?" report

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

    // Everything that can make a bar that is "there" draw nothing, read from the live bar, in one block of text to copy from the Console.
    private string BuildReport()
    {
        StringBuilder sb = new StringBuilder();
        List<string> causes = new List<string>();

        sb.AppendLine("HEALTH BAR REPORT - Player " + playerNumber + "   (binder '" + PathOf(transform) + "')   screen " + Screen.width + " x " + Screen.height);

        if (healthbar == null)
        {
            sb.AppendLine("The Healthbar slot is empty (or the bar was destroyed).");
            return sb.ToString();
        }

        Transform bar = healthbar.transform;
        sb.AppendLine("Bar: '" + PathOf(bar) + "'   switched on: " + healthbar.gameObject.activeInHierarchy + "   under this binder: " + bar.IsChildOf(transform) +
                      "   switched on/off by the binder " + switches + " time(s) (about 1 is normal)");
        if (switches > 3) causes.Add("the bar is being switched on and off over and over (" + switches + " times): another script is probably fighting the binder over it");

        // --- the numbers behind the fill
        HealthComponent health = healthbar.Health;
        if (health == null)
        {
            sb.AppendLine("Health: NONE assigned");
            causes.Add("the bar has no Health assigned, so it never updates");
        }
        else
        {
            sb.AppendLine("Health: '" + PathOf(health.transform) + "' (" + health.GetType().Name + ")   current " + health.CurrentHealth + "   max " + health.MaxHealth);
            if (health.MaxHealth <= 0f) causes.Add("Max Health is " + health.MaxHealth + ", so the fill is always empty (set Max Health on that player's CoopHealth)");
            else if (health.CurrentHealth <= 0f) causes.Add("Current Health is 0 (that player is dead, or never got its starting health), so the fill is empty");
        }

        // --- the bar's own settings
        Canvas inner = healthbar.BarCanvas;
        Image fill = healthbar.FillImage;
        sb.AppendLine("Healthbar settings: Hide Empty = " + healthbar.HideEmpty + "   Align With Camera = " + healthbar.AlignsWithCamera +
                      "   Canvas slot = " + (inner != null ? "'" + PathOf(inner.transform) + "' (switched on: " + inner.gameObject.activeInHierarchy + ")" : "EMPTY") +
                      "   Fill Image slot = " + (fill != null ? "'" + PathOf(fill.transform) + "'" : "EMPTY"));
        if (inner != null && !inner.gameObject.activeInHierarchy)
        {
            causes.Add("the bar's Canvas object ('" + inner.name + "') is switched OFF. Healthbar does that when health reads empty and Hide Empty is on");
        }

        if (fill == null)
        {
            causes.Add("the Healthbar's Fill Image slot is empty, so the fill can never change");
        }
        else
        {
            sb.AppendLine("Fill: type " + fill.type + ", fill amount " + fill.fillAmount.ToString("0.00") + ", sprite " + (fill.sprite != null ? fill.sprite.name : "NONE") +
                          ", colour alpha " + fill.color.a.ToString("0.00"));
            if (fill.fillAmount <= 0.001f) causes.Add("the Fill Image's fill amount is 0 (Healthbar sets it from the health numbers above)");
            if (fill.type != Image.Type.Filled) sb.AppendLine("   note: the Fill Image type is " + fill.type + " (it should be Filled, or Fill Amount does nothing)");
        }

        // --- where it is drawn: canvases
        SplitScreenCamera rig = SplitScreenCamera.Current;
        int composite = rig != null ? rig.CompositeCanvasSortingOrder : DynamicScreenCamera.DefaultCanvasSortingOrder;

        Canvas nearest = healthbar.GetComponentInParent<Canvas>();
        Canvas root = nearest != null ? nearest.rootCanvas : null;
        sb.AppendLine("Bar transform: position " + bar.position + "   world scale " + bar.lossyScale + "   world rotation " + bar.eulerAngles +
                      "   (the split-screen picture sorts at " + composite + ")");

        if (root == null)
        {
            causes.Add("the bar is not inside any Canvas, so it is never drawn");
        }
        else
        {
            float tilt = Vector3.Angle(bar.forward, root.transform.forward);
            if (tilt > 1f) causes.Add("the bar is turned " + Mathf.RoundToInt(tilt) + " degrees out of the screen plane; a quarter turn makes it edge-on and invisible");
        }

        List<Canvas> canvases = new List<Canvas>(healthbar.GetComponentsInChildren<Canvas>(true));
        Canvas[] above = healthbar.GetComponentsInParent<Canvas>(true);
        for (int i = 0; i < above.Length; i++)
        {
            if (!canvases.Contains(above[i])) canvases.Add(above[i]);
        }

        for (int i = 0; i < canvases.Count; i++)
        {
            Canvas c = canvases[i];
            sb.AppendLine("   canvas '" + PathOf(c.transform) + "': " + (c.isRootCanvas ? "ROOT" : "nested") + ", mode " + c.renderMode + ", enabled " + c.enabled +
                          ", override sorting " + c.overrideSorting + ", order " + c.sortingOrder + ", layer '" + c.sortingLayerName + "'");

            if (!c.enabled) causes.Add("the Canvas component on '" + c.name + "' is switched off, so nothing under it is drawn");
            if (c.isRootCanvas && c.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                causes.Add("'" + c.name + "' is its own " + c.renderMode + " canvas, not part of the Overlay HUD: the layout moves it in screen pixels, which in the world is nowhere near what the camera sees");
            }

            bool sortsInOverlay = (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) || c.overrideSorting;
            if (sortsInOverlay && c.sortingOrder <= composite) causes.Add("the canvas '" + c.name + "' sorts at " + c.sortingOrder + ", at or below the split-screen picture (" + composite + "), so the picture is drawn over it");
        }

        CanvasGroup[] groups = healthbar.GetComponentsInParent<CanvasGroup>(true);
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i].alpha < 0.01f) causes.Add("the Canvas Group on '" + PathOf(groups[i].transform) + "' has alpha " + groups[i].alpha + ", which hides everything under it");
        }

        // --- what is actually drawn, and how big it comes out on screen
        Graphic[] graphics = healthbar.GetComponentsInChildren<Graphic>(true);
        sb.AppendLine("Pictures and text under the bar (" + graphics.Length + "):");
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic g = graphics[i];
            g.rectTransform.GetWorldCorners(corners);

            Vector2 lo = new Vector2(corners[0].x, corners[0].y);
            Vector2 hi = lo;
            for (int c = 1; c < 4; c++)
            {
                lo = Vector2.Min(lo, new Vector2(corners[c].x, corners[c].y));
                hi = Vector2.Max(hi, new Vector2(corners[c].x, corners[c].y));
            }

            float w = hi.x - lo.x;
            float h = hi.y - lo.y;
            float alpha = g.color.a * g.canvasRenderer.GetInheritedAlpha();
            bool live = g.enabled && g.gameObject.activeInHierarchy;

            if (i < 10)
            {
                sb.AppendLine("   '" + PathOf(g.transform) + "' " + g.GetType().Name + (live ? "" : "  [OFF]") + "  " + w.ToString("0") + " x " + h.ToString("0") + " px at (" +
                              lo.x.ToString("0") + ", " + lo.y.ToString("0") + ")  alpha " + alpha.ToString("0.00") + "  culled " + g.canvasRenderer.cull);
            }

            if (!live) continue;
            if (w < 1.5f || h < 1.5f) causes.Add("'" + g.name + "' comes out only " + w.ToString("0") + " x " + h.ToString("0") + " px on screen (its scale or rotation squashes it)");
            if (alpha < 0.01f) causes.Add("'" + g.name + "' is fully transparent (alpha " + alpha.ToString("0.00") + ")");
            if (hi.x < 0f || lo.x > Screen.width || hi.y < 0f || lo.y > Screen.height) causes.Add("'" + g.name + "' is drawn outside the screen");
        }

        // --- other scripts on the bar (one of them may be driving or hiding it)
        List<string> scripts = new List<string>();
        Transform[] all = healthbar.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Component[] parts = all[i].GetComponents<Component>();
            for (int p = 0; p < parts.Length; p++)
            {
                if (parts[p] == null) { scripts.Add("MISSING SCRIPT on '" + all[i].name + "'"); continue; }
                if (!(parts[p] is MonoBehaviour)) continue;

                string ns = parts[p].GetType().Namespace ?? "";
                if (ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro")) continue;
                scripts.Add(parts[p].GetType().Name + " on '" + all[i].name + "'");
            }
        }
        sb.AppendLine("Scripts on the bar: " + (scripts.Count == 0 ? "none" : string.Join(", ", scripts.ToArray())));

        // --- verdict
        if (causes.Count == 0)
        {
            sb.AppendLine("VERDICT: nothing in the bar's own data explains an invisible bar. If the Game view still shows nothing, select the Fill object while the game runs and press F in the Scene view to see where it is drawn.");
        }
        else
        {
            sb.AppendLine("VERDICT: possible reasons nothing is drawn, in the order checked:");
            for (int i = 0; i < causes.Count; i++) sb.AppendLine("  " + (i + 1) + ". " + causes[i]);
        }
        return sb.ToString();
    }
}
