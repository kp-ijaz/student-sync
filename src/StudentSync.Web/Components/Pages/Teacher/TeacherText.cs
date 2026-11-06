using StudentSync.Contracts;
using StudentSync.Web.Services;

namespace StudentSync.Web.Components.Pages.Teacher;

/// <summary>Row subtitles shared by the teacher dashboard and Tasks (mirrors the mobile DoubtCard and grading rows).</summary>
internal static class TeacherText
{
    /// <summary>"Student · Subject · Teacher: Name · 2 replies · discussed in class on …".</summary>
    public static string DoubtLine(DoubtListItemDto d, string tz)
    {
        var parts = new List<string> { d.StudentName };
        if (d.SubjectName is not null) parts.Add(d.SubjectName);
        if (d.AssignedTeacherName is not null) parts.Add($"Teacher: {d.AssignedTeacherName}");
        parts.Add(d.ReplyCount == 1 ? "1 reply" : $"{d.ReplyCount} replies");
        if (d.FollowUpSessionStartUtc is { } at) parts.Add($"to be discussed in class on {Time.Short(at, tz)}");
        return string.Join(" · ", parts);
    }

    public static string SubmissionLine(SubmissionToGradeDto s, string tz) =>
        s.SubmittedAtUtc is { } at ? $"{s.StudentName} · submitted {Time.Short(at, tz)}" : s.StudentName;
}
