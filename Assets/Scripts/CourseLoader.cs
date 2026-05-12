using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.MLAgents.Sensors;

// ─── JSON shape ────────────────────────────────────────────────────────
[System.Serializable]
public class CL_Vec3 {
    public float x, y, z;
    public Vector3 ToVector3() => new Vector3(x, y, z);
}

[System.Serializable]
public class CL_Hurdle {
    public int order;
    public string label;
    public CL_Vec3 unityPosition;
    public float rotationY;
}

[System.Serializable]
public class CL_CourseInfo {
    public string name;
    public string arenaSize;
    public float speed_m_per_min;
    public float length_m;
    public float timeAllowed_sec;
    public float obstacleHeight_m;
    public int totalObstacles;
}

[System.Serializable]
public class CL_SensorPlacement {
    public CL_Vec3 unityPosition;
    public float rotationY;
}

[System.Serializable]
public class CL_Course {
    public CL_CourseInfo courseInfo;
    public CL_Hurdle[] hurdles;
    /// <summary>Optional. When set, CourseLoader spawns a transform and assigns CoursePathManager.startPoint.</summary>
    public CL_SensorPlacement startSensor;
    /// <summary>Optional. When set, CourseLoader spawns a transform and assigns CoursePathManager.finishPoint.</summary>
    public CL_SensorPlacement finishSensor;
}

/// <summary>Inspector presets for the auto-spawned finish-line trigger (Course_FinishSensor).</summary>
public enum CourseFinishTriggerPreset {
    Custom = 0,
    Standard = 1,
    Wide = 2,
    Tall = 3,
    Deep = 4,
}

// ─── Loader ────────────────────────────────────────────────────────────
// Runs early so spawned hurdles/ground/walls exist before the HorseAgent
// requests its first decision (avoids stale or empty `hurdles` array).
[DefaultExecutionOrder(-100)]
public class CourseLoader : MonoBehaviour {
    [Header("Source")]
    [Tooltip("Drag your exported course.json here")]
    public TextAsset courseJson;

    [Header("Hurdle Prefab (preferred)")]
    [Tooltip("If assigned, hurdles are spawned by instantiating this prefab — the procedural " +
             "post/rail builder below is skipped entirely. The prefab MUST have:\n" +
             "  • a trigger Collider on its ROOT (HorseAgent's OnTriggerEnter accepts the root " +
             "    or any child of the target hurdle, so a child trigger works too), and\n" +
             "  • the tag \"Hurdle\" on its root (auto-applied if missing).\n" +
             "Recommended: use Assets/Prefab/Hurdle.prefab — calibrated to the height the trained " +
             "model expects.")]
    public GameObject hurdlePrefab;

    [Tooltip("Only effective when `hurdlePrefab` is assigned. If true, the prefab's vertical " +
             "scale is multiplied so its bar matches the JSON's obstacleHeight_m. Off by default — " +
             "the prefab's native height is what the trained model saw, and rescaling distorts " +
             "leg thickness too.")]
    public bool scalePrefabToJsonHeight = false;

    [Tooltip("Native bar height of `hurdlePrefab`, in meters. Only used when `scalePrefabToJsonHeight` " +
             "is on. The supplied Hurdle.prefab puts its bar at ~1.7m.")]
    public float prefabNativeHeight = 1.7f;

    [Header("Procedural Hurdle Geometry (fallback — only used when no `hurdlePrefab` is assigned)")]
    public float postWidth = 0.15f;
    public float railLength = 3.0f;
    [Tooltip("If true, ignore the height from the JSON and use the value below")]
    public bool overrideHeight = false;
    public float overrideHeightValue = 1.4f;
    [Tooltip("If true, the decorative red/white rail bands have solid BoxColliders. The trained " +
             "model was conditioned on a single thin solid bar — keeping this false means the bands " +
             "are visual-only and the trigger volume handles clearance detection (recommended for " +
             "the supplied trained model). Enable for retraining with stricter physics.")]
    public bool solidRails = false;

    [Header("Detection Trigger (procedural fallback only)")]
    [Tooltip("Invisible zone the horse must pass through to clear the hurdle. Ignored when " +
             "`hurdlePrefab` is assigned — the prefab's own trigger is used.")]
    public float triggerWidth = 3.5f;
    public float triggerHeight = 2.5f;
    public float triggerDepth = 2.0f;

    [Header("Materials (procedural fallback only — leave blank for defaults)")]
    public Material postMaterial;
    public Material railWhiteMaterial;
    public Material railRedMaterial;

    [Header("Arena (built from courseInfo.arenaSize)")]
    [Tooltip("If true, CourseLoader builds a ground plane sized to the JSON arena. Disable if you have your own scene ground.")]
    public bool buildArena = true;
    [Tooltip("Fallback arena size if JSON arenaSize is missing or unparseable. (width × length, in meters)")]
    public Vector2 fallbackArenaSize = new Vector2(60f, 40f);
    public Material groundMaterial;
    [Tooltip("Layer NAME used for the generated ground. MUST match HorseAgent.groundLayer mask, " +
             "otherwise Physics.CheckSphere will report isGrounded=false forever and the trained " +
             "policy will misbehave. Default 'Ground' matches the layer used during training.")]
    public string groundLayerName = "Ground";
    [Tooltip("Fallback layer index if groundLayerName isn't defined in Project Settings → Tags & Layers.")]
    [Range(0, 31)] public int groundLayerFallback = 8;
    [Tooltip("If true, also creates thin walls around the arena so the horse can't leave.")]
    public bool buildWalls = true;
    public float wallHeight = 2.5f;
    public float wallThickness = 0.3f;
    public Material wallMaterial;

    [Header("Top-Down Camera (HorseAgent's TopCam visual sensor)")]
    [Tooltip("If true, builds a top-down camera sized to fit the arena and wires it into the " +
             "horse's TopCam CameraSensorComponent. The trained model expects this view; without " +
             "it the visual sensor feeds zeros and the policy degrades.")]
    public bool autoBuildTopDownCamera = true;
    [Tooltip("Optional: drag in your own top-down camera. If empty (and autoBuild is on), one " +
             "is created at runtime and parented to this GameObject.")]
    public Camera topDownCamera;
    [Tooltip("Sensor name on the HorseAgent to wire the camera into. Defaults to 'TopCam' " +
             "(matches the training prefab).")]
    public string topCamSensorName = "TopCam";
    [Tooltip("Vertical FOV used by the auto-built camera. 60° matches the training scene.")]
    public float topDownCameraFov = 60f;

    [Header("Auto-Wire")]
    [Tooltip("Drag your horse here and the hurdles array fills automatically")]
    public HorseAgent horseAgent;

    [Tooltip("If set, start/finish transforms from JSON are assigned here after load (calls RebuildPath).")]
    public CoursePathManager coursePathManager;

    [Header("Start / finish sensors (JSON + ML)")]
    [Tooltip("Empty `Course_StartSensor` / `Course_FinishSensor` objects are spawned under this loader when " +
             "your `courseJson` includes `startSensor` / `finishSensor` (export from the course designers). " +
             "Assign optional visual prefabs below so they are not invisible in the Scene view.")]
    public GameObject startSensorVisualPrefab;
    public GameObject finishSensorVisualPrefab;

    [Tooltip("Degrees added to JSON `rotationY` for **hurdles** plus **start/finish sensors**. " +
             "CourseDesigner3D uses local −Z as jump/teal-forward; Unity uses +Z — default 180° aligns obstacle " +
             "`transform.forward`, triggers, sensors, path tangents (`CoursePathManager`), and HorseAgent headings " +
             "with the designer. Set to **0** for JSON built for raw Unity yaw (e.g. some 2D SVG exports).")]
    [FormerlySerializedAs("importedSensorRotationYOffsetDegrees")]
    public float importedDesignerRotationYOffsetDegrees = 180f;

    [Tooltip("Quick size presets for the finish gate BoxCollider trigger. " +
             "Non-Custom presets overwrite Width/Height/Depth whenever the Inspector validates — " +
             "switch to **Custom** to keep manual sizes.")]
    public CourseFinishTriggerPreset finishTriggerPreset = CourseFinishTriggerPreset.Standard;

    [Tooltip("Finish trigger size in meters (local X). Overwritten by presets unless Preset = Custom.")]
    public float finishTriggerWidth = 4f;
    [Tooltip("Finish trigger size in meters (local Y). Overwritten by presets unless Preset = Custom.")]
    public float finishTriggerHeight = 2.5f;
    [Tooltip("Finish trigger size in meters (local Z, along gate forward). Overwritten by presets unless Preset = Custom.")]
    public float finishTriggerDepth = 2f;

    [Header("Loading")]
    public bool loadOnAwake = true;

    [Header("Training (mastery-based curriculum)")]
    [Tooltip("If true, the agent rotates through training courses based on MASTERY: it stays on " +
             "the current course until its average episode reward exceeds the threshold, then " +
             "advances to the next. Leave OFF for normal play — the single `courseJson` is used.")]
    public bool swapCoursesDuringTraining = false;

    [Tooltip("Average episode reward (over the last `masteryWindowSize` episodes) the agent must " +
             "reach on the current course before advancing. Tune to match your reward weights:\n" +
             " • With the carrot-heavy defaults (clear=8, jumpBonus=8, lap=20), a 3-hurdle " +
             "perfect run is ~3*(8+8)+20 = 68 + dense rewards. Threshold 30-40 = 'mostly clearing'.\n" +
             " • Threshold 50+ = near-perfect runs only.\n" +
             "Watch the [CourseLoader] progress logs in the console to calibrate.")]
    public float masteryRewardThreshold = 30f;

    [Tooltip("How many recent episodes to average rewards over. Higher = more stable signal " +
             "(less likely to advance on a single lucky episode). 10 is a good default.")]
    [Min(1)] public int masteryWindowSize = 10;

    [Tooltip("MINIMUM episodes the agent must spend on the current course BEFORE the mastery " +
             "threshold is checked. Prevents premature advancement during early lucky episodes. " +
             "(This used to be the fixed 'episodesPerCourse' value — your old setting carries over.)")]
    [FormerlySerializedAs("episodesPerCourse")]
    [Min(1)] public int minEpisodesPerCourse = 20;

    [Tooltip("Curriculum: courses are presented in this EXACT order. The agent advances to the " +
             "next entry only when it MASTERS the current one (avg reward >= threshold). After the " +
             "curriculum, courses are picked randomly from `trainingCourses`. Drag your simplest " +
             "courses here first.")]
    public TextAsset[] curriculumCourses;

    [Tooltip("Random pool used after the curriculum is fully mastered. Each random pick also uses " +
             "mastery-based pacing on it.")]
    public TextAsset[] trainingCourses;

    [Tooltip("If true, both the ForwardCam and TopCam CameraSensorComponents are stripped from the " +
             "horse at runtime. Required for vector-only training/inference (an .onnx trained without " +
             "camera observations cannot be paired with a horse that exposes camera sensors).")]
    public bool stripCameraSensors = false;

    private List<Transform> spawned = new List<Transform>();
    private List<Transform> arenaObjects = new List<Transform>();

    Transform loadedStartSensor;
    Transform loadedFinishSensor;

    /// <summary>World transform for the current JSON <c>startSensor</c>, if any. Lets <see cref="HorseAgent"/> spawn at the gate when <see cref="coursePathManager"/> is not assigned.</summary>
    public Transform LoadedStartSensor => loadedStartSensor;

    // Mastery-curriculum state (training only).
    private int curriculumIndex = 0;
    private TextAsset currentTrainingCourse;
    private int episodesOnCurrentCourse = 0;
    private readonly List<float> recentRewards = new List<float>();

    /// <summary>Applies preset sizes to the finish trigger fields (inspector).</summary>
    void OnValidate() {
        ApplyFinishTriggerPreset();
    }

    void ApplyFinishTriggerPreset() {
        if (finishTriggerPreset == CourseFinishTriggerPreset.Custom)
            return;
        switch (finishTriggerPreset) {
            case CourseFinishTriggerPreset.Standard:
                finishTriggerWidth = 4f;
                finishTriggerHeight = 2.5f;
                finishTriggerDepth = 2f;
                break;
            case CourseFinishTriggerPreset.Wide:
                finishTriggerWidth = 8f;
                finishTriggerHeight = 2.5f;
                finishTriggerDepth = 2f;
                break;
            case CourseFinishTriggerPreset.Tall:
                finishTriggerWidth = 4f;
                finishTriggerHeight = 3.5f;
                finishTriggerDepth = 2f;
                break;
            case CourseFinishTriggerPreset.Deep:
                finishTriggerWidth = 4f;
                finishTriggerHeight = 2.5f;
                finishTriggerDepth = 4f;
                break;
        }
    }

    void Awake() {
        // Strip camera sensors BEFORE the Agent's OnEnable initializes its sensor list.
        // CourseLoader's [DefaultExecutionOrder(-100)] guarantees this runs first.
        if (stripCameraSensors && horseAgent != null) {
            int removed = 0;
            foreach (var s in horseAgent.GetComponents<CameraSensorComponent>()) {
                DestroyImmediate(s);
                removed++;
            }
            if (removed > 0)
                Debug.Log($"[CourseLoader] Stripped {removed} CameraSensorComponent(s) from {horseAgent.name} (vector-only mode).");
        }

        // In training mode, pre-load the first curriculum course (or first training course as a
        // fallback) NOW so the arena, ground, and hurdles already exist when the agent's first
        // observation is collected. The agent's OnEpisodeBegin still drives further rotation —
        // it just sees this pre-loaded course as the "current" one for episode 1.
        if (swapCoursesDuringTraining) {
            TextAsset first = null;
            if (curriculumCourses != null && curriculumCourses.Length > 0 && curriculumCourses[0] != null) {
                first = curriculumCourses[0];
                curriculumIndex = 1; // we've consumed slot 0
            } else if (trainingCourses != null && trainingCourses.Length > 0) {
                foreach (var c in trainingCourses) if (c != null) { first = c; break; }
            } else {
                first = courseJson;
            }

            if (first != null) {
                currentTrainingCourse = first;
                courseJson = first;
                episodesOnCurrentCourse = 0; // OnEpisodeBegin will increment this on episode 1
                recentRewards.Clear();
                LoadCourse();
                Debug.Log($"[CourseLoader] Pre-loaded '{first.name}' (training start, mastery threshold {masteryRewardThreshold}).");
            } else {
                Debug.LogError("[CourseLoader] swapCoursesDuringTraining is ON but no curriculum/" +
                               "training/courseJson is configured — the agent will have no hurdles!");
            }
            return;
        }

        if (loadOnAwake && courseJson != null) LoadCourse();
    }

    // ─── Public API ──────────────────────────────────────────────────────
    /// <summary>Load a course from a raw JSON string (e.g. read from disk at runtime
    /// by CourseBrowserUI). Wraps the text in a transient TextAsset and reuses LoadCourse().</summary>
    public void LoadCourseFromJson(string json, string displayName = null) {
        if (string.IsNullOrEmpty(json)) {
            Debug.LogError("[CourseLoader] LoadCourseFromJson called with empty text.");
            return;
        }
        var ta = new TextAsset(json);
        if (!string.IsNullOrEmpty(displayName)) ta.name = displayName;
        courseJson = ta;
        LoadCourse();
    }

    [ContextMenu("Load Course")]
    public void LoadCourse() {
        ClearCourse();

        if (courseJson == null) {
            Debug.LogError("[CourseLoader] No JSON assigned!");
            return;
        }

        CL_Course data;
        try {
            data = JsonUtility.FromJson<CL_Course>(courseJson.text);
        } catch (System.Exception e) {
            Debug.LogError($"[CourseLoader] JSON parse error: {e.Message}");
            return;
        }

        if (data == null || data.hurdles == null || data.hurdles.Length == 0) {
            Debug.LogError("[CourseLoader] JSON has no hurdles.");
            return;
        }

        float h = overrideHeight
            ? overrideHeightValue
            : (data.courseInfo.obstacleHeight_m > 0 ? data.courseInfo.obstacleHeight_m : 1.4f);

        Debug.Log($"[CourseLoader] Loading '{data.courseInfo.name}' — {data.hurdles.Length} hurdles @ {h}m height");

        EnsureMaterials();

        Vector2 arenaSize = ParseArenaSize(data.courseInfo.arenaSize, fallbackArenaSize);

        if (buildArena) {
            BuildArena(arenaSize);
            Debug.Log($"[CourseLoader] Built arena {arenaSize.x}m × {arenaSize.y}m (from \"{data.courseInfo.arenaSize}\")");
        }

        if (autoBuildTopDownCamera) EnsureTopDownCamera(arenaSize);

        for (int i = 0; i < data.hurdles.Length; i++) {
            var hd = data.hurdles[i];
            // Name MUST match what HorseAgent expects: $"Hurdle_{currentHurdle + 1}"
            string objName = $"Hurdle_{i + 1}";
            var hurdle = CreateHurdle(objName, hd.unityPosition.ToVector3(), hd.rotationY, h);
            spawned.Add(hurdle.transform);
        }

        if (horseAgent != null) {
            horseAgent.hurdles = spawned.ToArray();
            Debug.Log($"[CourseLoader] Wired {spawned.Count} hurdles to HorseAgent.");
        } else {
            Debug.LogWarning("[CourseLoader] No HorseAgent assigned — drag your horse into the slot, or wire the array manually.");
        }

        ApplySensorsAndPath(data);

        if (horseAgent != null) {
            horseAgent.finishGate = loadedFinishSensor;
            if (loadedFinishSensor != null)
                Debug.Log("[CourseLoader] Finish gate assigned for HorseAgent (" + loadedFinishSensor.name + ").");
            // Places the horse on the start sensor as soon as the parkour exists (covers F2 load after Play, and play while paused before the next episode).
            horseAgent.RepositionAtCourseStart(applyJitter: false, overrideStart: loadedStartSensor);
        }
    }

    void ApplySensorsAndPath(CL_Course data) {
        if (data.startSensor != null) {
            CL_Vec3 uv = data.startSensor.unityPosition;
            Vector3 p = uv != null ? uv.ToVector3() : Vector3.zero;
            loadedStartSensor = CreateCourseSensor("Course_StartSensor", p, data.startSensor.rotationY).transform;
            if (coursePathManager != null) coursePathManager.startPoint = loadedStartSensor;
        }

        if (data.finishSensor != null) {
            CL_Vec3 uv = data.finishSensor.unityPosition;
            Vector3 p = uv != null ? uv.ToVector3() : Vector3.zero;
            loadedFinishSensor = CreateCourseSensor("Course_FinishSensor", p, data.finishSensor.rotationY).transform;
            if (coursePathManager != null) coursePathManager.finishPoint = loadedFinishSensor;
        }

        if (coursePathManager != null)
            coursePathManager.RebuildPath();
    }

    /// <summary>
    /// World Y rotation for hurdles/sensors loaded from designer JSON (<see cref="importedDesignerRotationYOffsetDegrees"/>).
    /// </summary>
    Quaternion RotationFromDesignerJsonYaw(float rotationYDeg) =>
        Quaternion.Euler(0f, rotationYDeg + importedDesignerRotationYOffsetDegrees, 0f);

    GameObject CreateCourseSensor(string sensorName, Vector3 worldPos, float rotY) {
        var go = new GameObject(sensorName);
        go.transform.SetParent(transform);
        go.transform.SetPositionAndRotation(worldPos, RotationFromDesignerJsonYaw(rotY));

        if (sensorName == "Course_FinishSensor") {
            TrySetTag(go, "Finish");
            var trig = new GameObject("FinishTrigger");
            trig.transform.SetParent(go.transform);
            trig.transform.localPosition = Vector3.zero;
            trig.transform.localRotation = Quaternion.identity;
            var col = trig.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, finishTriggerHeight * 0.5f, 0f);
            col.size = new Vector3(finishTriggerWidth, finishTriggerHeight, finishTriggerDepth);
        }

        GameObject visPrefab = sensorName == "Course_FinishSensor" ? finishSensorVisualPrefab : startSensorVisualPrefab;
        if (visPrefab != null) {
            GameObject viz = Instantiate(visPrefab, go.transform);
            viz.transform.localPosition = Vector3.zero;
            viz.transform.localRotation = Quaternion.identity;
            viz.name = "SensorVisual"; // clearer in hierarchy than "(Clone)"
        }

        return go;
    }

    // Called by HorseAgent.OnEpisodeBegin during training. Decides whether to stay on the
    // current course or advance based on MASTERY:
    //   • If the just-finished episode was the first ever, picks the first course.
    //   • Otherwise, increments the per-course episode count.
    //   • Once we've spent >= minEpisodesPerCourse on this course AND have >= masteryWindowSize
    //     samples, checks the average reward. If it >= masteryRewardThreshold, ADVANCE.
    //   • Otherwise stays on the same course (no rebuild — fast path).
    [ContextMenu("Load Next Training Course")]
    public void LoadNextTrainingCourse() {
        bool firstEverLoad = currentTrainingCourse == null;

        if (!firstEverLoad) {
            episodesOnCurrentCourse++;

            // Not enough data yet to evaluate mastery — stay on this course.
            if (episodesOnCurrentCourse < minEpisodesPerCourse) return;
            if (recentRewards.Count < masteryWindowSize) return;

            float avg = 0f;
            for (int i = 0; i < recentRewards.Count; i++) avg += recentRewards[i];
            avg /= recentRewards.Count;

            if (avg < masteryRewardThreshold) {
                // Not yet mastered — stay. Print progress every 25 episodes so the user can see it.
                if (episodesOnCurrentCourse % 25 == 0) {
                    Debug.Log($"[CourseLoader] '{currentTrainingCourse.name}': " +
                              $"{episodesOnCurrentCourse} eps, avg reward = {avg:F2} / threshold {masteryRewardThreshold:F2} " +
                              $"(window {masteryWindowSize})");
                }
                return;
            }

            // Mastered! Pick the next course and load it.
            TextAsset next = PickNextCurriculumOrRandom();
            if (next == null) next = courseJson;
            if (next == null) {
                Debug.LogError("[CourseLoader] Mastered current course but no next course available!");
                return;
            }

            Debug.Log($"[CourseLoader] *** MASTERED '{currentTrainingCourse.name}' " +
                      $"(avg reward {avg:F2} >= {masteryRewardThreshold:F2} after {episodesOnCurrentCourse} eps) " +
                      $"--- advancing to '{next.name}'");
            currentTrainingCourse = next;
            courseJson = next;
            episodesOnCurrentCourse = 1;
            recentRewards.Clear();
            LoadCourse();
            return;
        }

        // First-ever load: start the curriculum.
        TextAsset pick = PickNextCurriculumOrRandom();
        if (pick == null) pick = courseJson;
        if (pick == null) {
            Debug.LogError("[CourseLoader] Cannot start training: no curriculum/training/courseJson configured.");
            return;
        }

        currentTrainingCourse = pick;
        courseJson = pick;
        episodesOnCurrentCourse = 1;
        recentRewards.Clear();
        Debug.Log($"[CourseLoader] >>> START curriculum with '{pick.name}' " +
                  $"(threshold {masteryRewardThreshold}, window {masteryWindowSize}, min {minEpisodesPerCourse} eps)");
        LoadCourse();
    }

    // Called by HorseAgent at the start of each episode (BEFORE LoadNextTrainingCourse) to feed
    // the previous episode's total reward into the moving-average mastery tracker.
    public void RecordEpisodeReward(float episodeReward) {
        if (!swapCoursesDuringTraining) return;
        recentRewards.Add(episodeReward);
        while (recentRewards.Count > masteryWindowSize) recentRewards.RemoveAt(0);
    }

    private TextAsset PickNextCurriculumOrRandom() {
        // 1. Curriculum phase — go through curriculumCourses in order.
        if (curriculumCourses != null && curriculumIndex < curriculumCourses.Length) {
            var c = curriculumCourses[curriculumIndex];
            curriculumIndex++;
            if (c != null) return c;
            // null entry — skip and try next slot / random pool
        }

        // 2. Random phase — pick from trainingCourses (avoid repeating same course twice in a row
        //    so the agent actually sees a different layout).
        if (trainingCourses != null && trainingCourses.Length > 0) {
            // Single-item pool: just return it.
            if (trainingCourses.Length == 1) return trainingCourses[0];

            for (int i = 0; i < 8; i++) { // bounded retries
                var c = trainingCourses[Random.Range(0, trainingCourses.Length)];
                if (c != null && c != currentTrainingCourse) return c;
            }
            // Couldn't find a different one — accept any non-null.
            foreach (var c in trainingCourses) if (c != null) return c;
        }

        return null;
    }

    // Manual / context-menu helper: pure random pick, ignoring curriculum & pacing.
    [ContextMenu("Load Random Course (no pacing)")]
    public void LoadRandomCourse() {
        if (trainingCourses == null || trainingCourses.Length == 0) {
            if (courseJson != null) LoadCourse();
            return;
        }
        var pick = trainingCourses[Random.Range(0, trainingCourses.Length)];
        if (pick == null) { if (courseJson != null) LoadCourse(); return; }
        courseJson = pick;
        LoadCourse();
    }

    [ContextMenu("Clear Course")]
    public void ClearCourse() {
        if (coursePathManager != null) {
            if (loadedStartSensor != null && coursePathManager.startPoint == loadedStartSensor)
                coursePathManager.startPoint = null;
            if (loadedFinishSensor != null && coursePathManager.finishPoint == loadedFinishSensor)
                coursePathManager.finishPoint = null;
        }
        if (horseAgent != null && loadedFinishSensor != null && horseAgent.finishGate == loadedFinishSensor)
            horseAgent.finishGate = null;
        loadedStartSensor = null;
        loadedFinishSensor = null;

        var toRemove = new List<GameObject>();
        foreach (Transform child in transform) {
            if (child.name.StartsWith("Hurdle_") || child.name.StartsWith("Arena_")
                || child.name == "Course_StartSensor" || child.name == "Course_FinishSensor")
                toRemove.Add(child.gameObject);
        }
        foreach (var go in toRemove) {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
        spawned.Clear();
        arenaObjects.Clear();
    }

    // ─── Building one hurdle ─────────────────────────────────────────────
    GameObject CreateHurdle(string name, Vector3 pos, float rotY, float height) {
        // Prefab path (preferred). The prefab carries its own colliders, materials, animator,
        // and trigger volume — none of the procedural builder fields below apply.
        if (hurdlePrefab != null) {
            return InstantiateHurdleFromPrefab(name, pos, rotY, height);
        }

        // Procedural fallback (legacy). Built from primitives; no animator, no fancy mesh.
        var parent = new GameObject(name);
        parent.transform.SetParent(transform);
        parent.transform.position = pos;
        parent.transform.rotation = RotationFromDesignerJsonYaw(rotY);
        TrySetTag(parent, "Hurdle");

        // Posts
        AddCube(parent.transform, "LeftPost",
            new Vector3(-railLength * 0.5f, height * 0.5f, 0),
            new Vector3(postWidth, height, postWidth),
            postMaterial);
        AddCube(parent.transform, "RightPost",
            new Vector3(railLength * 0.5f, height * 0.5f, 0),
            new Vector3(postWidth, height, postWidth),
            postMaterial);

        // Rails — alternating red/white bands like real show jumping.
        // Decorative by default: the trained policy approaches the hurdle and jumps based on
        // the trigger volume below, not on physical contact with the bands. Solid bands at
        // y = height - 0.06 would be unjumpable with the prefab's jumpForce = 5.
        const int bands = 5;
        float bandW = railLength / bands;
        for (int i = 0; i < bands; i++) {
            float bx = -railLength * 0.5f + bandW * 0.5f + i * bandW;
            AddCube(parent.transform, $"TopBand_{i}",
                new Vector3(bx, height - 0.06f, 0),
                new Vector3(bandW * 0.95f, 0.10f, 0.10f),
                i % 2 == 0 ? railWhiteMaterial : railRedMaterial,
                removeCollider: !solidRails);
            AddCube(parent.transform, $"BotBand_{i}",
                new Vector3(bx, height * 0.55f, 0),
                new Vector3(bandW * 0.95f, 0.10f, 0.10f),
                i % 2 == 0 ? railRedMaterial : railWhiteMaterial,
                removeCollider: !solidRails);
        }

        // Detection trigger (invisible). Child object so HorseAgent's
        // OnTriggerEnter (`t == target || t.IsChildOf(target)`) accepts it.
        var trig = new GameObject("Trigger");
        trig.transform.SetParent(parent.transform);
        trig.transform.localPosition = new Vector3(0, height * 0.5f, 0);
        trig.transform.localRotation = Quaternion.identity;
        var col = trig.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(triggerWidth, triggerHeight, triggerDepth);

        return parent;
    }

    GameObject InstantiateHurdleFromPrefab(string name, Vector3 pos, float rotY, float height) {
        // Position+rotation are applied AFTER parenting so they're treated as world-space
        // (matches the procedural builder's behavior). Instantiate(prefab, pos, rot, parent)
        // also works, but if CourseLoader's GameObject is offset from world origin the
        // resulting local-space conversion can confuse the JSON's "unityPosition" expectations.
        var go = Instantiate(hurdlePrefab, transform);
        go.name = name;
        go.transform.position = pos;
        go.transform.rotation = RotationFromDesignerJsonYaw(rotY);

        // Tag should already be "Hurdle" on the prefab root, but enforce it so a misconfigured
        // prefab doesn't silently break HorseAgent's OnCollisionEnter classification.
        TrySetTag(go, "Hurdle");

        if (scalePrefabToJsonHeight && prefabNativeHeight > 0.01f) {
            float scale = height / prefabNativeHeight;
            Vector3 s = go.transform.localScale;
            go.transform.localScale = new Vector3(s.x, s.y * scale, s.z);
            // Note: this scales the prefab uniformly along Y. Leg thickness, bar diameter,
            // and trigger height all scale together. For more faithful height variation
            // ship multiple prefabs (low/medium/high) and swap them per course.
        }

        // Sanity check — fail loudly during development rather than silently miss hurdles.
        if (go.GetComponentInChildren<Collider>() == null) {
            Debug.LogError($"[CourseLoader] Hurdle prefab '{hurdlePrefab.name}' has no Collider " +
                           "in its hierarchy. The horse won't be able to clear it. Add at least " +
                           "a trigger Collider on the root.");
        } else if (!HasTriggerCollider(go)) {
            Debug.LogWarning($"[CourseLoader] Hurdle prefab '{hurdlePrefab.name}' has no TRIGGER " +
                             "collider — HorseAgent.OnTriggerEnter will never fire and hurdle " +
                             "clears won't be rewarded. Set isTrigger=true on at least one of " +
                             "the prefab's Colliders.");
        }

        return go;
    }

    static bool HasTriggerCollider(GameObject root) {
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) {
            if (c.isTrigger) return true;
        }
        return false;
    }

    void AddCube(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool removeCollider = false) {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<Renderer>().material = mat;
        if (removeCollider) {
            var col = go.GetComponent<Collider>();
            if (col != null) {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
        }
    }

    // ─── Arena ───────────────────────────────────────────────────────────
    // Parses strings like "60x40m", "60×40", "60 x 40 m" → Vector2(width, length).
    Vector2 ParseArenaSize(string s, Vector2 fallback) {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var clean = s.Replace("m", "").Replace("M", "")
                     .Replace("×", "x").Replace("X", "x")
                     .Replace(" ", "").Trim();
        var parts = clean.Split('x');
        if (parts.Length < 2) return fallback;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, ci, out var w)
         && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out var l)
         && w > 0 && l > 0) {
            return new Vector2(w, l);
        }
        return fallback;
    }

    void BuildArena(Vector2 size) {
        // Ground plane. Unity's built-in Plane is 10×10 units at scale 1, so divide by 10.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Arena_Ground";
        ground.transform.SetParent(transform);
        ground.transform.localPosition = Vector3.zero;
        ground.transform.localRotation = Quaternion.identity;
        ground.transform.localScale = new Vector3(size.x / 10f, 1f, size.y / 10f);
        if (groundMaterial != null) ground.GetComponent<Renderer>().material = groundMaterial;
        TrySetTag(ground, "Ground"); // HorseAgent's OnCollisionEnter checks this tag
        SetGroundLayerRecursive(ground);  // HorseAgent.groundLayer is a LayerMask — must match
        arenaObjects.Add(ground.transform);

        if (buildWalls) BuildWalls(size);

        // Keep ArenaBounds in sync so CoursePathManager / HorseAgent reasoning stays correct.
        var bounds = GetComponent<ArenaBounds>();
        if (bounds != null) {
            bounds.center = transform.position;
            bounds.width = size.x;
            bounds.length = size.y;
        }
    }

    // Wires a top-down camera into the horse's "TopCam" CameraSensorComponent so the trained
    // policy receives the visual input it expects. The training prefab's TopCam was at height
    // = max(W,L) * sqrt(3)/2 (so a 60° square FOV exactly fits the arena); we match that.
    void EnsureTopDownCamera(Vector2 size) {
        if (horseAgent == null) {
            Debug.LogWarning("[CourseLoader] autoBuildTopDownCamera is on but no HorseAgent is assigned — skipping camera setup.");
            return;
        }

        CameraSensorComponent target = null;
        foreach (var s in horseAgent.GetComponents<CameraSensorComponent>()) {
            if (s.SensorName == topCamSensorName) { target = s; break; }
        }
        if (target == null) {
            Debug.LogWarning($"[CourseLoader] No CameraSensorComponent named '{topCamSensorName}' found on the HorseAgent — skipping camera setup.");
            return;
        }

        // Reuse: explicit user-supplied camera > camera already on the sensor > new build.
        Camera cam = topDownCamera != null ? topDownCamera : target.Camera;

        if (cam == null) {
            var go = new GameObject("Arena_TopDownCam");
            go.transform.SetParent(transform);
            float halfFovRad = topDownCameraFov * 0.5f * Mathf.Deg2Rad;
            float maxSide = Mathf.Max(size.x, size.y);
            // 5% headroom so border walls aren't clipped at the edge of the frame.
            float height = (maxSide * 0.5f) / Mathf.Tan(halfFovRad) * 1.05f;
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            cam = go.AddComponent<Camera>();
            cam.fieldOfView = topDownCameraFov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = Mathf.Max(1000f, height * 2f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.19f, 0.30f, 0.47f, 0f);
            // Match the training prefab: the Camera component itself stays disabled — the
            // CameraSensorComponent enables it on demand each time it renders an observation.
            cam.enabled = false;
            arenaObjects.Add(go.transform);
            Debug.Log($"[CourseLoader] Built top-down camera at height {height:0.0}m for the {target.SensorName} sensor.");
        } else if (topDownCamera != null) {
            // User-supplied camera: keep their settings, but reposition it over the arena
            // so a non-square arena still fits the trained 60° FOV.
            float halfFovRad = topDownCameraFov * 0.5f * Mathf.Deg2Rad;
            float maxSide = Mathf.Max(size.x, size.y);
            float height = (maxSide * 0.5f) / Mathf.Tan(halfFovRad) * 1.05f;
            cam.transform.position = transform.position + new Vector3(0f, height, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        target.Camera = cam;
    }

    // Sets the GameObject (and all children) onto the layer the HorseAgent's groundLayer mask
    // expects. Without this the Physics.CheckSphere ground check fails forever, isGrounded stays
    // false, and a trained policy freezes because it never sees that observation state.
    void SetGroundLayerRecursive(GameObject go) {
        int layer = LayerMask.NameToLayer(groundLayerName);
        if (layer < 0) {
            Debug.LogWarning($"[CourseLoader] Layer '{groundLayerName}' not found in Project Settings → Tags and Layers. " +
                             $"Falling back to layer index {groundLayerFallback}. " +
                             "If the horse looks frozen / never jumps, fix the ground layer.");
            layer = Mathf.Clamp(groundLayerFallback, 0, 31);
        }
        go.layer = layer;
        foreach (Transform child in go.transform) SetGroundLayerRecursive(child.gameObject);
    }

    void BuildWalls(Vector2 size) {
        float w = size.x, l = size.y, h = wallHeight, t = wallThickness;
        var parent = new GameObject("Arena_Walls");
        parent.transform.SetParent(transform);
        parent.transform.localPosition = Vector3.zero;
        parent.transform.localRotation = Quaternion.identity;
        arenaObjects.Add(parent.transform);

        AddCube(parent.transform, "Wall_North",
            new Vector3(0, h * 0.5f,  l * 0.5f + t * 0.5f),
            new Vector3(w + t * 2f, h, t), wallMaterial);
        AddCube(parent.transform, "Wall_South",
            new Vector3(0, h * 0.5f, -l * 0.5f - t * 0.5f),
            new Vector3(w + t * 2f, h, t), wallMaterial);
        AddCube(parent.transform, "Wall_East",
            new Vector3( w * 0.5f + t * 0.5f, h * 0.5f, 0),
            new Vector3(t, h, l), wallMaterial);
        AddCube(parent.transform, "Wall_West",
            new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0),
            new Vector3(t, h, l), wallMaterial);
    }

    void EnsureMaterials() {
        if (!Application.isPlaying) return; // skip in edit mode (avoids leaked materials)

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (postMaterial == null)      { postMaterial      = new Material(shader); postMaterial.color      = new Color(0.95f, 0.93f, 0.86f); }
        if (railWhiteMaterial == null) { railWhiteMaterial = new Material(shader); railWhiteMaterial.color = new Color(0.92f, 0.88f, 0.78f); }
        if (railRedMaterial == null)   { railRedMaterial   = new Material(shader); railRedMaterial.color   = new Color(0.78f, 0.30f, 0.30f); }
        if (groundMaterial == null)    { groundMaterial    = new Material(shader); groundMaterial.color    = new Color(0.50f, 0.65f, 0.43f); }
        if (wallMaterial == null)      { wallMaterial      = new Material(shader); wallMaterial.color      = new Color(0.85f, 0.82f, 0.74f); }
    }

    void TrySetTag(GameObject go, string tag) {
        try {
            go.tag = tag;
        } catch (UnityException) {
            Debug.LogWarning($"[CourseLoader] Tag '{tag}' doesn't exist. Add it in Project Settings → Tags and Layers, then reload.");
        }
    }
}
