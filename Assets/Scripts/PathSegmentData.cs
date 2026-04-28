using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PathSegmentData
{
    public string segmentName;

    public CourseObstacle fromObstacle;
    public CourseObstacle toObstacle;

    public Vector3 startPoint;
    public Vector3 endPoint;
    public List<Vector3> curvePoints = new();

    public float distance;
    public float turnAngle;
    public float wallClearance;

    public float requiredApproachDistance;
    public float requiredLandingDistance;

    public bool isCombinationSegment;
    public float combinationDistanceScore;

    public SegmentStatus status = SegmentStatus.Safe;
    public List<string> warnings = new();

    public GameObject lineObject;
}