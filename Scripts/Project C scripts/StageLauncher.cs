using System.Collections;
using UnityEngine;
using TMPro;
using Ilumisoft.HealthSystem;

/// <summary>
/// Put this ONCE in the Puzzle scene (on any always-active empty object). When the scene opens it:
///
///   1. reads the stage that was picked in the main menu (StageSelection),
///   2. turns the PVP toggle ON for a Pvp stage and OFF for a Puzzle stage (PvpMode.Enabled),
///   3. moves every player to that stage's spawn points (StageDefinition > P1 Spawn / P2 Spawn) and snaps the cameras onto them,
///   4. tells CoopDowned and LightsOutScreen about the new start, so a respawn or a Retry puts players back at THIS stage's start,
///   5. shows the stage name for a moment.
///
/// Nothing is picked (you pressed Play in the Puzzle scene itself)? Then nobody is moved, unless you fill "Test Stage Id" below.
/// "Pvp When No Stage" is the editor's own PVP checkbox for that case.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Stages/Stage Launcher")]
public class StageLauncher : MonoBehaviour
{
    [Header("What it does")]
    [Tooltip("Move the players to the stage's spawn points when the scene opens.")]
    [SerializeField] private bool placePlayers = true;
    [Tooltip("Show the stage name for a moment when it starts.")]
    [SerializeField] private bool showBanner = true;

    [Header("Testing in the editor (pressing Play right in this scene, no menu)")]
    [Tooltip("Stage to start on when nothing was picked in the menu. Use the stage's Id (its object name in lower case with dashes, e.g. pvp-arena-1 or stage-3). Empty = nobody is moved.")]
    [SerializeField] private string testStageId = "";
    [Tooltip("PVP toggle for testing: only used when no stage was picked in the menu AND Test Stage Id is empty.")]
    [SerializeField] private bool pvpWhenNoStage = false;

    /// <summary>The stage that is running right now (null if none was picked).</summary>
    public static StageDefinition Current { get; private set; }

    private IEnumerator Start()
    {
        Current = null;

        StageDefinition stage = StageDefinition.Find(StageSelection.StageId);
        if (stage == null && !StageSelection.HasSelection) stage = StageDefinition.Find(testStageId);

        bool pvp = stage != null ? stage.mode == StageMode.Pvp : (!StageSelection.HasSelection && pvpWhenNoStage);
        PvpMode.Enabled = pvp;
        if (pvp)
        {
            PvpMode.ApplyRules();
            PvpMode.RequireTwoPlayers();
        }

        Current = stage;
        if (stage == null)
        {
            if (StageSelection.HasSelection)
            {
                Debug.LogWarning("StageLauncher: the stage '" + StageSelection.StageId + "' is not in this scene (was it deleted?). Players stay where they are.", this);
            }
            yield break;
        }

        if (placePlayers)
        {
            // Wait until the players exist (Player 2 appears a moment later when PVP has just switched it on).
            int needed = pvp ? 2 : 1;
            float waited = 0f;
            while (waited < 3f && CoopHealth.Players.Count < needed)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;   // one more frame, so the player numbers are settled

            PlaceAll(stage);
        }

        if (showBanner)
        {
            StageBanner.Show(this, stage.Title, stage.mode == StageMode.Pvp ? "PVP ARENA" : "PUZZLE STAGE",
                             stage.mode == StageMode.Pvp ? StageUi.Orange : StageUi.Blue, 1.8f);
        }
    }

    private void PlaceAll(StageDefinition stage)
    {
        LightsOutScreen lightsOut = FindAnyObjectByType<LightsOutScreen>();
        CoopHealth[] players = CoopHealth.Players.ToArray();

        for (int i = 0; i < players.Length; i++)
        {
            CoopHealth p = players[i];
            if (p == null) continue;

            int number = p.PlayerNumber;
            Vector3 spot = stage.SpawnFor(number);
            MoveTo(p, spot);

            CoopDowned downed = p.GetComponent<CoopDowned>();
            if (downed != null) downed.SetOrigin(spot);               // where a blown-up player comes back
            if (lightsOut != null) lightsOut.SetStartPosition(number, spot);   // where Retry puts everybody
        }

        CoopDowned.EnterRoom(0);   // room 0 = "go back to the point of origin", which is now this stage's start
        Physics2D.SyncTransforms();
        if (SplitScreenCamera.Current != null) SplitScreenCamera.Current.SnapToPlayers();
    }

    private static void MoveTo(CoopHealth player, Vector3 spot)
    {
        Transform t = player.transform;
        spot.z = t.position.z;

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body == null) body = player.GetComponentInParent<Rigidbody2D>();
        if (body != null)
        {
            body.position = spot;
            body.linearVelocity = Vector2.zero;
        }
        t.position = spot;
    }
}

/// <summary>A big name that fades in and out in the middle of the screen. Built in code, nothing to set up.</summary>
public static class StageBanner
{
    public static void Show(MonoBehaviour runner, string title, string subtitle, Color accent, float hold)
    {
        if (runner == null) return;
        runner.StartCoroutine(Routine(title, subtitle, accent, hold));
    }

    private static IEnumerator Routine(string title, string subtitle, Color accent, float hold)
    {
        Canvas canvas = StageUi.CreateCanvas("Stage Banner", 900);
        CanvasGroup group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        RectTransform box = StageUi.Rect("Box", canvas.transform);
        StageUi.Center(box, new Vector2(1100f, 220f), new Vector2(0f, 120f));

        TextMeshProUGUI big = StageUi.Label("Title", box, title.ToUpperInvariant(), 96f, StageUi.Text,
                                            TextAlignmentOptions.Center, FontStyles.Bold);
        big.characterSpacing = 6f;
        StageUi.Top(big.rectTransform, new Vector2(1100f, 120f), 0f);

        TextMeshProUGUI small = StageUi.Label("Subtitle", box, subtitle, 34f, accent, TextAlignmentOptions.Center, FontStyles.Bold);
        small.characterSpacing = 14f;
        StageUi.Top(small.rectTransform, new Vector2(1100f, 50f), 130f);

        float t = 0f;
        while (t < 0.3f) { t += Time.unscaledDeltaTime; group.alpha = Mathf.Clamp01(t / 0.3f); yield return null; }
        group.alpha = 1f;

        float wait = 0f;
        while (wait < hold) { wait += Time.unscaledDeltaTime; yield return null; }

        t = 0f;
        while (t < 0.5f) { t += Time.unscaledDeltaTime; group.alpha = 1f - Mathf.Clamp01(t / 0.5f); yield return null; }

        if (canvas != null) Object.Destroy(canvas.gameObject);
    }
}
