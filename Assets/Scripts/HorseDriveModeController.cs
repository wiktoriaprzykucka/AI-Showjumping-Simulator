using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// Switches ML-Agents between neural policy (<see cref="BehaviorType.InferenceOnly"/> or Default)
/// and human input via <see cref="HorseAgent.Heuristic"/> (<see cref="BehaviorType.HeuristicOnly"/>).
/// Add to the same GameObject as <see cref="HorseAgent"/> (or it is added the first time
/// <see cref="HorseDriveModeSession.ApplyToAllAgentsInScene"/> runs).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HorseAgent))]
public sealed class HorseDriveModeController : MonoBehaviour
{
    [Header("Toggle (during play)")]
    [Tooltip("Switch between AI and player control.")]
    public KeyCode toggleDriveModeKey = KeyCode.Tab;

    [Header("HUD")]
    public bool showDriveModeHud = true;

    BehaviorParameters _behaviorParameters;
    DecisionRequester _decisionRequester;

    /// <summary>Inspector / serialized behavior before we ever forced heuristic (Default or InferenceOnly).</summary>
    BehaviorType _cachedAiBehaviorType = BehaviorType.InferenceOnly;

    int _cachedAiDecisionPeriod = 5;

    HorseDriveMode _current;

    void Awake()
    {
        _behaviorParameters = GetComponent<BehaviorParameters>();
        _decisionRequester = GetComponent<DecisionRequester>();

        if (_behaviorParameters != null)
        {
            _cachedAiBehaviorType = _behaviorParameters.BehaviorType;
            if (_cachedAiBehaviorType == BehaviorType.HeuristicOnly)
                _cachedAiBehaviorType = BehaviorType.InferenceOnly;
        }

        if (_decisionRequester != null)
            _cachedAiDecisionPeriod = Mathf.Max(1, _decisionRequester.DecisionPeriod);
    }

    void Start()
    {
        var gate = FindFirstObjectByType<RoundStartGate>();
        if (gate != null && !gate.SimulationRunning)
            return;

        ApplyMode(HorseDriveModeSession.SelectedMode);
    }

    void Update()
    {
        var gate = FindFirstObjectByType<RoundStartGate>();
        if (gate != null && !gate.SimulationRunning)
            return;

        if (toggleDriveModeKey != KeyCode.None && Input.GetKeyDown(toggleDriveModeKey))
        {
            var next = _current == HorseDriveMode.AgentModel
                ? HorseDriveMode.PlayerHeuristic
                : HorseDriveMode.AgentModel;
            ApplyMode(next);
            HorseDriveModeSession.SelectedMode = next;
        }
    }

    void OnGUI()
    {
        if (!showDriveModeHud || !Application.isPlaying)
            return;

        var gate = FindFirstObjectByType<RoundStartGate>();
        if (gate != null && !gate.SimulationRunning)
            return;

        string setupKeyText = (gate != null && gate.openSetupKey != KeyCode.None)
            ? gate.openSetupKey.ToString()
            : "F3";

        const int pad = 8;
        var rect = new Rect(pad, pad, 420, 54);
        GUI.Box(rect, GUIContent.none);
        string line1 = _current == HorseDriveMode.PlayerHeuristic
            ? "<b>Mode: Player</b> (WASD move, Space jump)"
            : "<b>Mode: AI agent</b>";
        string line2 = $"<size=11>Tab: AI / Player   ·   {setupKeyText}: setup menu   ·   C: camera (top / horse)   ·   F2: courses</size>";
        var title = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13, wordWrap = true };
        GUI.Label(new Rect(pad + 6, pad + 4, 408, 22), line1, title);
        GUI.Label(new Rect(pad + 6, pad + 24, 408, 22), line2, title);
    }

    /// <summary>Applies behavior + decision frequency. Safe to call multiple times.</summary>
    public void ApplyMode(HorseDriveMode mode)
    {
        _current = mode;
        if (_behaviorParameters == null)
        {
            Debug.LogWarning($"{nameof(HorseDriveModeController)} on {name}: missing BehaviorParameters.", this);
            return;
        }

        switch (mode)
        {
            case HorseDriveMode.PlayerHeuristic:
                _behaviorParameters.BehaviorType = BehaviorType.HeuristicOnly;
                if (_decisionRequester != null)
                    _decisionRequester.DecisionPeriod = 1;
                break;
            case HorseDriveMode.AgentModel:
                _behaviorParameters.BehaviorType = _cachedAiBehaviorType;
                if (_decisionRequester != null)
                    _decisionRequester.DecisionPeriod = _cachedAiDecisionPeriod;
                break;
        }
    }

    public HorseDriveMode CurrentMode => _current;
}

/// <summary>Driving mode selected on the start gate and toggled in-game.</summary>
public enum HorseDriveMode
{
    AgentModel,
    PlayerHeuristic,
}

/// <summary>Holds the start-screen choice and applies it to all <see cref="HorseAgent"/> instances.</summary>
public static class HorseDriveModeSession
{
    public static HorseDriveMode SelectedMode = HorseDriveMode.AgentModel;

    public static void ApplyToAllAgentsInScene()
    {
        foreach (var agent in Object.FindObjectsByType<HorseAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var ctrl = agent.GetComponent<HorseDriveModeController>();
            if (ctrl == null)
                ctrl = agent.gameObject.AddComponent<HorseDriveModeController>();
            ctrl.ApplyMode(SelectedMode);
        }
    }
}
