using UnityEngine;

public class ArenaBounds : MonoBehaviour
{
    public Vector3 center = Vector3.zero;
    public float width = 30f;
    public float length = 60f;

    public float DistanceToNearestWall(Vector3 point)
    {
        float left = Mathf.Abs(point.x - (center.x - width * 0.5f));
        float right = Mathf.Abs((center.x + width * 0.5f) - point.x);
        float bottom = Mathf.Abs(point.z - (center.z - length * 0.5f));
        float top = Mathf.Abs((center.z + length * 0.5f) - point.z);

        return Mathf.Min(left, right, bottom, top);
    }

    public bool IsInsideArena(Vector3 point)
    {
        return point.x >= center.x - width * 0.5f &&
               point.x <= center.x + width * 0.5f &&
               point.z >= center.z - length * 0.5f &&
               point.z <= center.z + length * 0.5f;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, new Vector3(width, 0.1f, length));
    }
}