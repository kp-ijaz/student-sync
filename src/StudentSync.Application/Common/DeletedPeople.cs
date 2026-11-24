namespace StudentSync.Application.Common;

/// <summary>
/// Owner decision 2026-11-24: a deleted person is gone from every working screen. Where a past class or piece of work of
/// a current student was led by a teacher who has since been deleted, the row stays (it is the student's history) but the
/// teacher is shown by this label instead of their name and photo.
/// </summary>
public static class DeletedPeople
{
    public const string TeacherName = "Former teacher";
}
