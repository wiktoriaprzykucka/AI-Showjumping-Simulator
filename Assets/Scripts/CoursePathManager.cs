using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CoursePathManager : MonoBehaviour
{
    [Header("References")]
    public ArenaBounds arenaBounds;
    public Transform lineParent;
    public Material lineMaterial;
    public CoursePathUI pathUI;
    public CourseSettings courseSettings;

    [Header("Start / Finish")]
    public Transform startPoint;
    public Transform finishPoint;

    [Header("Path Settings")]
    public int curveResolution = 30;
    public float controlPointStrength = 0.25f;
    public float lineHeightOffset = 0.15f;
    public float lineWidth = 0.15f;

    private readonly List<GameObject> spawnedLines = new();
    private readonly List<PathSegmentData> segments = new();

    [ContextMenu("Rebuild Path")]
    public void RebuildPath()
    {
        ClearLines();
        segments.Clear();

        List<CourseObstacle> obstacles = FindObjectsByType<CourseObstacle>(FindObjectsSortMode.None)
            .OrderBy(o => o.orderIndex)
            .ToList();

        if (obstacles.Count == 0)
        {
            pathUI?.DisplayCourseSummary(BuildCourseSummary(obstacles, segments), segments);
            return;
        }

        // START → FIRST OBSTACLE
        if (startPoint != null)
        {
            PathSegmentData startSegment = BuildFreeSegment(
                "Start → 1",
                startPoint.position,
                obstacles[0].ApproachPoint,
                startPoint.forward,
                -obstacles[0].transform.forward
            );

            EvaluateSegment(startSegment);
            DrawSegment(startSegment);
            segments.Add(startSegment);
        }

        // DRAW JUMP LINES
        foreach (CourseObstacle obstacle in obstacles)
        {
            DrawObstacleJumpLine(obstacle);
        }

        // OBSTACLE → OBSTACLE
        for (int i = 0; i < obstacles.Count - 1; i++)
        {
            PathSegmentData segment = BuildTurnSegment(obstacles[i], obstacles[i + 1]);
            EvaluateSegment(segment);
            DrawSegment(segment);
            segments.Add(segment);
        }

        // LAST OBSTACLE → FINISH
        if (finishPoint != null)
        {
            CourseObstacle last = obstacles[^1];

            PathSegmentData finishSegment = BuildFreeSegment(
                $"{last.orderIndex} → Finish",
                last.LandingPoint,
                finishPoint.position,
                last.transform.forward,
                finishPoint.forward
            );

            EvaluateSegment(finishSegment);
            DrawSegment(finishSegment);
            segments.Add(finishSegment);
        }

        CourseSummaryData summary = BuildCourseSummary(obstacles, segments);
        pathUI?.DisplayCourseSummary(summary, segments);
    }

    // =========================
    // BUILD SEGMENTS
    // =========================

    private PathSegmentData BuildTurnSegment(CourseObstacle from, CourseObstacle to)
    {
        PathSegmentData segment = new PathSegmentData
        {
            segmentName = $"{from.orderIndex} → {to.orderIndex}",
            fromObstacle = from,
            toObstacle = to,
            startPoint = from.LandingPoint,
            endPoint = to.ApproachPoint
        };

        Vector3 p0 = from.LandingPoint;
        Vector3 p3 = to.ApproachPoint;

        float dist = Vector3.Distance(p0, p3);
        float handle = dist * controlPointStrength;

        Vector3 p1 = p0 + from.transform.forward * handle;
        Vector3 p2 = p3 - to.transform.forward * handle;

        for (int i = 0; i <= curveResolution; i++)
        {
            float t = i / (float)curveResolution;
            Vector3 point = CubicBezier(p0, p1, p2, p3, t);
            segment.curvePoints.Add(AddHeight(point));
        }

        segment.distance = dist;

        Vector3 dir = (p3 - p0).normalized;
        segment.turnAngle = Vector3.Angle(from.transform.forward, dir);

        return segment;
    }

    private PathSegmentData BuildFreeSegment(
        string name,
        Vector3 start,
        Vector3 end,
        Vector3 startDir,
        Vector3 endDir)
    {
        PathSegmentData segment = new PathSegmentData
        {
            segmentName = name,
            startPoint = start,
            endPoint = end
        };

        Vector3 p0 = start;
        Vector3 p3 = end;

        float dist = Vector3.Distance(p0, p3);
        float handle = dist * controlPointStrength;

        Vector3 p1 = p0 + startDir.normalized * handle;
        Vector3 p2 = p3 - endDir.normalized * handle;

        for (int i = 0; i <= curveResolution; i++)
        {
            float t = i / (float)curveResolution;
            Vector3 point = CubicBezier(p0, p1, p2, p3, t);
            segment.curvePoints.Add(AddHeight(point));
        }

        segment.distance = dist;

        if (startDir != Vector3.zero)
        {
            Vector3 dir = (end - start).normalized;
            segment.turnAngle = Vector3.Angle(startDir, dir);
        }

        return segment;
    }

    private void DrawObstacleJumpLine(CourseObstacle obstacle)
    {
        List<Vector3> points = new()
        {
            AddHeight(obstacle.ApproachPoint),
            AddHeight(obstacle.JumpCenter),
            AddHeight(obstacle.LandingPoint)
        };

        GameObject obj = CreateLineObject($"Jump_{obstacle.orderIndex}");
        LineRenderer lr = SetupLineRenderer(obj, points.Count, Color.white);
        lr.SetPositions(points.ToArray());

        spawnedLines.Add(obj);
    }

    private void DrawSegment(PathSegmentData segment)
{
    GameObject obj = CreateLineObject($"Segment_{segment.segmentName}");

    Color color = segment.status switch
    {
        SegmentStatus.Safe => Color.green,
        SegmentStatus.Warning => Color.yellow,
        SegmentStatus.Invalid => Color.red,
        _ => Color.white
    };

    LineRenderer lr = SetupLineRenderer(obj, segment.curvePoints.Count, color);
    lr.SetPositions(segment.curvePoints.ToArray());

    segment.lineObject = obj;
    spawnedLines.Add(obj);
}

    // =========================
    // EVALUATION
    // =========================

    private void EvaluateSegment(PathSegmentData segment)
    {
        if (arenaBounds == null) return;

        float minWall = float.MaxValue;

        foreach (var p in segment.curvePoints)
        {
            Vector3 flat = new(p.x, 0f, p.z);

            if (!arenaBounds.IsInsideArena(flat))
            {
                segment.status = SegmentStatus.Invalid;
                segment.warnings.Add("Outside arena.");
            }

            float d = arenaBounds.DistanceToNearestWall(flat);
            minWall = Mathf.Min(minWall, d);
        }

        segment.wallClearance = minWall;

        if (segment.turnAngle > 60f)
        {
            segment.status = SegmentStatus.Invalid;
            segment.warnings.Add("Turn too sharp.");
        }
        else if (segment.turnAngle > 30f)
        {
            segment.status = SegmentStatus.Warning;
            segment.warnings.Add("Tight turn.");
        }

        if (minWall < 2f)
        {
            segment.status = SegmentStatus.Invalid;
            segment.warnings.Add("Too close to wall.");
        }
    }

    // =========================
    // COURSE SUMMARY
    // =========================

    private CourseSummaryData BuildCourseSummary(
        List<CourseObstacle> obstacles,
        List<PathSegmentData> segments)
    {
        CourseSummaryData s = new();

        s.height = courseSettings.height;
        s.speed = courseSettings.speed;

        s.obstacleCount = obstacles.Count;
        s.effortCount = obstacles.Count;

        foreach (var seg in segments)
        {
            s.courseLength += seg.distance;

            if (seg.status == SegmentStatus.Invalid)
                s.invalidSegments++;

            if (seg.status == SegmentStatus.Warning)
                s.warningSegments++;
        }

        s.timeAllowed = (s.courseLength / s.speed) * 60f;
        s.timeLimit = s.timeAllowed * 2f;

        if (s.invalidSegments > 0)
            s.status = CourseStatus.Invalid;
        else if (s.warningSegments > 0)
            s.status = CourseStatus.Risky;
        else
            s.status = CourseStatus.Safe;

        return s;
    }

    // =========================
    // DRAWING
    // =========================

    private GameObject CreateLineObject(string name)
    {
        GameObject obj = new(name);
        obj.transform.SetParent(lineParent);
        return obj;
    }

    private LineRenderer SetupLineRenderer(GameObject obj, int count, Color color)
    {
        LineRenderer lr = obj.AddComponent<LineRenderer>();

        lr.material = lineMaterial;
        lr.positionCount = count;
        lr.widthMultiplier = lineWidth;
        lr.useWorldSpace = true;

        lr.startColor = color;
        lr.endColor = color;

        return lr;
    }

    private Vector3 AddHeight(Vector3 p)
    {
        return new Vector3(p.x, p.y + lineHeightOffset, p.z);
    }

    private Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;

        return
            u * u * u * p0 +
            3f * u * u * t * p1 +
            3f * u * t * t * p2 +
            t * t * t * p3;
    }

    private void ClearLines()
    {
        foreach (var obj in spawnedLines)
        {
            if (obj != null)
                Destroy(obj);
        }

        spawnedLines.Clear();
    }
}