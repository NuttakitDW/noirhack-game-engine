using UnityEngine;
using UnityEngine.UI;

public class MuteToggle : MonoBehaviour
{
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private Image icon;

    [SerializeField] private Sprite spriteOn;
    [SerializeField] private Sprite spriteOff;

    private bool muted;

    void Start()
    {
        muted = PlayerPrefs.GetInt("musicMuted", 0) == 1;
        Apply();
        GetComponent<Button>().onClick.AddListener(Toggle);
    }

    void Toggle()
    {
        muted = !muted;
        PlayerPrefs.SetInt("musicMuted", muted ? 1 : 0);
        Apply();
    }

    void Apply()
    {
        if (musicSource != null) musicSource.mute = muted;
        if (icon != null)
            icon.sprite = muted ? spriteOff : spriteOn;
    }
}
