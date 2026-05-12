using TMPro;
using UnityEngine;

/// <summary>
/// Optional component on the hurdle prefab root. <see cref="CourseLoader"/> calls
/// <see cref="SetOrder"/> after spawn so TMP(s) show 1…N (same order as <c>Hurdle_1</c> naming).
///
/// Supports two labels: one aimed at the top-down camera, one at the horse POV (named
/// <c>TopCameraView</c> / <c>HorsePov</c> by default — assign explicitly or auto-found).
/// </summary>
[DisallowMultipleComponent]
public sealed class HurdleOrderLabel : MonoBehaviour
{
    [Header("Labels (optional — auto-found by GameObject name if empty)")]
    [Tooltip("World TMP for the arena / top-down camera.")]
    [SerializeField] TMP_Text topViewLabel;

    [Tooltip("World TMP for the horse first-person / side view.")]
    [SerializeField] TMP_Text horsePovLabel;

    [Tooltip("Legacy: single TMP when you do not use the dual setup above.")]
    [SerializeField] TMP_Text label;

    [Header("Billboard")]
    [Tooltip("Rotate labels each frame toward their camera rig (see CameraSwitcher).")]
    public bool billboardFaceCamera = true;

    [Tooltip("180° Y after LookAt — try off if digits look mirrored.")]
    public bool flipYAfterLookAtTop = true;

    [Tooltip("180° Y after LookAt for the horse POV label.")]
    public bool flipYAfterLookAtHorse = true;

    [Tooltip("Optional override for legacy single-label mode only.")]
    public Camera billboardCameraOverride;

    [Header("Digit clipping")]
    [Tooltip("Expand the TMP RectTransform after SetOrder so 11, 12, … are not clipped (narrow width was hiding the 2nd digit).")]
    public bool autoResizeRectToFitDigits = true;

    [Tooltip("Extra width added to TextMeshPro’s measured size (world / rect units depending on TMP setup).")]
    public float horizontalTextPadding = 0.4f;

    CameraSwitcher _cachedSwitcher;

    void Awake()
    {
        AutoFindLabels();
        _cachedSwitcher ??= FindFirstObjectByType<CameraSwitcher>();
    }

    void AutoFindLabels()
    {
        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
        {
            string n = t.gameObject.name;
            if (topViewLabel == null &&
                (n == "TopCameraView" || n.Contains("TopCamera", System.StringComparison.OrdinalIgnoreCase)))
                topViewLabel = t;
            else if (horsePovLabel == null &&
                     (n == "HorsePov" || n.Contains("HorsePov", System.StringComparison.OrdinalIgnoreCase)))
                horsePovLabel = t;
        }

        if (label == null && topViewLabel == null && horsePovLabel == null)
            label = GetComponentInChildren<TMP_Text>(true);
    }

    public void SetOrder(int order1Based)
    {
        AutoFindLabels();
        string s = order1Based.ToString();

        if (topViewLabel != null)
            topViewLabel.text = s;
        if (horsePovLabel != null)
            horsePovLabel.text = s;

        if (topViewLabel == null && horsePovLabel == null && label != null)
            label.text = s;

        if (autoResizeRectToFitDigits)
        {
            ApplyRectFit(topViewLabel, s);
            ApplyRectFit(horsePovLabel, s);
            if (topViewLabel == null && horsePovLabel == null)
                ApplyRectFit(label, s);
        }
    }

    /// <summary>
    /// Call after setting <paramref name="tmp"/>.text so multi-digit hurdle indices don’t clip.
    /// Safe to use from <see cref="CourseLoader"/> when no <see cref="HurdleOrderLabel"/> is present.
    /// </summary>
    public static void FitRectForDigits(TMP_Text tmp, string displayedText, float horizontalPadding = 0.4f)
    {
        if (tmp == null || string.IsNullOrEmpty(displayedText))
            return;

        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.ForceMeshUpdate(true);
        Vector2 pref = tmp.GetPreferredValues(displayedText);
        RectTransform rt = tmp.rectTransform;
        if (rt == null)
            return;

        float w = pref.x + horizontalPadding;
        float h = Mathf.Max(pref.y + horizontalPadding * 0.35f, rt.sizeDelta.y);
        rt.sizeDelta = new Vector2(w, h);
    }

    void ApplyRectFit(TMP_Text tmp, string displayedText)
    {
        if (tmp == null) return;
        FitRectForDigits(tmp, displayedText, horizontalTextPadding);
    }

    void Start()
    {
        _cachedSwitcher ??= FindFirstObjectByType<CameraSwitcher>();
    }

    void LateUpdate()
    {
        if (!billboardFaceCamera)
            return;

        AutoFindLabels();
        _cachedSwitcher ??= FindFirstObjectByType<CameraSwitcher>();

        bool anyDual = topViewLabel != null || horsePovLabel != null;
        if (anyDual && _cachedSwitcher != null)
        {
            if (topViewLabel != null && _cachedSwitcher.topDownCam != null)
                FaceCamera(_cachedSwitcher.topDownCam, topViewLabel.transform, flipYAfterLookAtTop);
            if (horsePovLabel != null && _cachedSwitcher.horsePOVCam != null)
                FaceCamera(_cachedSwitcher.horsePOVCam, horsePovLabel.transform, flipYAfterLookAtHorse);
            return;
        }

        if (label == null)
            return;

        Camera cam = ResolveBillboardCameraLegacy();
        if (cam == null)
            return;

        FaceCamera(cam, label.transform, flipYAfterLookAtTop);
    }

    static void FaceCamera(Camera cam, Transform textTransform, bool flipY)
    {
        if (cam == null || textTransform == null)
            return;

        textTransform.LookAt(cam.transform.position, Vector3.up);
        if (flipY)
            textTransform.Rotate(0f, 180f, 0f);
    }

    Camera ResolveBillboardCameraLegacy()
    {
        if (billboardCameraOverride != null && billboardCameraOverride.enabled)
            return billboardCameraOverride;

        if (_cachedSwitcher != null)
        {
            if (_cachedSwitcher.horsePOVCam != null && _cachedSwitcher.horsePOVCam.enabled)
                return _cachedSwitcher.horsePOVCam;
            if (_cachedSwitcher.topDownCam != null && _cachedSwitcher.topDownCam.enabled)
                return _cachedSwitcher.topDownCam;
        }

        return Camera.main;
    }
}
