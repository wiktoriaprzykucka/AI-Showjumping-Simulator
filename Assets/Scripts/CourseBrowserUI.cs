using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Runtime UI to browse and load parkour course JSON files into a CourseLoader.
///
/// Uses Unity's IMGUI (OnGUI) so it works in any render pipeline / Unity version
/// without any prefab dependencies or Canvas configuration.
///
/// Scans:
///   • Editor / source-mode  : <project>/Assets/<assetsCoursesSubfolder>/**/*.json
///   • Built player          : <Application.streamingAssetsPath>/<assetsCoursesSubfolder>/**/*.json
///   • Optional extra folder : `extraSearchFolder` (absolute or relative to project)
/// </summary>
[DefaultExecutionOrder(100)]
public class CourseBrowserUI : MonoBehaviour {

    [Header("Wiring")]
    [Tooltip("CourseLoader that will receive the chosen JSON. Auto-found if left empty.")]
    public CourseLoader courseLoader;

    [Header("Folders")]
    [Tooltip("Folder under Assets/ that holds course JSON files. Subfolders are scanned recursively.")]
    public string assetsCoursesSubfolder = "Courses";

    [Tooltip("Optional extra folder to scan. Absolute path or relative to project root.")]
    public string extraSearchFolder = "";

    [Header("UI")]
    [Tooltip("Press this key during play to toggle the panel.")]
    public KeyCode toggleKey = KeyCode.F2;

    [Tooltip("Visible when the scene starts.")]
    public bool startVisible = true;

    [Tooltip("Width of the panel, in pixels (before uiScale is applied).")]
    [Range(100, 600)] public int panelWidth = 260;

    [Tooltip("Height of the panel, in pixels (before uiScale is applied).")]
    [Range(100, 800)] public int panelHeight = 420;

    [Tooltip("Gap between the panel (after scaling) and the screen edges, in pixels.")]
    [Range(0, 120)] public int panelMargin = 8;

    [Tooltip("Uniform scale — shrinks the whole panel including text. 1 = full size, 0.5 = half. Tune this to control visual size.")]
    [Range(0.35f, 1f)] public float uiScale = 0.55f;

    // ── runtime state ──────────────────────────────────────────────────────
    private readonly List<CourseEntry> _allCourses = new();
    private readonly List<CourseEntry> _filtered   = new();

    private string _search = "";
    private int    _folderIndex = 0;
    private int    _sortIndex = 0;
    private string[] _folderOptions = new[] { "All" };
    private readonly string[] _sortOptions = { "Name A→Z", "Name Z→A", "Newest", "Oldest", "Folder" };
    private Vector2 _scroll;
    private string  _status = "";
    private bool    _visible;

    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _rowStyle;
    private GUIStyle _rowSubStyle;
    private GUIStyle _hintStyle;
    private bool _stylesReady;
    private float _builtAtScale = -1f;

    private class CourseEntry {
        public string fullPath;
        public string fileName;
        public string displayName;
        public string folderLabel;
        public DateTime modified;
    }

    // ── lifecycle ──────────────────────────────────────────────────────────
    void Start() {
        if (courseLoader == null) {
            courseLoader = FindFirstObjectByType<CourseLoader>();
            if (courseLoader == null)
                Debug.LogWarning("[CourseBrowserUI] No CourseLoader assigned and none found in scene.");
        }
        ScanCourses();
        _visible = startVisible;
    }

    void Update() {
        if (Input.GetKeyDown(toggleKey)) _visible = !_visible;
    }

    // ── scanning ───────────────────────────────────────────────────────────
    public void ScanCourses() {
        _allCourses.Clear();

        var roots = new List<string>();
#if UNITY_EDITOR
        roots.Add(Path.Combine(Application.dataPath, assetsCoursesSubfolder));
#else
        roots.Add(Path.Combine(Application.streamingAssetsPath, assetsCoursesSubfolder));
#endif
        if (!string.IsNullOrWhiteSpace(extraSearchFolder)) {
            string p = Path.IsPathRooted(extraSearchFolder)
                ? extraSearchFolder
                : Path.Combine(Directory.GetCurrentDirectory(), extraSearchFolder);
            roots.Add(p);
        }

        foreach (var root in roots) {
            if (!Directory.Exists(root)) continue;
            string rootFull = Path.GetFullPath(root);

            string[] files;
            try {
                files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            } catch (Exception e) {
                Debug.LogWarning($"[CourseBrowserUI] Failed to scan '{root}': {e.Message}");
                continue;
            }

            foreach (var f in files) {
                string full = Path.GetFullPath(f);
                string rel  = full.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dir  = Path.GetDirectoryName(rel);
                string folderLabel = string.IsNullOrEmpty(dir) ? "(root)" : dir.Replace('\\', '/');

                _allCourses.Add(new CourseEntry {
                    fullPath    = full,
                    fileName    = Path.GetFileName(f),
                    displayName = Path.GetFileNameWithoutExtension(f),
                    folderLabel = folderLabel,
                    modified    = File.GetLastWriteTime(full),
                });
            }
        }

        var distinct = _allCourses.Select(c => c.folderLabel).Distinct().OrderBy(s => s).ToList();
        var opts = new List<string> { "All" };
        opts.AddRange(distinct);
        _folderOptions = opts.ToArray();
        if (_folderIndex >= _folderOptions.Length) _folderIndex = 0;

        Debug.Log($"[CourseBrowserUI] Found {_allCourses.Count} course JSON file(s) across {distinct.Count} folder(s).");
        Refilter();
    }

    private void Refilter() {
        _filtered.Clear();
        string query = (_search ?? "").Trim().ToLowerInvariant();
        string folder = _folderOptions[Mathf.Clamp(_folderIndex, 0, _folderOptions.Length - 1)];

        foreach (var c in _allCourses) {
            if (folder != "All" && c.folderLabel != folder) continue;
            if (query.Length > 0 &&
                !c.displayName.ToLowerInvariant().Contains(query) &&
                !c.folderLabel.ToLowerInvariant().Contains(query)) continue;
            _filtered.Add(c);
        }

        switch (_sortIndex) {
            case 0: _filtered.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase)); break;
            case 1: _filtered.Sort((a, b) => string.Compare(b.displayName, a.displayName, StringComparison.OrdinalIgnoreCase)); break;
            case 2: _filtered.Sort((a, b) => b.modified.CompareTo(a.modified)); break;
            case 3: _filtered.Sort((a, b) => a.modified.CompareTo(b.modified)); break;
            case 4: _filtered.Sort((a, b) => {
                int f = string.Compare(a.folderLabel, b.folderLabel, StringComparison.OrdinalIgnoreCase);
                return f != 0 ? f : string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
            }); break;
        }

        _status = $"{_filtered.Count} / {_allCourses.Count} courses";
    }

    private void LoadCourse(CourseEntry entry) {
        if (entry == null || courseLoader == null) {
            _status = "No CourseLoader wired.";
            return;
        }
        try {
            string text = File.ReadAllText(entry.fullPath);
            courseLoader.LoadCourseFromJson(text, entry.displayName);
            _status = $"Loaded: {entry.displayName}";
            Debug.Log($"[CourseBrowserUI] Loaded '{entry.displayName}' from {entry.fullPath}");
        } catch (Exception e) {
            _status = "Error: " + e.Message;
            Debug.LogError($"[CourseBrowserUI] Failed to load '{entry.fullPath}': {e}");
        }
    }

    // ── IMGUI ──────────────────────────────────────────────────────────────
    private void EnsureStyles() {
        if (_stylesReady && Mathf.Approximately(_builtAtScale, uiScale)) return;
        _stylesReady = false;
        _builtAtScale = uiScale;

        var bg = MakeColorTexture(new Color(0f, 0f, 0f, 0.85f));
        _panelStyle = new GUIStyle { normal = { background = bg }, padding = new RectOffset(6, 6, 6, 6) };

        _titleStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 14, fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        _rowStyle = new GUIStyle(GUI.skin.button) {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13, fontStyle = FontStyle.Bold,
            padding = new RectOffset(6, 6, 3, 3),
        };
        _rowStyle.normal.textColor = Color.white;
        _rowStyle.hover.textColor  = Color.yellow;

        _rowSubStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 11, fontStyle = FontStyle.Italic,
            padding = new RectOffset(8, 4, 0, 2),
            normal = { textColor = new Color(0.75f, 0.85f, 1f, 1f) },
        };

        _hintStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 12, fontStyle = FontStyle.Bold, richText = true,
            normal = { textColor = Color.white, background = MakeColorTexture(new Color(0f, 0f, 0f, 0.6f)) },
            padding = new RectOffset(6, 6, 3, 3),
        };

        _stylesReady = true;
    }

    private static Texture2D MakeColorTexture(Color c) {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var pixels = new Color[4]; for (int i = 0; i < 4; i++) pixels[i] = c;
        t.SetPixels(pixels); t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    void OnGUI() {
        EnsureStyles();

        // Bottom-right pivot + ScaleAroundPivot: visual size is (w × scale, h × scale).
        // Layout size must be capped using maxW,maxH /= scale so the scaled panel stays
        // fully inside [margin, Screen − margin].
        int margin = Mathf.Clamp(panelMargin, 0, 120);
        margin = Mathf.Min(margin, Mathf.Max(0, Screen.width / 2 - 8));
        margin = Mathf.Min(margin, Mathf.Max(0, Screen.height / 2 - 8));

        float scale = Mathf.Clamp(uiScale, 0.35f, 1f);
        float safeW = Mathf.Max(0f, Screen.width - 2f * margin);
        float safeH = Mathf.Max(0f, Screen.height - 2f * margin);
        float maxLayoutW = safeW / scale;
        float maxLayoutH = safeH / scale;

        int upperW = Mathf.Max(1, Mathf.FloorToInt(maxLayoutW));
        int upperH = Mathf.Max(1, Mathf.FloorToInt(maxLayoutH));
        int w = Mathf.Clamp(panelWidth, Mathf.Min(100, upperW), upperW);
        int h = Mathf.Clamp(panelHeight, Mathf.Min(100, upperH), upperH);

        int x = Mathf.Max(margin, Screen.width - margin - w);
        int y = Mathf.Max(margin, Screen.height - margin - h);

        Matrix4x4 oldMatrix = GUI.matrix;
        Vector2 panelBottomRight = new Vector2(Screen.width - margin, Screen.height - margin);
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), panelBottomRight);

        if (!_visible) {
            float hw = Mathf.Min(230f, Mathf.Max(40f, safeW));
            float hintX = Mathf.Max(margin, Screen.width - hw - margin);
            float hintY = Mathf.Max(margin, Screen.height - 24 - margin);
            var hintRect = new Rect(hintX, hintY, hw, 24);
            GUI.Label(hintRect,
                $"Press <color=yellow>{toggleKey}</color> for course browser",
                _hintStyle);
            GUI.matrix = oldMatrix;
            return;
        }

        var rect = new Rect(x, y, w, h);
        GUI.Box(rect, GUIContent.none, _panelStyle);

        GUILayout.BeginArea(new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8));

        // Title row + buttons
        GUILayout.BeginHorizontal();
        GUILayout.Label("PARKOURS", _titleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↻", GUILayout.Width(24), GUILayout.Height(22))) ScanCourses();
        if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(22))) _visible = false;
        GUILayout.EndHorizontal();

        // Search
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(46));
        string newSearch = GUILayout.TextField(_search ?? "", GUILayout.MinWidth(80), GUILayout.Height(20));
        if (newSearch != _search) { _search = newSearch; Refilter(); }
        GUILayout.EndHorizontal();

        // Folder cycle
        string folderLabel = _folderOptions[Mathf.Clamp(_folderIndex, 0, _folderOptions.Length - 1)];
        if (GUILayout.Button("Folder: " + folderLabel, GUILayout.Height(22))) {
            _folderIndex = (_folderIndex + 1) % _folderOptions.Length;
            Refilter();
        }

        // Sort cycle
        if (GUILayout.Button("Sort: " + _sortOptions[_sortIndex], GUILayout.Height(22))) {
            _sortIndex = (_sortIndex + 1) % _sortOptions.Length;
            Refilter();
        }

        GUILayout.Label(_status, _rowSubStyle);

        // List
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        if (_filtered.Count == 0) {
            GUILayout.Label("(no matches)");
        } else {
            for (int i = 0; i < _filtered.Count; i++) {
                var entry = _filtered[i];
                if (GUILayout.Button(entry.displayName, _rowStyle, GUILayout.Height(24))) {
                    LoadCourse(entry);
                }
                GUILayout.Label($"{entry.folderLabel}  ·  {entry.modified:yyyy-MM-dd}", _rowSubStyle);
            }
        }
        GUILayout.EndScrollView();

        GUILayout.EndArea();
        GUI.matrix = oldMatrix;
    }
}
