using System.Text;
using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// A temporary on-screen debug panel for finding out WHY the doors or the game-over screen do not fire. Nothing to set up: it makes
/// itself when you press Play (in the editor and in development builds only), and it changes nothing in the game.
///
///   F9    show / hide the panel
///   F10   also write the panel's text to the Console (easy to copy and paste)
///
/// It shows, live:
///   GAME OVER   every player (health, alive, counts, knocked-down state), whether the team counts as wiped, whether a LightsOutScreen
///               is switched on, and how many times the "team wiped" event has actually been raised.
///   DOORS       what is blocking the door check, whether the doors are armed, who is standing on which door, and whether the
///               door groups think they are ready.
///
/// When you are done, delete this file. Nothing else needs it.
/// </summary>
public class CoopDebugOverlay : MonoBehaviour
{
    private static bool visible = true;

    private int wipeCount;
    private float lastWipeAt = -1f;
    private string text = "";
    private float nextRefresh;
    private float nextSceneScan;
    private string lightsOutScan = "";
    private FloorController floor;
    private GUIStyle style;
    private Texture2D backdrop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        if (FindAnyObjectByType<CoopDebugOverlay>() != null) return;

        GameObject go = new GameObject("CoopDebugOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<CoopDebugOverlay>();
    }

    private void OnEnable()
    {
        CoopHealth.TeamWiped += HandleWiped;
    }

    private void OnDisable()
    {
        CoopHealth.TeamWiped -= HandleWiped;
    }

    private void HandleWiped()
    {
        wipeCount++;
        lastWipeAt = Time.unscaledTime;
    }

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(KeyCode.F9)) visible = !visible;
            if (Input.GetKeyDown(KeyCode.F10))
            {
                Rebuild();
                Debug.Log("CoopDebugOverlay (F10)\n" + text);
            }
        }
        catch (System.InvalidOperationException)
        {
            // The old Input Manager is switched off in this project: the panel just stays on.
        }

        if (!visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        Rebuild();
    }

    private void Rebuild()
    {
        var sb = new StringBuilder(1024);
        sb.Append("COOP DEBUG  (F9 hide, F10 copy to Console)   timeScale ").Append(Time.timeScale.ToString("0.##")).Append('\n');

        // ---- game over
        sb.Append("\nGAME OVER\n");
        if (CoopHealth.Players.Count == 0) sb.Append("   no CoopHealth is switched on in the scene\n");

        for (int i = 0; i < CoopHealth.Players.Count; i++)
        {
            CoopHealth p = CoopHealth.Players[i];
            if (p == null) { sb.Append("   (a destroyed CoopHealth is still in the list)\n"); continue; }

            CoopDowned downed = p.GetComponent<CoopDowned>();
            sb.Append("   ").Append(p.name).Append("  Player ").Append(p.PlayerNumber)
              .Append("  health ").Append(p.CurrentHealth.ToString("0.#"))
              .Append(p.IsAlive ? "  ALIVE" : "  at 0")
              .Append(CoopRules.Counts(p.PlayerNumber) ? "  counts" : "  does NOT count")
              .Append("  state ").Append(downed != null ? downed.CurrentState.ToString() : "(no CoopDowned)");
            if (p.Invincible || p.Airborne || p.Grace)
            {
                sb.Append("  blocking damage:");
                if (p.Invincible) sb.Append(" Invincible");
                if (p.Airborne) sb.Append(" Airborne");
                if (p.Grace) sb.Append(" Grace");
            }
            sb.Append('\n');
        }

        sb.Append("   team counts as wiped (AllCountedDead): ").Append(CoopHealth.AllCountedDead)
          .Append("   TeamWipeBlockers: ").Append(CoopHealth.TeamWipeBlockers).Append('\n');
        sb.Append("   LightsOutScreen.Available: ").Append(LightsOutScreen.Available)
          .Append("   IsActive: ").Append(LightsOutScreen.IsActive).Append('\n');
        sb.Append("   'team wiped' event raised ").Append(wipeCount).Append(" time(s)");
        if (lastWipeAt >= 0f) sb.Append(", last ").Append((Time.unscaledTime - lastWipeAt).ToString("0.0")).Append("s ago");
        sb.Append('\n');

        if (Time.unscaledTime >= nextSceneScan)
        {
            nextSceneScan = Time.unscaledTime + 1f;
            LightsOutScreen[] screens = FindObjectsByType<LightsOutScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var scan = new StringBuilder();
            scan.Append("   LightsOutScreen objects in the scene: ").Append(screens.Length);
            for (int i = 0; i < screens.Length; i++)
            {
                scan.Append("  ['").Append(screens[i].name).Append("' ").Append(screens[i].isActiveAndEnabled ? "on" : "OFF").Append(']');
            }
            scan.Append('\n');
            lightsOutScan = scan.ToString();
        }
        sb.Append(lightsOutScan);

        // ---- doors
        sb.Append("\nDOORS\n");
        if (floor == null) floor = FindAnyObjectByType<FloorController>();
        if (floor == null) sb.Append("   no FloorController in the scene\n");
        else sb.Append("   ").Append(floor.DebugReport()).Append('\n');

        text = sb.ToString();
    }

    private void OnGUI()
    {
        if (!visible || string.IsNullOrEmpty(text)) return;

        if (style == null)
        {
            backdrop = new Texture2D(1, 1);
            backdrop.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            backdrop.Apply();

            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 12;
            style.wordWrap = true;
            style.richText = false;
            style.normal.textColor = Color.white;
            style.normal.background = backdrop;
            style.alignment = TextAnchor.UpperLeft;
            style.padding = new RectOffset(8, 8, 6, 6);
        }

        float width = Mathf.Min(Screen.width - 20f, 760f);
        float height = style.CalcHeight(new GUIContent(text), width);
        GUI.Label(new Rect(10f, 10f, width, height), text, style);
    }

    private void OnDestroy()
    {
        if (backdrop != null) Destroy(backdrop);
    }
}
