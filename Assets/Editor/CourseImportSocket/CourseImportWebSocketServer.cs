#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Fleck;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Local WebSocket server (Fleck) so the HTML course designer can push JSON straight into Assets/Courses.
/// Menu: Tools → Course Import → Start/Stop WebSocket Server. Default URL ws://127.0.0.1:8765
/// </summary>
[Serializable]
internal class SaveCourseWsMessage {
    public string type;
    public string relativePath;
    public string jsonText;
}

[Serializable]
internal class WsReplyDto {
    public bool ok;
    public string error;
    public string assetPath;
}

public static class CourseImportWebSocketServer {
    const int DefaultPort = 8765;
    static WebSocketServer s_server;
    static readonly ConcurrentQueue<PendingSave> s_queue = new ConcurrentQueue<PendingSave>();
    static bool s_updateHooked;

    struct PendingSave {
        public string FullPath;
        public string JsonText;
        public IWebSocketConnection Connection;
    }

    static CourseImportWebSocketServer() {
        EditorApplication.quitting += OnEditorQuitting;
    }

    static void OnEditorQuitting() {
        if (s_server == null) return;
        try {
            s_server.Dispose();
        } catch {
            // ignore shutdown races
        } finally {
            s_server = null;
            UnhookUpdate();
        }
    }

    [MenuItem("Tools/Course Import/Start WebSocket Server", false, 0)]
    public static void StartServer() {
        if (s_server != null) {
            Debug.LogWarning("[CourseImport] Server already running.");
            return;
        }

        HookUpdate();
        try {
            FleckLog.Level = LogLevel.Warn;
            s_server = new WebSocketServer($"ws://127.0.0.1:{DefaultPort}");
            s_server.Start(conn => {
                conn.OnMessage = raw => HandleMessage(conn, raw);
            });
        } catch (Exception e) {
            UnhookUpdate();
            s_server = null;
            Debug.LogError($"[CourseImport] Failed to bind ws://127.0.0.1:{DefaultPort} — {e.Message}");
            return;
        }

        Debug.Log($"[CourseImport] WebSocket server listening ws://127.0.0.1:{DefaultPort} (send from Course Designer)");
    }

    [MenuItem("Tools/Course Import/Start WebSocket Server", true)]
    static bool StartServer_Validate() => s_server == null;

    [MenuItem("Tools/Course Import/Stop WebSocket Server", false, 1)]
    public static void StopServer() {
        try {
            s_server?.Dispose();
        } catch (Exception e) {
            Debug.LogWarning($"[CourseImport] Dispose: {e.Message}");
        } finally {
            s_server = null;
            UnhookUpdate();
            Debug.Log("[CourseImport] WebSocket server stopped.");
        }
    }

    [MenuItem("Tools/Course Import/Stop WebSocket Server", true)]
    static bool StopServer_Validate() => s_server != null;

    static void HookUpdate() {
        if (!s_updateHooked) {
            EditorApplication.update += PumpMainThreadQueue;
            s_updateHooked = true;
        }
    }

    static void UnhookUpdate() {
        if (s_updateHooked) {
            EditorApplication.update -= PumpMainThreadQueue;
            s_updateHooked = false;
        }
    }

    static void HandleMessage(IWebSocketConnection conn, string raw) {
        try {
            var msg = JsonUtility.FromJson<SaveCourseWsMessage>(raw);
            if (msg == null || msg.type != "saveCourse" || string.IsNullOrEmpty(msg.jsonText)) {
                SendJson(conn, new WsReplyDto { ok = false, error = "Expected {type:'saveCourse', relativePath, jsonText}", assetPath = "" });
                return;
            }

            if (!TryGetSafeFilePath(msg.relativePath, out var fullPath, out var err)) {
                SendJson(conn, new WsReplyDto { ok = false, error = err, assetPath = "" });
                return;
            }

            s_queue.Enqueue(new PendingSave {
                FullPath = fullPath,
                JsonText = msg.jsonText,
                Connection = conn,
            });
        } catch (Exception e) {
            SendJson(conn, new WsReplyDto { ok = false, error = e.Message, assetPath = "" });
        }
    }

    static void PumpMainThreadQueue() {
        while (s_queue.TryDequeue(out var job)) {
            try {
                var dir = Path.GetDirectoryName(job.FullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(job.FullPath, job.JsonText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                AssetDatabase.Refresh();

                var tail = job.FullPath.Substring(Application.dataPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var assetPath = "Assets/" + tail.Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');

                SendJson(job.Connection, new WsReplyDto { ok = true, error = "", assetPath = assetPath });
            } catch (Exception e) {
                SendJson(job.Connection, new WsReplyDto { ok = false, error = e.Message, assetPath = "" });
            }
        }
    }

    static void SendJson(IWebSocketConnection conn, WsReplyDto dto) {
        try {
            conn.Send(JsonUtility.ToJson(dto));
        } catch (Exception e) {
            Debug.LogWarning($"[CourseImport] Reply failed: {e.Message}");
        }
    }

    /// <summary>relativePath is under Assets/ on disk (no "Assets/" prefix): e.g. Courses/Imports/2026-05-07/foo.json</summary>
    static bool TryGetSafeFilePath(string relativePath, out string fullPath, out string error) {
        fullPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(relativePath)) {
            error = "relativePath is empty";
            return false;
        }

        var norm = relativePath.Replace('\\', '/').TrimStart('/');
        if (norm.Contains("..", StringComparison.Ordinal)) {
            error = "relativePath must not contain '..'";
            return false;
        }

        if (Path.IsPathRooted(relativePath)) {
            error = "relativePath must be relative (e.g. Courses/Imports/day/file.json)";
            return false;
        }

        if (!norm.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            error = "file must end with .json";
            return false;
        }

        var combined = Path.Combine(Application.dataPath, norm.Replace('/', Path.DirectorySeparatorChar));
        fullPath = Path.GetFullPath(combined);
        var root = Path.GetFullPath(Application.dataPath);
        if (!fullPath.StartsWith(root, PathInternalUtils.StringComparison)) {
            error = "resolved path leaves Assets folder";
            return false;
        }

        return true;
    }

    /// <summary>Path segment comparison that works on Windows (case-insensitive).</summary>
    static class PathInternalUtils {
        internal static readonly StringComparison StringComparison =
            Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}
#endif
