using UnityEngine;

public class CourseSettings : MonoBehaviour
{
    [Header("Competition Settings")]

    [Range(0.30f, 1.60f)]
    public float height = 1.10f;

    public float speed = 350f;

    public float minHeight = 0.30f;
    public float maxHeight = 1.60f;

    public bool IsHeightValid()
    {
        return height >= minHeight && height <= maxHeight;
    }
}