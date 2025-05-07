using UnityEngine;
using System.Text;

#if ENABLE_INPUT_SYSTEM             // <── automatically defined when the package is active
using UnityEngine.InputSystem;      // new Input System namespace
#endif

public class SimpleConsole : MonoBehaviour
{
    [SerializeField] int maxChars = 10_000;
    private readonly StringBuilder buffer = new StringBuilder(1024);
    private Vector2 scroll;

    /* ------------------ capture logs ------------------ */
    private void OnEnable() => Application.logMessageReceived += Handle;
    private void OnDisable() => Application.logMessageReceived -= Handle;

    private void Handle(string msg, string stack, LogType type)
    {
        buffer.AppendLine(msg);
        if (buffer.Length > maxChars)
            buffer.Remove(0, buffer.Length - maxChars);
    }

    private bool showConsole = true;

    /* ------------------ toggle visibility ------------------ */
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            showConsole = !showConsole;
#else                       // legacy Input Manager
        if (Input.GetKeyDown(KeyCode.F1))
            showConsole = !showConsole;
#endif
    }

    /* ------------------ draw overlay ------------------ */
    /* ------------------ draw overlay ------------------ */
    void OnGUI()
    {
        if (!showConsole) return;

        const int pad = 8;
        const int header = 300;

        float w = Mathf.Min(650, Screen.width * 0.45f);
        float h = Screen.height - header - pad;

        Rect box = new Rect(pad, header, w, h);

        GUI.Box(box, "Console (F1 to hide)");
        GUI.skin.label.fontSize = 24;

        /* --- REAL height of the whole text block --- */
        string text = buffer.ToString();
        GUIStyle style = GUI.skin.label;

        // Width available inside the scroll view (subtract margins + scrollbar)
        float contentWidth = w - 24;
        // Let Unity compute exact pixel height with wrapping
        float contentH = style.CalcHeight(new GUIContent(text), contentWidth);

        // Ensure it's at least as tall as the viewport so scrollbar appears
        contentH = Mathf.Max(contentH, h - 28);

        /* ------------- Scroll view ------------- */
        scroll = GUI.BeginScrollView(
            new Rect(box.x + 4, box.y + 24, w - 8, h - 28),   // viewport
            scroll,
            new Rect(0, 0, contentWidth, contentH)            // content
        );
        GUI.Label(new Rect(0, 0, contentWidth, contentH), text, style);
        GUI.EndScrollView();
    }
}
