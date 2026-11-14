using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One stage card on the Level Select / PVP pages. The card that sits in the scene (under Viewport > Content) is the TEMPLATE: when the
/// scene starts, LevelSelectController hides it and makes one copy of it per stage in the catalog, filling in the name, number and picture
/// through this component. So you restyle EVERY card by restyling that one card in the Scene view: move things, change fonts, colours,
/// sizes, add decoration. Just keep the references below pointing at the right objects.
///
/// A card with no picture (the stage has no thumbnail) hides the picture and shows its big number instead.
///
/// THE RANK: the badge in the corner shows the best rank earned on this stage (P S A B C D F, or "-" if it was never cleared) and the strip
/// along the bottom of the picture shows the best score, the best time and the last few tries. Both come from LevelRecords.
/// A template card that has no badge yet (a scene saved before the rank system) gets one built in code, so old scenes keep working;
/// "Build / Rebuild Menu UI" on the Level Select Controller (or Tools > Stages > Menu UI > Build Level Select UI In Open Scene) puts a real,
/// editable one in the scene (that rebuild throws away other edits you made to that UI, so it asks first).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Stages/Stage Card View")]
public class StageCardView : MonoBehaviour
{
    [Tooltip("The card's button. LevelSelectController hooks the click to 'start this stage'.")]
    public Button button;
    [Tooltip("The picture. Gets the stage's thumbnail, or the 'No Picture Colour' when the stage has none.")]
    public Image thumbnail;
    [Tooltip("The big number shown instead of a picture. Switched off when the stage has a thumbnail.")]
    public TMP_Text thumbnailNumber;
    [Tooltip("The small number in the pill in the corner.")]
    public TMP_Text pillNumber;
    [Tooltip("The stage's name.")]
    public TMP_Text nameLabel;
    [Tooltip("The colour the picture area has when the stage has no thumbnail.")]
    public Color noPictureColor = new Color(0.06f, 0.08f, 0.13f, 1f);

    [Header("Best rank (filled in from the saved records)")]
    [Tooltip("The round badge in the corner: the best rank letter. Switched off on cards that have no rank (PVP arenas).")]
    public Image rankBadge;
    [Tooltip("The rank letter inside the badge.")]
    public TMP_Text rankLetter;
    [Tooltip("The strip of text along the bottom of the picture: best score, best time, last tries.")]
    public TMP_Text recordLabel;
    [Tooltip("The dark strip behind the record text.")]
    public Image recordStrip;

    /// <summary>Fills the card in. number starts at 1.</summary>
    public void Fill(string title, int number, Sprite picture)
    {
        string digits = number.ToString("00");

        if (nameLabel != null) nameLabel.text = title;
        if (pillNumber != null) pillNumber.text = digits;
        if (thumbnailNumber != null)
        {
            thumbnailNumber.text = digits;
            thumbnailNumber.gameObject.SetActive(picture == null);
        }

        if (thumbnail != null)
        {
            thumbnail.sprite = picture;
            thumbnail.color = picture != null ? Color.white : noPictureColor;
            thumbnail.preserveAspect = true;
        }
    }

    /// <summary>
    /// Shows the best result on this card. record is null when the stage was never cleared; showRank false hides the rank parts altogether
    /// (the PVP page, where nothing is ranked).
    /// </summary>
    public void ShowRecord(LevelRecord record, bool showRank)
    {
        if (showRank) EnsureRecordUi();

        bool has = record != null && record.Cleared;

        if (rankBadge != null) rankBadge.gameObject.SetActive(showRank);
        if (recordStrip != null) recordStrip.gameObject.SetActive(showRank);
        if (recordLabel != null) recordLabel.gameObject.SetActive(showRank);
        if (!showRank) return;

        int rank = has ? record.bestRank : RankCalc.NoRank;
        if (rankLetter != null)
        {
            rankLetter.text = RankCalc.Letter(rank);
            rankLetter.color = has ? new Color(0.05f, 0.06f, 0.10f, 1f) : StageUi.TextDim;
        }
        if (rankBadge != null)
        {
            Color c = RankCalc.ColorOf(rank);
            rankBadge.color = has ? c : new Color(0.16f, 0.20f, 0.30f, 1f);
        }

        if (recordLabel != null)
        {
            if (!has)
            {
                recordLabel.text = "NOT CLEARED YET";
                recordLabel.color = StageUi.TextDim;
            }
            else
            {
                string line = "BEST " + RankCalc.FormatScore(record.bestScore) + "   -   " + RankCalc.FormatTime(record.bestTime);
                if (record.history.Count > 1)   // more than one try: show the last few ranks, newest first
                {
                    line += "   -   LAST";
                    int newest = record.history.Count - 1;
                    for (int i = 0; i < Mathf.Min(record.history.Count, 5); i++)
                    {
                        line += " " + RankCalc.Rich(record.history[newest - i].rank);
                    }
                }
                recordLabel.text = line;
                recordLabel.color = StageUi.Text;
            }
        }
    }

    /// <summary>
    /// Builds the badge and the record strip if this card doesn't have them yet, and points the fields at them. Called when a card is filled
    /// in, and by MenuUiBuilder when the Level Select scene is baked (so the baked card has real objects you can restyle).
    /// </summary>
    public void BuildRecordUi()
    {
        EnsureRecordUi();
    }

    private void EnsureRecordUi()
    {
        if (rankBadge != null && recordLabel != null) return;

        Transform root = transform;

        // The strip sits along the bottom of the picture area (the picture ends where the name starts, about a fifth of the way up).
        if (recordStrip == null)
        {
            recordStrip = StageUi.Img("Record Strip", root, new Color(0f, 0f, 0f, 0.55f));
            RectTransform sr = recordStrip.rectTransform;
            sr.anchorMin = new Vector2(0f, 0.22f);
            sr.anchorMax = new Vector2(1f, 0.22f);
            sr.pivot = new Vector2(0.5f, 0f);
            sr.sizeDelta = new Vector2(-24f, 40f);
            sr.anchoredPosition = new Vector2(0f, 0f);
        }

        if (recordLabel == null)
        {
            TextMeshProUGUI label = StageUi.Label("Record", recordStrip.transform, "", 22f, StageUi.Text, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = 24f;
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(12f, 0f);
            lr.offsetMax = new Vector2(-12f, 0f);
            recordLabel = label;
        }

        if (rankBadge == null)
        {
            rankBadge = StageUi.Img("Rank Badge", root, new Color(0.16f, 0.20f, 0.30f, 1f), StageUi.Rounded, true);
            RectTransform br = rankBadge.rectTransform;
            br.anchorMin = new Vector2(1f, 1f);
            br.anchorMax = new Vector2(1f, 1f);
            br.pivot = new Vector2(1f, 1f);
            br.sizeDelta = new Vector2(76f, 76f);
            br.anchoredPosition = new Vector2(-22f, -22f);

            TextMeshProUGUI letter = StageUi.Label("Letter", rankBadge.transform, "-", 52f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
            letter.overflowMode = TextOverflowModes.Overflow;
            StageUi.Stretch(letter.rectTransform);
            rankLetter = letter;
        }
    }
}
