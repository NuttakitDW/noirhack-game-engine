using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerCard : MonoBehaviour
{
    public string PlayerId { get; private set; }

    [SerializeField] Image outline;
    [SerializeField] Image avatarImage;
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] GameObject roleBadge;
    [SerializeField] TMP_Text roleLabel;
    [SerializeField] Image backgroundImage;
    [SerializeField] GameObject deathOverlay;
    [SerializeField] TMP_Text voteBadge;
    [SerializeField] Sprite placeholderAvatar;


    Button btn;

    public void Init(string id, string name, Sprite avatar)
    {
        PlayerId = id;
        nameLabel.text = name;
        avatarImage.sprite = avatar;

        btn = GetComponent<Button>();
        btn.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        PlayerCardEvents.Select(this);
    }

    public void SetSelected(bool sel) =>
        outline.color = sel
            ? new Color32(0xFF, 0xCF, 0x3F, 0x40)
            : new Color32(0, 0, 0, 0);

    public void ShowRole(string role)
    {
        roleLabel.text = role;
        roleBadge.SetActive(true);
    }

    public void MarkDead()
    {
        // greyscale the avatar
        if (avatarImage) avatarImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        // darken background
        if (backgroundImage) backgroundImage.color = new Color32(0x14, 0x18, 0x29, 0xFF);

        // show big red X overlay
        if (deathOverlay) deathOverlay.SetActive(true);

        // prevent clicks
        var btn = GetComponent<Button>();
        if (btn) btn.interactable = false;
    }

    public void SetVoteCount(int n)
    {
        voteBadge.text = n > 0 ? n.ToString() : "";
        voteBadge.gameObject.SetActive(n > 0);
    }
}
