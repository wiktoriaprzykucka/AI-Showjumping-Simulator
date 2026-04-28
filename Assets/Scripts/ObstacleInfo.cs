using UnityEngine;

public class ObstacleInfo : MonoBehaviour
{
    [Header("Obstacle Parts")]
    public Transform leftStand;
    public Transform rightStand;
    public Transform topPole;

    [Header("Approach Settings")]
    public float minTakeoffDistance = 1.5f;
    public float maxTakeoffDistance = 3.0f;

    public Vector3 GetCenter()
    {
        if (leftStand != null && rightStand != null)
        {
            return (leftStand.position + rightStand.position) / 2f;
        }

        return transform.position;
    }

    public Vector3 GetForward()
    {
        return transform.forward.normalized;
    }

    public float GetWidth()
    {
        if (leftStand != null && rightStand != null)
        {
            return Vector3.Distance(leftStand.position, rightStand.position);
        }

        return 0f;
    }

    public float GetHeight()
    {
        if (topPole != null)
        {
            return topPole.position.y - transform.position.y;
        }

        return 0f;
    }
}