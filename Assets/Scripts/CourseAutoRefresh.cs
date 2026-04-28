using UnityEngine;

public class CourseAutoRefresh : MonoBehaviour
{
    public CoursePathManager pathManager;

    [Tooltip("Rebuild every frame during testing. Turn off later if needed.")]
    public bool autoRefresh = true;

    private void Update()
    {
        if (!autoRefresh || pathManager == null)
            return;

        pathManager.RebuildPath();
    }
}