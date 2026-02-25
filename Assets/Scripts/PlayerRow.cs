using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the PlayerRow prefab.
/// Simple UI row showing a player's role, ready state, and whether they're host/local.
///
/// Prefab structure:
///   PlayerRow
///   ├── ImgRoleIcon    (Image — fish or rod icon)
///   ├── TxtRole        (TMP_Text — "Fisher" / "Fish")
///   ├── TxtTag         (TMP_Text — "(You)" / "(Host)")
///   └── ImgReadyDot    (Image — green/grey dot)
/// </summary>
public class PlayerRow : MonoBehaviour
{
    [SerializeField] private Image    imgRoleIcon;
    [SerializeField] private TMP_Text txtRole;
    [SerializeField] private TMP_Text txtTag;
    [SerializeField] private Image    imgReadyDot;

    [Header("Role Icons")]
    [SerializeField] private Sprite iconFish;
    [SerializeField] private Sprite iconFisher;

    [Header("Ready Colors")]
    [SerializeField] private Color colorReady   = new Color(0.2f, 0.85f, 0.3f);
    [SerializeField] private Color colorNotReady = new Color(0.5f, 0.5f, 0.5f);

    public void Setup(string role, bool isReady, bool isHost, bool isLocal)
    {
        // Role text and icon
        txtRole.text = role;
        if (imgRoleIcon != null)
            imgRoleIcon.sprite = role == "Fisher" ? iconFisher : iconFish;

        // Tag
        if (isLocal && isHost)      txtTag.text = "(You - Host)";
        else if (isLocal)           txtTag.text = "(You)";
        else if (isHost)            txtTag.text = "(Host)";
        else                        txtTag.text = "";

        // Ready dot
        if (imgReadyDot != null)
            imgReadyDot.color = isReady ? colorReady : colorNotReady;
    }
}
