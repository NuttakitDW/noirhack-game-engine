using UnityEngine;
using System.Text;

public class SimpleConsole : MonoBehaviour
{
    // Keep ~10 k characters (adjust if you want)
    [SerializeField] int maxChars = 10000;
    StringBuilder buffer = new StringBuilder(1024);
    Vector2 scroll;

    void OnEnable() => Application.logMessageReceived += Handle;
    void OnDisable() => Application.logMessageReceived -= Handle;

    void Handle(string msg, string stack, LogType type)
    {
        buffer.AppendLine(msg);
        if (buffer.Length > maxChars)
            buffer.Remove(0, buffer.Length - maxChars);
    }

    void Update()
    {
        // F1 toggles visibility
        if (Input.GetKeyDown(KeyCode.F1))
            enabled = !enabled;
    }

    void OnGUI()
    {
        const int pad = 8;
        int w = Screen.width - 2 * pad;
        int h = Screen.height / 3;              // bottom-third of the screen
        GUI.Box(new Rect(pad, pad, w, h), "Console (F1 to hide)");
        scroll = GUI.BeginScrollView(
            new Rect(pad, pad + 20, w, h - 24), scroll,
            new Rect(0, 0, w - 20, h + buffer.Length / 3f)
        );
        GUI.Label(new Rect(0, 0, w - 20, h + buffer.Length / 3f), buffer.ToString());
        GUI.EndScrollView();
    }
}
