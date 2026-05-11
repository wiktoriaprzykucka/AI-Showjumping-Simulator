using UnityEngine;

/// <summary>
/// Starts play mode paused: pick a course with <see cref="CourseBrowserUI"/> (default F2), then confirm with
/// Begin Round or <see cref="beginRoundKey"/>. Uses IMGUI like <see cref="CourseBrowserUI"/> — no Canvas required.
/// </summary>
[DefaultExecutionOrder(-5000)]
public sealed class RoundStartGate : MonoBehaviour {

    [Tooltip("When true, time stops until Begin Round.")]
    public bool pauseOnPlay = true;

    [Tooltip("Time.timeScale restored when starting the round. Keep at 1 for normal playback.")]
    [Min(0.0001f)] public float runTimeScale = 1f;

    [Tooltip("Keyboard shortcut to begin the round (alongside the on-screen button).")]
    public KeyCode beginRoundKey = KeyCode.Return;

    [Tooltip("Shown when paused for round start.")]
    public string title = "Parkour setup";

    [Tooltip("Short instructions under the title.")]
    [TextArea(2, 4)] public string body =
        "Open F2 for the course list, pick a JSON, then press Begin round.\n";

    bool _waitingForStart;

    GUIStyle _panelStyle;
    GUIStyle _titleStyle;
    GUIStyle _bodyStyle;
    GUIStyle _buttonStyle;
    bool _stylesReady;

    /// <summary>False while the gates screen is blocking the simulation.</summary>
    public bool SimulationRunning => !_waitingForStart;

    void Awake() {
        if (pauseOnPlay) {
            Time.timeScale = 0f;
            _waitingForStart = true;
        } else {
            _waitingForStart = false;
        }
    }

    void Update() {
        if (!_waitingForStart || beginRoundKey == KeyCode.None) return;
        if (Input.GetKeyDown(beginRoundKey))
            BeginRound();
    }

    public void BeginRound() {
        if (!_waitingForStart) return;
        Time.timeScale = runTimeScale;
        _waitingForStart = false;
    }

    /// <summary>Pause again (e.g. from another script). Enables the gate overlay.</summary>
    public void PauseForSetup() {
        Time.timeScale = 0f;
        _waitingForStart = true;
    }

    void EnsureStyles() {
        if (_stylesReady) return;
        _panelStyle = new GUIStyle {
            normal = { background = MakeColorTexture(new Color(0f, 0f, 0f, 0.92f)) },
            padding = new RectOffset(16, 16, 14, 14),
            border = new RectOffset(1, 1, 1, 1),
        };
        _titleStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
        _bodyStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            richText = true,
            normal = { textColor = new Color(0.88f, 0.9f, 0.95f) },
        };
        _buttonStyle = new GUIStyle(GUI.skin.button) {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(14, 14, 10, 10),
            fixedHeight = 40,
        };
        _stylesReady = true;
    }

    static Texture2D MakeColorTexture(Color c) {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.SetPixels(new[] { c, c, c, c });
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    void OnGUI() {
        if (!_waitingForStart) return;
        EnsureStyles();

        // This script runs before CourseBrowserUI (execution order −5000 vs 100). Use a higher GUI.depth
        // so the course browser (default depth) stays on top and remains clickable.
        int prevDepth = GUI.depth;
        GUI.depth = 128;

        float w = Mathf.Min(480f, Screen.width - 32f);
        float h = 220f;
        var card = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUILayout.BeginArea(card, _panelStyle);
        GUILayout.Label(title, _titleStyle);
        GUILayout.Space(6);
        GUILayout.Label(body.TrimEnd() + '\n', _bodyStyle, GUILayout.Height(72));
        if (GUILayout.Button("Begin round", _buttonStyle, GUILayout.ExpandWidth(true)))
            BeginRound();
        GUILayout.Space(4);
        GUILayout.Label(
            $"Keyboard: <b>{beginRoundKey}</b> begins   ·   <b>F2</b> course browser",
            new GUIStyle(_bodyStyle) { fontSize = 11, alignment = TextAnchor.MiddleCenter });
        GUILayout.EndArea();

        GUI.depth = prevDepth;
    }
}
