using UnityEngine;

public class CourseObstacle : MonoBehaviour
{
    [Header("Order")]
    public int orderIndex;

    [Header("Type")]
    public ObstacleType obstacleType = ObstacleType.Vertical;
    public CombinationRole combinationRole = CombinationRole.None;

    [Header("Visual")]
    public Transform visualModel;

    [Header("Logical Parameters")]
    public float logicalHeight = 1.10f;
    public float logicalWidth = 2.00f;

    [Range(0.5f, 3f)]
    public float difficultyWeight = 1.0f;

    [Header("Path Distances")]
    public float baseApproachDistance = 8.0f;
    public float baseLandingDistance = 6.0f;

    [Header("Optional Combination Info")]
    public bool isPartOfCombination = false;

    public Vector3 JumpCenter => transform.position;

    public float RequiredApproachDistance
    {
        get
        {
            return baseApproachDistance + logicalHeight * 2.0f + difficultyWeight * 0.5f;
        }
    }

    public float RequiredLandingDistance
    {
        get
        {
            return baseLandingDistance + logicalHeight * 1.5f + difficultyWeight * 0.5f;
        }
    }

    public Vector3 ApproachPoint => transform.position - transform.forward * RequiredApproachDistance;

    public Vector3 LandingPoint => transform.position + transform.forward * RequiredLandingDistance;

    private void OnDrawGizmos()
    {
        Vector3 center = transform.position;

        // Red = approach side
        Gizmos.color = Color.red;
        Gizmos.DrawLine(center, ApproachPoint);
        Gizmos.DrawSphere(ApproachPoint, 0.25f);

        // White = jump center
        Gizmos.color = Color.white;
        Gizmos.DrawSphere(JumpCenter, 0.25f);

        // Blue = landing side / forward direction
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(center, LandingPoint);
        Gizmos.DrawSphere(LandingPoint, 0.25f);

        // Green = obstacle right direction
        Gizmos.color = Color.green;
        Gizmos.DrawLine(center, center + transform.right * 2f);
    }
}