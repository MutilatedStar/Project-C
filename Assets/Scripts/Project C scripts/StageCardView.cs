using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One stage card on the Level Select / PVP pages. The card that sits in the scene (under Viewport > Content) is the TEMPLATE: when the
/// scene starts, LevelSelectController hides it and makes one copy of it per stage in the catalog, filling in the name, number and picture
/// through this component. So you restyle EVERY card by restyling that one card in the Scene view: move things, change fonts, colours,
/// sizes, add decoration. Just keep the five references below pointing at the right objects.
///
/// A card with no picture (the stage has no thumbnail) hides the picture and shows its big number instead.
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
}
