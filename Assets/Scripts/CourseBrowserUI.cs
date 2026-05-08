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

    [Tooltip("Width of the panel, in pixels.")]
    public int panelWidth = 200;

    [Tooltip("Height of the panel, in pixels.")]
    public int panelHeight = 240;

    [Tooltip("Margin from the screen edges, in pixels.")]
    public int panelMargin = 8;

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
        if (_stylesReady) return;

        var bg = MakeColorTexture(new Color(0f, 0f, 0f, 0.85f));
        _panelStyle = new GUIStyle { normal = { background = bg }, padding = new RectOffset(4, 4, 4, 4) };

        _titleStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 11, fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        _rowStyle = new GUIStyle(GUI.skin.button) {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 10, fontStyle = FontStyle.Bold,
            padding = new RectOffset(4, 4, 1, 1),
        };
        _rowStyle.normal.textColor = Color.white;
        _rowStyle.hover.textColor  = Color.yellow;

        _rowSubStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 8, fontStyle = FontStyle.Italic,
            padding = new RectOffset(6, 4, 0, 1),
            normal = { textColor = new Color(0.75f, 0.85f, 1f, 1f) },
        };

        _hintStyle = new GUIStyle(GUI.skin.label) {
            fontSize = 10, fontStyle = FontStyle.Bold, richText = true,
            normal = { textColor = Color.white, background = MakeColorTexture(new Color(0f, 0f, 0f, 0.6f)) },
            padding = new RectOffset(5, 5, 2, 2),
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

        // Bottom-right anchor
        int w = Mathf.Min(panelWidth, Screen.width  - panelMargin * 2);
        int h = Mathf.Min(panelHeight, Screen.height - panelMargin * 2);
        int x = Screen.width  - w - panelMargin;
        int y = Screen.height - h - panelMargin;

        if (!_visible) {
            var hintRect = new Rect(Screen.width - 230 - panelMargin, Screen.height - 24 - panelMargin, 230, 24);
            GUI.Label(hintRect,
                $"Press <color=yellow>{toggleKey}</color> for course browser",
                _hintStyle);
            return;
        }

        var rect = new Rect(x, y, w, h);
        GUI.Box(rect, GUIContent.none, _panelStyle);

        GUILayout.BeginArea(new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8));

        // Title row + buttons
        GUILayout.BeginHorizontal();
        GUILayout.Label("PARKOURS", _titleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↻", GUILayout.Width(18), GUILayout.Height(16))) ScanCourses();
        if (GUILayout.Button("✕", GUILayout.Width(18), GUILayout.Height(16))) _visible = false;
        GUILayout.EndHorizontal();

        // Search
        GUILayout.BeginHorizontal();
        GUILayout.Label("🔍", GUILayout.Width(14));
        string newSearch = GUILayout.TextField(_search ?? "", GUILayout.MinWidth(60));
        if (newSearch != _search) { _search = newSearch; Refilter(); }
        GUILayout.EndHorizontal();

        // Folder cycle
        string folderLabel = _folderOptions[Mathf.Clamp(_folderIndex, 0, _folderOptions.Length - 1)];
        if (GUILayout.Button("📁 " + folderLabel, GUILayout.Height(18))) {
            _folderIndex = (_folderIndex + 1) % _folderOptions.Length;
            Refilter();
        }

        // Sort cycle
        if (GUILayout.Button("⇵ " + _sortOptions[_sortIndex], GUILayout.Height(18))) {
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
                if (GUILayout.Button(entry.displayName, _rowStyle, GUILayout.Height(16))) {
                    LoadCourse(entry);
                }
                GUILayout.Label($"{entry.folderLabel}  ·  {entry.modified:yyyy-MM-dd}", _rowSubStyle);
            }
        }
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }
}
