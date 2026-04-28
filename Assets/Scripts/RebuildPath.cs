using UnityEngine;

public class CourseDemoSetup : MonoBehaviour
{
    public CoursePathManager pathManager;

    private void Start()
    {
        pathManager.RebuildPath();
    }
}