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

    [Header("Re-open setup (during play)")]
    [Tooltip("Press during play to pause and open this panel again — switch AI / player, then Begin round. Set to None to disable.")]
    public KeyCode openSetupKey = KeyCode.F3;

    [Tooltip("Shown when paused for round start.")]
    public string title = "Parkour setup";

    [Tooltip("Short instructions under the title.")]
    [TextArea(2, 4)] public string body =
        "Open F2 for the course list, pick a JSON, then confirm here.\n";

    [Tooltip("When pauseOnPlay is false, this driving mode is applied immediately (no gate UI).")]
    public HorseDriveMode quickStartMode = HorseDriveMode.AgentModel;

    bool _waitingForStart;

    /// <summary>0 = AI, 1 = player (matches <see cref="GUILayout.SelectionGrid"/>).</summary>
    int _driveModeSelection;

    const int DriveModeAi = 0;
    const int DriveModePlayer = 1;

    GUIStyle _panelStyle;
    GUIStyle _titleStyle;
    GUIStyle _bodyStyle;
    GUIStyle _buttonStyle;
    bool _stylesReady;

    /// <summary>False while the gates screen is blocking the simulation.</summary>
    public bool SimulationRunning => !_waitingForStart;

    void Awake() {
        _driveModeSelection = quickStartMode == HorseDriveMode.AgentModel ? DriveModeAi : DriveModePlayer;
        if (pauseOnPlay) {
            Time.timeScale = 0f;
            _waitingForStart = true;
        } else {
            _waitingForStart = false;
            HorseDriveModeSession.SelectedMode = quickStartMode;
            HorseDriveModeSession.ApplyToAllAgentsInScene();
        }
    }

    void Update() {
        if (_waitingForStart) {
            if (beginRoundKey != KeyCode.None && Input.GetKeyDown(beginRoundKey))
                BeginRound();
            return;
        }

        if (openSetupKey != KeyCode.None && Input.GetKeyDown(openSetupKey))
            OpenSetupDuringPlay();
    }

    /// <summary>
    /// Pauses and shows the setup UI so the player can change drive mode (or confirm). Syncs the
    /// selection grid from <see cref="HorseDriveModeSession.SelectedMode"/>.
    /// </summary>
    public void OpenSetupDuringPlay() {
        SyncDriveModeSelectionFromSession();
        PauseForSetup();
    }

    void SyncDriveModeSelectionFromSession() {
        _driveModeSelection = HorseDriveModeSession.SelectedMode == HorseDriveMode.AgentModel
            ? DriveModeAi
            : DriveModePlayer;
    }

    public void BeginRound() {
        if (!_waitingForStart) return;
        HorseDriveModeSession.SelectedMode = _driveModeSelection == DriveModeAi
            ? HorseDriveMode.AgentModel
            : HorseDriveMode.PlayerHeuristic;
        HorseDriveModeSession.ApplyToAllAgentsInScene();
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
        float h = 300f;
        var card = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUILayout.BeginArea(card, _panelStyle);
        GUILayout.Label(title, _titleStyle);
        GUILayout.Space(6);
        GUILayout.Label(body.TrimEnd() + '\n', _bodyStyle, GUILayout.Height(56));
        GUILayout.Space(4);
        GUILayout.Label("Who drives the horse?", new GUIStyle(_bodyStyle) { fontStyle = FontStyle.Bold, fontSize = 12 });
        var options = new[] { "AI agent (trained model)", "Player — WASD + Space" };
        _driveModeSelection = GUILayout.SelectionGrid(_driveModeSelection, options, 1, GUILayout.Height(72));
        GUILayout.Space(8);
        if (GUILayout.Button("Begin round", _buttonStyle, GUILayout.ExpandWidth(true)))
            BeginRound();
        GUILayout.Space(4);
        var openKeyHint = openSetupKey != KeyCode.None
            ? $"<b>{openSetupKey}</b> open this menu in play   ·   "
            : "";
        GUILayout.Label(
            $"<b>{beginRoundKey}</b> begin   ·   {openKeyHint}<b>F2</b> courses   ·   <b>Tab</b> AI / player   ·   <b>C</b> camera",
            new GUIStyle(_bodyStyle) { fontSize = 11, alignment = TextAnchor.MiddleCenter });
        GUILayout.EndArea();

        GUI.depth = prevDepth;
    }
}
