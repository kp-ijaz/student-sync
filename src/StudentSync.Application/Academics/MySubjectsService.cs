using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Application.Scheduling;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Academics;

/// <summary>Progress of one syllabus chapter, from the statuses of its visible classes. Cancelled classes are ignored.</summary>
public static class SyllabusChapterStatus
{
    public const string Completed = "completed";
    public const string InProgress = "inProgress";
    public const string Upcoming = "upcoming";
    public const string NotScheduled = "notScheduled";

    public static string From(IEnumerable<SessionStatus> statuses)
    {
        int open = 0, completed = 0;
        foreach (var s in statuses)
        {
            if (s == SessionStatus.Completed) completed++;
            else if (s is SessionStatus.Scheduled or SessionStatus.Live) open++;
        }
        return From(open, completed);
    }

    /// <param name="open">Scheduled or live classes.</param>
    /// <param name="completed">Completed classes.</param>
    public static string From(int open, int completed) =>
        (open, completed) switch
        {
            (0, 0) => NotScheduled,
            (0, _) => Completed,
            (_, 0) => Upcoming,
            _ => InProgress,
        };
}

/// <summary>
/// The caller's subjects organised as subject → syllabus chapter → classes and videos.
/// Which subjects: students get their curriculum (<see cref="StudentCurriculum"/>: core subjects of their courses, chosen
/// languages, assigned additional subjects), including subjects without any class yet; teachers get the subjects they are
/// assigned to (<c>TeacherSubject</c>) plus subjects of classes they teach; admins get every subject with classes.
/// Counts, videos and the next class are always derived from <see cref="AccessPolicy.VisibleSessionsAsync"/> only.
/// </summary>
public sealed class MySubjectsService(IAppDbContext db, AccessPolicy access, IClock clock, PhotoUrls photos, VideoService videos, ContentService content, StudentCurriculumService curriculum)
{
    public const string OtherClassesTitle = "Other classes";

    /// <summary>Safety bound on the classes loaded for one subject's syllabus.</summary>
    public const int MaxSessionsPerSubject = 1000;

    private sealed record ChapterInfo(Guid Id, string Title, int SortOrder);
    private sealed record SubjectInfo(Guid Id, string Name, string Code, string? Description, string CourseName, List<ChapterInfo> Chapters);
    private sealed record Membership(string Kind, string? Group);

    public async Task<IReadOnlyList<MySubjectDto>> ListAsync(CancellationToken ct)
    {
        EnsureAllowed();
        var visible = (await access.VisibleSessionsAsync(ct)).Where(s => s.SubjectId != null);
        var members = await MembershipAsync(visible, null, ct);
        if (members.Count == 0) return [];
        var memberIds = members.Keys.ToList();
        // Only classes in the caller's own subjects count towards them.
        var sessions = visible.Where(s => memberIds.Contains(s.SubjectId!.Value));

        // One aggregate over the visible classes instead of loading them all.
        var groups = await sessions
            .GroupBy(s => new { s.SubjectId, s.ChapterId, s.TeacherId, s.Status })
            .Select(g => new { g.Key.SubjectId, g.Key.ChapterId, g.Key.TeacherId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var subjects = await LoadSubjectsAsync(memberIds, ct);
        var withClasses = groups.Select(g => g.SubjectId!.Value).ToHashSet();
        var assignedTeachers = await AssignedTeachersAsync(memberIds.Where(id => !withClasses.Contains(id)).ToList(), ct);
        var teachers = await TeachersAsync(groups.Select(g => g.TeacherId).Concat(assignedTeachers.SelectMany(t => t.Value)), ct);
        var videoRows = (await videos.ForSubjectsAsync(null, ct)).Where(v => members.ContainsKey(v.SubjectId)).ToList();

        var now = clock.UtcNow;
        var nextIds = await Upcoming(sessions, now)
            .GroupBy(s => s.SubjectId)
            .Select(g => new { SubjectId = g.Key, Id = g.OrderBy(s => s.StartUtc).ThenBy(s => s.Id).Select(s => s.Id).First() })
            .ToListAsync(ct);
        var ids = nextIds.Select(x => x.Id).ToList();
        var nextDtos = ids.Count == 0 ? [] : await photos.WithTeacherPhotosAsync(
            await SchedulingService.Project(db.Sessions.Where(s => ids.Contains(s.Id))).ToListAsync(ct), ct);
        var nextBySubject = nextIds.Join(nextDtos, x => x.Id, d => d.Id, (x, d) => (SubjectId: x.SubjectId!.Value, Dto: d))
            .ToDictionary(x => x.SubjectId, x => x.Dto);

        var result = new List<MySubjectDto>();
        foreach (var subject in subjects)
        {
            var mine = groups.Where(g => g.SubjectId == subject.Id).ToList();
            var chapters = subject.Chapters.Select(c =>
            {
                var rows = mine.Where(g => g.ChapterId == c.Id).ToList();
                return SyllabusChapterStatus.From(
                    rows.Where(g => g.Status is SessionStatus.Scheduled or SessionStatus.Live).Sum(g => g.Count),
                    rows.Where(g => g.Status == SessionStatus.Completed).Sum(g => g.Count));
            }).ToList();
            var subjectVideos = videoRows.Where(v => v.SubjectId == subject.Id).ToList();
            var member = members[subject.Id];
            result.Add(new MySubjectDto(subject.Id, subject.Name, subject.Code, subject.CourseName,
                TeachersOf(mine.Count > 0 ? mine.Select(g => g.TeacherId) : assignedTeachers.GetValueOrDefault(subject.Id, []), teachers),
                subject.Chapters.Count, chapters.Count(s => s == SyllabusChapterStatus.Completed),
                mine.Where(g => g.Status != SessionStatus.Cancelled).Sum(g => g.Count),
                mine.Where(g => g.Status == SessionStatus.Completed).Sum(g => g.Count),
                subjectVideos.Count, subjectVideos.Count(v => v.Video.IsUnlocked),
                nextBySubject.GetValueOrDefault(subject.Id), member.Kind, member.Group));
        }

        // Predictable order: core subjects, then languages, then additional subjects; each by name.
        return result
            .OrderBy(s => StudentCurriculum.SortRank(s.Kind))
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Code, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SubjectSyllabusDto> GetAsync(Guid subjectId, CancellationToken ct)
    {
        EnsureAllowed();
        var all = await access.VisibleSessionsAsync(ct);
        // Not one of the caller's subjects: answer exactly as for a subject that does not exist.
        var members = await MembershipAsync(all.Where(s => s.SubjectId != null), subjectId, ct);
        if (!members.TryGetValue(subjectId, out var member)) throw new NotFoundException("Subject");
        var subject = (await LoadSubjectsAsync([subjectId], ct)).SingleOrDefault() ?? throw new NotFoundException("Subject");

        var visible = all.Where(s => s.SubjectId == subjectId).OrderBy(s => s.StartUtc).ThenBy(s => s.Id).Take(MaxSessionsPerSubject);
        var chapterOf = await visible.Select(s => new { s.Id, s.ChapterId }).ToDictionaryAsync(s => s.Id, s => s.ChapterId, ct);
        var sessions = chapterOf.Count == 0 ? [] : await photos.WithTeacherPhotosAsync(await SchedulingService.Project(visible).ToListAsync(ct), ct);
        var order = sessions.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var teacherIds = sessions.Count > 0 ? sessions.Select(s => s.TeacherId).ToList()
            : (await AssignedTeachersAsync([subjectId], ct)).GetValueOrDefault(subjectId, []);
        var teachers = await TeachersAsync(teacherIds, ct);
        var videoRows = sessions.Count == 0 ? [] : (await videos.ForSubjectsAsync(subjectId, ct)).Where(v => order.ContainsKey(v.Video.SessionId)).ToList();
        var recordings = sessions.Count == 0 ? [] : (await content.SubjectRecordingsAsync(subjectId, ct)).Where(r => order.ContainsKey(r.SessionId))
            .OrderBy(r => order[r.SessionId]).ThenBy(r => r.CreatedAtUtc).ToList();

        // Classes whose chapter is not part of this subject's syllabus are grouped with the unassigned ones.
        var known = subject.Chapters.Select(c => c.Id).ToHashSet();
        Guid? Key(Guid sessionId) => chapterOf.GetValueOrDefault(sessionId) is { } c && known.Contains(c) ? c : null;

        SyllabusChapterDto Chapter(Guid? id, string title, int sortOrder)
        {
            var chapterSessions = sessions.Where(s => Key(s.Id) == id).ToList();
            return new SyllabusChapterDto(id, title, sortOrder, SyllabusChapterStatus.From(chapterSessions.Select(s => s.Status)), chapterSessions,
                videoRows.Where(v => Key(v.Video.SessionId) == id).Select(v => v.Video).ToList(),
                recordings.Where(r => Key(r.SessionId) == id).ToList());
        }

        var chapters = subject.Chapters.Select(c => Chapter(c.Id, c.Title, c.SortOrder)).ToList();
        if (sessions.Any(s => Key(s.Id) is null)) chapters.Add(Chapter(null, OtherClassesTitle, int.MaxValue));

        var now = clock.UtcNow;
        var next = sessions.Where(s => s.Status is SessionStatus.Scheduled or SessionStatus.Live && s.EndUtc > now)
            .OrderBy(s => s.StartUtc).ThenBy(s => s.Id).FirstOrDefault();
        return new SubjectSyllabusDto(subject.Id, subject.Name, subject.Code, subject.CourseName, subject.Description,
            TeachersOf(teacherIds, teachers),
            subject.Chapters.Count, chapters.Count(c => c.ChapterId is not null && c.Status == SyllabusChapterStatus.Completed),
            sessions.Count(s => s.Status != SessionStatus.Cancelled), sessions.Count(s => s.Status == SessionStatus.Completed),
            videoRows.Count, videoRows.Count(v => v.Video.IsUnlocked), next, chapters, member.Kind, member.Group);
    }

    /// <summary>
    /// The caller's subjects (optionally just <paramref name="only"/>) with how each reaches them. Students: their curriculum.
    /// Teachers: active subjects they are assigned to plus subjects of their visible classes. Admins: subjects of visible classes.
    /// </summary>
    private async Task<Dictionary<Guid, Membership>> MembershipAsync(IQueryable<LearningSession> visible, Guid? only, CancellationToken ct)
    {
        if (access.IsStudent)
        {
            var kinds = await curriculum.KindsAsync(access.UserId, ct);
            return kinds.Where(k => only is null || k.Key == only)
                .ToDictionary(k => k.Key, k => new Membership(k.Value.Kind, k.Value.Group));
        }

        var ids = visible.Where(s => s.SubjectId != null).Select(s => s.SubjectId!.Value);
        if (access.IsTeacher)
            ids = ids.Union(db.TeacherSubjects.Where(t => t.TeacherId == access.UserId && t.Subject!.Status == CatalogStatus.Active).Select(t => t.SubjectId));
        if (only is { } one) ids = ids.Where(id => id == one);
        var rows = await db.Subjects.Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Kind, s.ChoiceGroup }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new Membership(StudentCurriculum.KindFor(r.Kind), r.ChoiceGroup));
    }

    /// <summary>Teachers assigned to each subject; shown for subjects that have no visible class yet.</summary>
    private async Task<Dictionary<Guid, List<Guid>>> AssignedTeachersAsync(List<Guid> subjectIds, CancellationToken ct)
    {
        if (subjectIds.Count == 0) return [];
        var rows = await db.TeacherSubjects.Where(t => subjectIds.Contains(t.SubjectId)).Select(t => new { t.SubjectId, t.TeacherId }).ToListAsync(ct);
        return rows.GroupBy(r => r.SubjectId).ToDictionary(g => g.Key, g => g.Select(r => r.TeacherId).ToList());
    }

    /// <summary>Institution users only; parents (who cannot sign in) and platform staff are refused.</summary>
    private void EnsureAllowed()
    {
        if (!(access.IsAdmin || access.IsTeacher || access.IsStudent)) throw new ForbiddenException();
    }

    private static IQueryable<LearningSession> Upcoming(IQueryable<LearningSession> sessions, DateTime now) =>
        sessions.Where(s => (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.Live) && s.EndUtc > now);

    private async Task<List<SubjectInfo>> LoadSubjectsAsync(List<Guid> ids, CancellationToken ct)
    {
        var rows = await db.Subjects.Where(s => ids.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Code,
                s.Description,
                CourseName = s.Course!.Name,
                Chapters = s.Chapters.Select(c => new { c.Id, c.Title, c.SortOrder }).ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(s => new SubjectInfo(s.Id, s.Name, s.Code, s.Description, s.CourseName,
            s.Chapters.OrderBy(c => c.SortOrder).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new ChapterInfo(c.Id, c.Title, c.SortOrder)).ToList())).ToList();
    }

    private async Task<Dictionary<Guid, SubjectTeacherDto>> TeachersAsync(IEnumerable<Guid> teacherIds, CancellationToken ct)
    {
        var ids = teacherIds.Distinct().ToList();
        var rows = await db.Teachers.Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.User!.FullName, t.User.PhotoFileId }).ToListAsync(ct);
        // Staff photos may be shown to anyone in the institution (same rule as the session list).
        return rows.ToDictionary(t => t.Id, t => new SubjectTeacherDto(t.Id, t.FullName, photos.For(t.PhotoFileId)));
    }

    private static List<SubjectTeacherDto> TeachersOf(IEnumerable<Guid> ids, Dictionary<Guid, SubjectTeacherDto> teachers) =>
        ids.Distinct().Select(teachers.GetValueOrDefault).OfType<SubjectTeacherDto>()
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}
