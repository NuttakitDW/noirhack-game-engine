using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerCard : MonoBehaviour
{
    public string PlayerId { get; private set; }

    [SerializeField] Image outline;
    [SerializeField] Image avatarImage;
    [SerializeField] TMP_Text nameLabel;

    Button btn;

    public void Init(string id, string name, Sprite[] avatarPool)
    {
        PlayerId = id;

        nameLabel.text = name;
        avatarImage.sprite = PickAvatar(id, avatarPool);

        btn = GetComponent<Button>();
        btn.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        PlayerCardEvents.Select(this);
    }

    public void SetSelected(bool sel) =>
        outline.color = sel
            ? new Color32(0xFF, 0xCF, 0x3F, 0xFF)
            : new Color32(0, 0, 0, 0);

    Sprite PickAvatar(string seed, Sprite[] pool)
    {
        if (pool == null || pool.Length == 0) return null;

        int idx = Mathf.Abs(seed.GetHashCode()) % pool.Length;
        return pool[idx];
    }
}
