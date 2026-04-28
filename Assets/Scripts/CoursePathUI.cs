using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

public class CoursePathUI : MonoBehaviour
{
    public TextMeshProUGUI overallText;
    public TextMeshProUGUI detailsText;

    public void DisplayCourseSummary(CourseSummaryData summary, List<PathSegmentData> segments)
    {
        if (summary == null)
        {
            overallText.text = "No course data.";
            detailsText.text = "";
            return;
        }

        overallText.text =
            $"COURSE INFORMATION\n" +
            $"Height: {summary.height:F2} m\n" +
            $"Speed: {summary.speed:F0} m/min\n" +
            $"Course Length: {summary.courseLength:F1} m\n" +
            $"Obstacles: {summary.obstacleCount}\n" +
            $"Efforts: {summary.effortCount}\n" +
            $"Time Allowed: {summary.timeAllowed:F0} sec\n" +
            $"Time Limit: {summary.timeLimit:F0} sec\n\n" +
            $"COURSE STATUS\n" +
            $"Status: {summary.status}\n" +
            $"Invalid Segments: {summary.invalidSegments}\n" +
            $"Warnings: {summary.warningSegments}";

        StringBuilder sb = new();

        sb.AppendLine("SEGMENT DETAILS");
        sb.AppendLine("----------------");

        foreach (PathSegmentData segment in segments)
        {
            sb.AppendLine($"Segment {segment.segmentName}");
            sb.AppendLine($"Distance: {segment.distance:F1} m");
            sb.AppendLine($"Turn Angle: {segment.turnAngle:F1}°");
            sb.AppendLine($"Wall Clearance: {segment.wallClearance:F1} m");
            sb.AppendLine($"Status: {segment.status}");

            foreach (string warning in segment.warnings)
            {
                sb.AppendLine($"- {warning}");
            }

            sb.AppendLine();
        }

        detailsText.text = sb.ToString();
    }
}