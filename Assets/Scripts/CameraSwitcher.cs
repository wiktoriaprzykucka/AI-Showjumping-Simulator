using UnityEngine;

// Toggles the Game view between the top-down arena camera and the horse's first-person
// camera with a hotkey. Both fields are null-safe — the switcher just skips a slot that
// isn't wired, instead of throwing NullReferenceException on Start (which silently broke
// the whole camera setup before).
//
// `alwaysOff` is for extra cameras that must never appear on screen — most importantly
// CourseLoader's auto-built `Arena_TopDownCam`. If two URP Base cameras are enabled at
// the same time, you get a black Game view; the always-off list prevents that.
//
// Runs late (DefaultExecutionOrder = 50) so it's the last script to touch Camera.enabled
// in Start. Otherwise CourseLoader (-100), HorseAgent's sensor wiring, or anything else
// could undo the switcher's chosen initial state.
[DefaultExecutionOrder(50)]
public class CameraSwitcher : MonoBehaviour
{
    [Tooltip("Top-down arena camera. Enabled at Start.")]
    public Camera topDownCam;

    [Tooltip("First-person camera mounted on the horse. Disabled at Start; press the toggle key to swap.")]
    public Camera horsePOVCam;

    [Tooltip("Cameras that must always stay disabled at runtime (so they don't fight the " +
             "two cameras above for the Game view). Drag CourseLoader's Arena_TopDownCam " +
             "here if you don't want it competing.")]
    public Camera[] alwaysOff;

    [Tooltip("Hotkey that swaps between top-down and POV.")]
    public KeyCode toggleKey = KeyCode.C;

    void Start()
    {
        ForceAlwaysOff();
        if (topDownCam != null) topDownCam.enabled = true;
        if (horsePOVCam != null) horsePOVCam.enabled = false;
    }

    void Update()
    {
        if (!Input.GetKeyDown(toggleKey)) return;

        ForceAlwaysOff();

        bool topActive = topDownCam != null && topDownCam.enabled;
        if (topDownCam != null)   topDownCam.enabled   = !topActive;
        if (horsePOVCam != null)  horsePOVCam.enabled  = topActive;
    }

    void ForceAlwaysOff()
    {
        if (alwaysOff == null) return;
        foreach (var c in alwaysOff)
            if (c != null && c.enabled) c.enabled = false;
    }
}
