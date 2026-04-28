using System.Collections.Generic;

public enum CourseStatus
{
    Safe,
    Risky,
    Invalid
}

public class CourseSummaryData
{
    public float height;
    public float speed;

    public float courseLength;
    public int obstacleCount;
    public int effortCount;

    public float timeAllowed;
    public float timeLimit;

    public int invalidSegments;
    public int warningSegments;

    public CourseStatus status;

    public List<string> mainIssues = new();
}