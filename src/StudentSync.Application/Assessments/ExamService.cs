using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.People;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Assessments;

public sealed class ExamService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, NotificationService notify, Content.FileService files)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------- Question bank ----------------

    private IQueryable<Question> ManageableQuestions()
    {
        access.Require(Permissions.ExamsManage);
        var q = db.Questions.Where(x => !x.IsArchived);
        return access.IsAdmin ? q : q.Where(x => db.TeacherSubjects.Any(t => t.SubjectId == x.SubjectId && t.TeacherId == user.UserId));
    }

    public async Task<PagedResult<QuestionDto>> QuestionsAsync(QuestionQuery q, CancellationToken ct)
    {
        var items = ManageableQuestions();
        if (q.SubjectId is { } s) items = items.Where(x => x.SubjectId == s);
        if (q.Type is { } t) items = items.Where(x => x.Type == t);
        if (q.Difficulty is { } d) items = items.Where(x => x.Difficulty == d);
        if (!string.IsNullOrWhiteSpace(q.Search)) items = items.Where(x => x.Stem.Contains(q.Search));
        var total = await items.CountAsync(ct);
        var page = await items.OrderByDescending(x => x.CreatedAtUtc).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize)
            .Select(x => new QuestionDto(x.Id, x.SubjectId, db.Subjects.Where(s => s.Id == x.SubjectId).Select(s => s.Name).First(), x.ChapterId, x.Type, x.Difficulty,
                x.Stem, x.Explanation, x.DefaultMarks, x.AcceptedAnswers, x.NumericAnswer, x.NumericTolerance,
                x.Options.OrderBy(o => o.SortOrder).Select(o => new QuestionOptionDto(o.Id, o.Text, o.IsCorrect, o.MatchText, o.SortOrder)).ToList(),
                x.ImageFileId, db.Chapters.Where(c => c.Id == x.ChapterId).Select(c => c.Title).FirstOrDefault()))
            .ToListAsync(ct);
        return new PagedResult<QuestionDto>(page, q.SafePage, q.SafePageSize, total);
    }

    public async Task<Guid> SaveQuestionAsync(Guid? id, SaveQuestionRequest r, CancellationToken ct)
    {
        access.Require(Permissions.ExamsManage);
        if (!await db.Subjects.AnyAsync(s => s.Id == r.SubjectId, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
        if (!access.IsAdmin && !await db.TeacherSubjects.AnyAsync(t => t.SubjectId == r.SubjectId && t.TeacherId == user.UserId, ct))
            throw new ForbiddenException("You are not assigned to this subject.");
        if (r.ChapterId is { } c && !await db.Chapters.AnyAsync(x => x.Id == c && x.SubjectId == r.SubjectId, ct)) throw RequestValidationException.For("chapterId", "Chapter not found.");
        if (r.Options.Count > 12) throw RequestValidationException.For("options", "At most 12 options.");

        Question q;
        if (id is { } existingId)
        {
            q = await ManageableQuestions().Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new NotFoundException("Question");
            if (await db.ExamQuestions.AnyAsync(eq => eq.QuestionId == q.Id && db.ExamAttempts.Any(a => a.ExamId == eq.ExamId), ct))
                throw new DomainException("This question has been answered in an exam. Create a new question instead of changing it.", "question_in_use");
            db.QuestionOptions.RemoveRange(q.Options);
            q.Options.Clear();
        }
        else db.Questions.Add(q = new Question());

        q.SubjectId = r.SubjectId; q.ChapterId = r.ChapterId; q.Type = r.Type; q.Difficulty = r.Difficulty;
        q.Stem = Text.Required(r.Stem, "stem", 10_000); q.Explanation = Text.Optional(r.Explanation, "explanation", 10_000);
        q.DefaultMarks = r.DefaultMarks; q.AcceptedAnswers = Text.Optional(r.AcceptedAnswers, "acceptedAnswers", 1000);
        q.NumericAnswer = r.NumericAnswer; q.NumericTolerance = Math.Max(0, r.NumericTolerance);
        // A new image must have been uploaded by the caller (purpose QuestionImage); keeping or removing an existing one is free.
        if (r.ImageFileId != q.ImageFileId && r.ImageFileId is { } img) await files.ClaimAsync([img], FilePurpose.QuestionImage, ct);
        q.ImageFileId = r.ImageFileId;
        var order = 0;
        foreach (var o in r.Type is QuestionType.ShortAnswer or QuestionType.LongAnswer or QuestionType.FillBlank or QuestionType.Numerical ? [] : r.Options)
            q.Options.Add(new QuestionOption { Text = Text.Required(o.Text, "options", 1000), IsCorrect = o.IsCorrect, MatchText = Text.Optional(o.MatchText, "matchText", 1000), SortOrder = order++ });
        try { q.Validate(); }
        catch (DomainException ex) { throw RequestValidationException.For("question", ex.Message); }
        audit.Record(id is null ? "question.created" : "question.updated", "Question", q.Id, null);
        await db.SaveChangesAsync(ct);
        return q.Id;
    }

    public async Task ArchiveQuestionAsync(Guid id, CancellationToken ct)
    {
        var q = await ManageableQuestions().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Question");
        q.IsArchived = true;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Exams ----------------

    private async Task<IQueryable<Exam>> VisibleExamsAsync(CancellationToken ct)
    {
        if (access.IsAdmin) return db.Exams;
        if (access.IsTeacher) return db.Exams.Where(e => e.TeacherId == user.UserId);
        var students = access.IsParent ? await access.ChildIdsAsync(ct) : access.IsStudent ? [user.UserId] : [];
        return db.Exams.Where(e => e.Status != ExamStatus.Draft && e.Recipients.Any(r => students.Contains(r.StudentId)));
    }

    public async Task<IReadOnlyList<ExamListItemDto>> ListAsync(Guid? studentId, ExamPurpose? purpose, CancellationToken ct)
    {
        var exams = await VisibleExamsAsync(ct);
        if (purpose is { } p) exams = exams.Where(e => e.Purpose == p);
        var target = access.IsStudent ? user.UserId : studentId;
        if (studentId is { } sid) { await access.EnsureCanViewStudentAsync(sid, ct); exams = exams.Where(e => e.Recipients.Any(r => r.StudentId == sid)); }
        return await exams.OrderByDescending(e => e.OpensAtUtc).Take(200).Select(e => new ExamListItemDto(
            e.Id, e.Title, e.Status, e.OpensAtUtc, e.ClosesAtUtc, e.DurationMinutes, e.Questions.Count, e.Questions.Sum(q => q.Marks),
            db.Subjects.Where(s => s.Id == e.SubjectId).Select(s => s.Name).First(), e.ResultsReleased,
            db.ExamAttempts.Count(a => a.ExamId == e.Id && a.StudentId == target), e.MaxAttempts,
            e.ResultsReleased ? db.ExamAttempts.Where(a => a.ExamId == e.Id && a.StudentId == target && a.Status == AttemptStatus.Graded).Max(a => a.Score) : null,
            db.ExamAttempts.Any(a => a.ExamId == e.Id && a.StudentId == target && a.Status == AttemptStatus.InProgress),
            db.ExamAttempts.Where(a => a.ExamId == e.Id && a.StudentId == target).OrderByDescending(a => a.AttemptNumber).Select(a => (Guid?)a.Id).FirstOrDefault(), e.Purpose)).ToListAsync(ct);
    }

    private async Task<Exam> GetManageableExamAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.ExamsManage);
        var e = await db.Exams.Include(x => x.Questions).Include(x => x.Recipients).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exam");
        if (!access.IsAdmin && e.TeacherId != user.UserId) throw new NotFoundException("Exam");
        return e;
    }

    public async Task<ExamDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await GetManageableExamAsync(id, ct);
        var questions = await db.ExamQuestions.Where(x => x.ExamId == id).OrderBy(x => x.SortOrder)
            .Select(x => new ExamQuestionItemDto(x.Id, x.QuestionId, x.Question!.Type, x.Question.Stem, x.Marks, x.SortOrder, x.Question.ImageFileId,
                x.Question.Options.OrderBy(o => o.SortOrder).Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList(),
                x.Question.Type == QuestionType.Matching ? x.Question.Options.Select(o => o.MatchText!).OrderBy(m => m).ToList() : null,
                db.Subjects.Where(s => s.Id == x.Question.SubjectId).Select(s => s.Name).FirstOrDefault(),
                db.Chapters.Where(c => c.Id == x.Question.ChapterId).Select(c => c.Title).FirstOrDefault())).ToListAsync(ct);
        var recipients = await db.ExamRecipients.Where(x => x.ExamId == id)
            .Select(x => new LookupItem(x.StudentId, db.Users.Where(u => u.Id == x.StudentId).Select(u => u.FullName).First(), null)).ToListAsync(ct);
        return new ExamDetailDto(e.Id, e.Title, e.Instructions, e.Status, e.SubjectId, e.SessionId, e.BatchId, e.OpensAtUtc, e.ClosesAtUtc, e.DurationMinutes,
            e.MaxAttempts, e.ShuffleQuestions, e.ResultsReleased, questions, recipients, questions.Sum(q => q.Marks), e.Purpose, e.GradeLevelId, e.CurriculumId, e.TeacherId);
    }

    public async Task<Guid> SaveAsync(Guid? id, SaveExamRequest r, CancellationToken ct)
    {
        access.Require(Permissions.ExamsManage);
        Exam e;
        if (id is { } eid)
        {
            e = await GetManageableExamAsync(eid, ct);
            if (e.Status != ExamStatus.Draft) throw new DomainException("Published exams cannot be edited.", "exam_published");
        }
        else db.Exams.Add(e = new Exam { TeacherId = user.UserId });

        if (!await db.Subjects.AnyAsync(s => s.Id == r.SubjectId, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
        if (r.SessionId is { } sid) await access.GetManageableSessionAsync(sid, ct);
        if (r.BatchId is { } bid && !await db.Batches.AnyAsync(b => b.Id == bid && (access.IsAdmin || b.TeacherId == user.UserId), ct)) throw RequestValidationException.For("batchId", "Batch not found.");
        if (r.MaxAttempts is < 1 or > 10) throw RequestValidationException.For("maxAttempts", "Attempts must be between 1 and 10.");
        if (r.GradeLevelId is { } gl && !await db.GradeLevels.AnyAsync(g => g.Id == gl, ct)) throw RequestValidationException.For("gradeLevelId", "Class not found.");
        if (r.CurriculumId is { } cu && !await db.Curricula.AnyAsync(c => c.Id == cu, ct)) throw RequestValidationException.For("curriculumId", "Syllabus not found.");
        if (access.IsAdmin && id is null)
        {
            // An admin names the teacher who runs the assessment, or inherits it from the linked session/batch.
            if (r.TeacherId is { } tid)
            {
                if (!await db.Teachers.AnyAsync(t => t.Id == tid && t.Status == PersonStatus.Active, ct)) throw RequestValidationException.For("teacherId", "Teacher not found.");
                e.TeacherId = tid;
            }
            else e.TeacherId = r.SessionId is { } s2 ? (await db.Sessions.FirstAsync(x => x.Id == s2, ct)).TeacherId
                : r.BatchId is { } b2 ? (await db.Batches.FirstAsync(x => x.Id == b2, ct)).TeacherId
                : throw RequestValidationException.For("teacherId", "Choose the teacher who runs this assessment, or link it to a session or batch.");
        }

        e.Title = Text.Required(r.Title, "title", 200);
        e.Instructions = Text.Optional(r.Instructions, "instructions", 10_000);
        e.SubjectId = r.SubjectId; e.SessionId = r.SessionId; e.BatchId = r.BatchId;
        e.OpensAtUtc = DateTime.SpecifyKind(r.OpensAtUtc, DateTimeKind.Utc);
        e.ClosesAtUtc = DateTime.SpecifyKind(r.ClosesAtUtc, DateTimeKind.Utc);
        e.DurationMinutes = r.DurationMinutes; e.MaxAttempts = r.MaxAttempts; e.ShuffleQuestions = r.ShuffleQuestions;
        e.Purpose = r.Purpose; e.GradeLevelId = r.GradeLevelId; e.CurriculumId = r.CurriculumId;

        var questionIds = r.Questions.Select(q => q.QuestionId).Distinct().ToList();
        if (questionIds.Count != r.Questions.Count) throw RequestValidationException.For("questions", "Each question can be used once.");
        var bank = await db.Questions.Where(q => questionIds.Contains(q.Id) && !q.IsArchived).ToDictionaryAsync(q => q.Id, ct);
        if (bank.Count != questionIds.Count) throw RequestValidationException.For("questions", "One or more questions were not found.");
        db.ExamQuestions.RemoveRange(e.Questions);
        e.Questions.Clear();
        var order = 0;
        foreach (var sel in r.Questions)
        {
            var marks = sel.Marks ?? bank[sel.QuestionId].DefaultMarks;
            if (marks is <= 0 or > 100) throw RequestValidationException.For("questions", "Marks per question must be between 0 and 100.");
            e.Questions.Add(new ExamQuestion { QuestionId = sel.QuestionId, Marks = marks, SortOrder = order++ });
        }

        var students = r.StudentIds is { Count: > 0 } ? r.StudentIds.Distinct().ToList()
            : r.SessionId is { } s3 ? await db.SessionParticipants.Where(p => p.SessionId == s3 && p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToListAsync(ct)
            : r.BatchId is { } b3 ? await db.BatchEnrolments.Where(x => x.BatchId == b3 && x.Status == EnrolmentStatus.Active).Select(x => x.StudentId).ToListAsync(ct)
            : [];
        if (await db.Students.CountAsync(s => students.Contains(s.Id), ct) != students.Count) throw RequestValidationException.For("studentIds", "One or more students were not found.");
        if (!access.IsAdmin && await access.StudentsTaughtBy(e.TeacherId).CountAsync(x => students.Contains(x), ct) != students.Count)
            throw RequestValidationException.For("studentIds", "You can only set exams for students you teach.");
        db.ExamRecipients.RemoveRange(e.Recipients);
        e.Recipients.Clear();
        foreach (var s in students) e.Recipients.Add(new ExamRecipient { StudentId = s });

        audit.Record(id is null ? "exam.created" : "exam.updated", "Exam", e.Id, e.Title);
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task PublishAsync(Guid id, CancellationToken ct)
    {
        var e = await GetManageableExamAsync(id, ct);
        if (e.Status != ExamStatus.Draft) throw new DomainException("Only draft exams can be published.", "exam_published");
        e.ValidateForPublish();
        e.Status = ExamStatus.Published;
        audit.Record("exam.published", "Exam", e.Id, e.Title);
        await notify.NotifyAsync(await notify.WithGuardiansAsync(e.Recipients.Select(r => r.StudentId), ct), NotificationType.ExamPublished,
            $"Exam scheduled: {e.Title}", $"Opens {e.OpensAtUtc:u}, {e.DurationMinutes} minutes.", "/exams", $"exam-published:{e.Id}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseResultsAsync(Guid id, CancellationToken ct)
    {
        var e = await GetManageableExamAsync(id, ct);
        if (await db.ExamAttempts.AnyAsync(a => a.ExamId == id && a.Status == AttemptStatus.Submitted, ct))
            throw new DomainException("Grade all written answers before releasing results.", "manual_grading_pending");
        e.ResultsReleased = true;
        if (e.ClosesAtUtc <= clock.UtcNow) e.Status = ExamStatus.Closed;
        audit.Record("exam.results_released", "Exam", e.Id, e.Title);
        var students = await db.ExamAttempts.Where(a => a.ExamId == id).Select(a => a.StudentId).Distinct().ToListAsync(ct);
        await notify.NotifyAsync(await notify.WithGuardiansAsync(students, ct), NotificationType.ExamResultsReleased, $"Results available: {e.Title}", "View your result and feedback.", "/exams", $"exam-results:{e.Id}", ct);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Attempts ----------------

    public async Task<AttemptDto> StartAsync(Guid examId, CancellationToken ct)
    {
        access.Require(Permissions.ExamsTake);
        var now = clock.UtcNow;
        var e = await db.Exams.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == examId && x.Status == ExamStatus.Published && x.Recipients.Any(r => r.StudentId == user.UserId), ct)
                ?? throw new NotFoundException("Exam");
        var attempts = await db.ExamAttempts.Where(a => a.ExamId == e.Id && a.StudentId == user.UserId).ToListAsync(ct);
        var open = attempts.FirstOrDefault(a => a.Status == AttemptStatus.InProgress);
        if (open is not null && !open.IsExpired(now)) return await GetAttemptAsync(open.Id, ct); // idempotent resume
        if (open is not null) await FinaliseAsync(open, now, ct);

        if (now < e.OpensAtUtc) throw new DomainException("This exam has not opened yet.", "exam_not_open");
        if (now >= e.ClosesAtUtc) throw new DomainException("This exam has closed.", "exam_closed");
        if (attempts.Count >= e.MaxAttempts) throw new DomainException("You have used all your attempts.", "no_attempts_left");

        var order = e.Questions.OrderBy(q => q.SortOrder).Select(q => q.Id).ToList();
        if (e.ShuffleQuestions) order = order.OrderBy(_ => Random.Shared.Next()).ToList();
        var deadline = now.AddMinutes(e.DurationMinutes);
        var attempt = new ExamAttempt
        {
            ExamId = e.Id,
            StudentId = user.UserId,
            AttemptNumber = attempts.Count + 1,
            StartedAtUtc = now,
            DeadlineUtc = deadline < e.ClosesAtUtc ? deadline : e.ClosesAtUtc,
            QuestionOrder = string.Join(',', order),
        };
        db.ExamAttempts.Add(attempt);
        audit.Record("exam.attempt_started", "ExamAttempt", attempt.Id, $"{e.Title} #{attempt.AttemptNumber}");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new ConflictException("An attempt was already started. Reload to continue it.", "attempt_exists"); }
        return await GetAttemptAsync(attempt.Id, ct);
    }

    public async Task<AttemptDto> GetAttemptAsync(Guid attemptId, CancellationToken ct)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == attemptId && x.StudentId == user.UserId, ct)
                ?? throw new NotFoundException("Attempt");
        var order = a.QuestionOrder.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
        var questions = await db.ExamQuestions.Where(q => q.ExamId == a.ExamId).Include(q => q.Question!).ThenInclude(q => q.Options).ToListAsync(ct);
        var dtos = order.Select(id => questions.First(q => q.Id == id)).Select(eq =>
        {
            var q = eq.Question!;
            var saved = a.Answers.FirstOrDefault(x => x.ExamQuestionId == eq.Id);
            // Answer keys (IsCorrect, accepted answers, matches) are deliberately not included.
            var options = q.Type == QuestionType.Matching || q.Type is QuestionType.ShortAnswer or QuestionType.LongAnswer or QuestionType.FillBlank or QuestionType.Numerical
                ? (q.Type == QuestionType.Matching ? q.Options.OrderBy(o => o.SortOrder).Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList() : [])
                : q.Options.OrderBy(o => o.SortOrder).Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList();
            var matchChoices = q.Type == QuestionType.Matching ? q.Options.Select(o => o.MatchText!).OrderBy(x => x).ToList() : null;
            return new AttemptQuestionDto(eq.Id, q.Type, q.Stem, eq.Marks, options, matchChoices,
                saved is null ? null : JsonSerializer.Deserialize<ExamResponse>(saved.ResponseJson, Json), q.ImageFileId);
        }).ToList();
        return new AttemptDto(a.Id, a.ExamId, a.Exam!.Title, a.Status, a.StartedAtUtc, a.DeadlineUtc, a.AttemptNumber, dtos);
    }

    /// <summary>Autosave one answer. Rejected once the server-side deadline has passed.</summary>
    public async Task SaveAnswerAsync(Guid attemptId, SaveAnswerRequest r, CancellationToken ct)
    {
        var a = await db.ExamAttempts.FirstOrDefaultAsync(x => x.Id == attemptId && x.StudentId == user.UserId, ct) ?? throw new NotFoundException("Attempt");
        if (a.Status != AttemptStatus.InProgress) throw new DomainException("This attempt has been submitted.", "attempt_submitted");
        if (a.IsExpired(clock.UtcNow)) throw new DomainException("Time is up for this attempt.", "attempt_expired");
        if (!a.QuestionOrder.Contains(r.ExamQuestionId.ToString())) throw new NotFoundException("Question");
        var json = JsonSerializer.Serialize(r.Response, Json);
        if (json.Length > 60_000) throw RequestValidationException.For("response", "The answer is too long.");
        var ans = await db.ExamAnswers.FirstOrDefaultAsync(x => x.AttemptId == a.Id && x.ExamQuestionId == r.ExamQuestionId, ct);
        if (ans is null) db.ExamAnswers.Add(new ExamAnswer { AttemptId = a.Id, ExamQuestionId = r.ExamQuestionId, ResponseJson = json });
        else ans.ResponseJson = json;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Idempotent: submitting an already-submitted attempt returns the existing result.</summary>
    public async Task<AttemptResultDto> SubmitAsync(Guid attemptId, CancellationToken ct)
    {
        var a = await db.ExamAttempts.Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == attemptId && x.StudentId == user.UserId, ct) ?? throw new NotFoundException("Attempt");
        if (a.Status == AttemptStatus.InProgress)
        {
            await FinaliseAsync(a, clock.UtcNow, ct);
            audit.Record("exam.attempt_submitted", "ExamAttempt", a.Id, null);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* submitted concurrently by another request; fall through to the stored result */ }
        }
        return await ResultAsync(a.Id, ct);
    }

    private async Task FinaliseAsync(ExamAttempt a, DateTime now, CancellationToken ct)
    {
        var questions = await db.ExamQuestions.Where(q => q.ExamId == a.ExamId).Include(q => q.Question!).ThenInclude(q => q.Options).ToListAsync(ct);
        var answers = await db.ExamAnswers.Where(x => x.AttemptId == a.Id).ToListAsync(ct);
        var manual = false;
        var all = new List<ExamAnswer>(answers);
        foreach (var eq in questions.Where(q => a.QuestionOrder.Contains(q.Id.ToString())))
        {
            var ans = answers.FirstOrDefault(x => x.ExamQuestionId == eq.Id);
            if (ans is null)
            {
                db.ExamAnswers.Add(ans = new ExamAnswer { TenantId = a.TenantId, AttemptId = a.Id, ExamQuestionId = eq.Id, ResponseJson = "{}" });
                all.Add(ans);
            }
            var response = JsonSerializer.Deserialize<ExamResponse>(ans.ResponseJson, Json);
            var grade = QuestionGrader.Grade(eq.Question!, response, eq.Marks);
            if (grade.AutoGraded) { ans.IsCorrect = grade.IsCorrect; ans.AwardedMarks = grade.Awarded; }
            else if (string.IsNullOrWhiteSpace(response?.Text)) { ans.IsCorrect = false; ans.AwardedMarks = 0; }
            else manual = true;
        }
        a.SubmittedAtUtc = now > a.DeadlineUtc ? a.DeadlineUtc : now;
        a.RequiresManualGrading = manual;
        a.Status = manual ? AttemptStatus.Submitted : AttemptStatus.Graded;
        a.Score = manual ? null : all.Sum(x => x.AwardedMarks ?? 0);
    }

    public async Task<AttemptResultDto> ResultAsync(Guid attemptId, CancellationToken ct)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).Include(x => x.Student!.User).Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == attemptId, ct) ?? throw new NotFoundException("Attempt");
        var isStaff = access.IsAdmin || (access.IsTeacher && a.Exam!.TeacherId == user.UserId);
        var isOwner = a.StudentId == user.UserId;
        var isGuardian = access.IsParent && (await access.ChildIdsAsync(ct)).Contains(a.StudentId);
        if (!isStaff && !isOwner && !isGuardian) throw new NotFoundException("Attempt");
        var showDetail = isStaff || a.Exam!.ResultsReleased;
        var questions = await db.ExamQuestions.Where(q => q.ExamId == a.ExamId).Include(q => q.Question!).ThenInclude(q => q.Options).ToDictionaryAsync(q => q.Id, ct);
        var answers = !showDetail ? [] : a.Answers.Where(x => questions.ContainsKey(x.ExamQuestionId)).Select(x =>
        {
            var q = questions[x.ExamQuestionId].Question!;
            var correct = q.Type switch
            {
                QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.TrueFalse => string.Join(", ", q.Options.Where(o => o.IsCorrect).Select(o => o.Text)),
                QuestionType.Numerical => q.NumericAnswer?.ToString(),
                QuestionType.Matching => string.Join("; ", q.Options.Select(o => $"{o.Text} → {o.MatchText}")),
                _ => q.AcceptedAnswers,
            };
            var response = JsonSerializer.Deserialize<ExamResponse>(x.ResponseJson, Json);
            return new AnswerResultDto(x.ExamQuestionId, q.Stem, q.Type, response, x.IsCorrect, x.AwardedMarks,
                questions[x.ExamQuestionId].Marks, x.Feedback, q.Explanation, correct, q.ImageFileId, Describe(q, response));
        }).OrderBy(x => questions[x.ExamQuestionId].SortOrder).ToList();
        return new AttemptResultDto(a.Id, a.Exam!.Title, a.StudentId, a.Student!.User!.FullName, a.Status, a.SubmittedAtUtc,
            showDetail ? a.Score : null, questions.Values.Sum(q => q.Marks), a.RequiresManualGrading, a.Exam.ResultsReleased, answers,
            a.Exam.Purpose, a.EntryMode, a.ExamId);
    }

    public async Task<ExamResultsDto> ResultsAsync(Guid examId, CancellationToken ct)
    {
        var e = await GetManageableExamAsync(examId, ct);
        var attempts = await db.ExamAttempts.Where(a => a.ExamId == examId).OrderBy(a => a.Student!.User!.FullName).ThenBy(a => a.AttemptNumber)
            .Select(a => new AttemptSummaryDto(a.Id, a.StudentId, a.Student!.User!.FullName, a.AttemptNumber, a.Status, a.SubmittedAtUtc, a.Score, a.RequiresManualGrading, a.EntryMode))
            .ToListAsync(ct);
        var scored = attempts.Where(a => a.Score is not null).Select(a => a.Score!.Value).ToList();
        return new ExamResultsDto(e.Id, e.Title, e.TotalMarks, attempts, scored.Count == 0 ? null : Math.Round(scored.Average(), 2), scored.Count == 0 ? null : scored.Max(),
            attempts.Count(a => a.Status == AttemptStatus.Submitted));
    }

    public async Task GradeAnswerAsync(Guid attemptId, GradeAnswerRequest r, CancellationToken ct)
    {
        var a = await db.ExamAttempts.Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == attemptId, ct) ?? throw new NotFoundException("Attempt");
        await GetManageableExamAsync(a.ExamId, ct);
        if (a.Status == AttemptStatus.InProgress) throw new DomainException("The attempt has not been submitted yet.", "attempt_in_progress");
        var eq = await db.ExamQuestions.FirstOrDefaultAsync(q => q.Id == r.ExamQuestionId && q.ExamId == a.ExamId, ct) ?? throw new NotFoundException("Question");
        if (r.AwardedMarks < 0 || r.AwardedMarks > eq.Marks) throw RequestValidationException.For("awardedMarks", $"Marks must be between 0 and {eq.Marks}.");
        var ans = a.Answers.FirstOrDefault(x => x.ExamQuestionId == eq.Id) ?? throw new NotFoundException("Answer");
        var before = ans.AwardedMarks;
        ans.AwardedMarks = r.AwardedMarks;
        ans.IsCorrect = r.AwardedMarks == eq.Marks;
        ans.Feedback = Text.Optional(r.Feedback, "feedback", 5000);
        ans.GradedByUserId = user.UserId;
        if (a.Answers.All(x => x.AwardedMarks is not null))
        {
            a.Status = AttemptStatus.Graded;
            a.Score = a.Answers.Sum(x => x.AwardedMarks!.Value);
        }
        audit.Record("exam.answer_graded", "ExamAttempt", a.Id, $"{eq.Id}: {before?.ToString() ?? "none"} -> {r.AwardedMarks}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A student's response in words (chosen option texts, number, text, matches) for result views.</summary>
    private static string? Describe(Question q, ExamResponse? r) => r switch
    {
        null => null,
        { OptionIds: { Count: > 0 } ids } => string.Join(", ", q.Options.Where(o => ids.Contains(o.Id)).OrderBy(o => o.SortOrder).Select(o => o.Text)),
        { Number: { } n } => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        { Text: { Length: > 0 } t } => t,
        { Matches: { Count: > 0 } m } => string.Join("; ", q.Options.Where(o => m.ContainsKey(o.Id)).Select(o => $"{o.Text} → {m[o.Id]}")),
        _ => null,
    };

    // ---------------- Diagnostic assessments (addendum C) ----------------

    /// <summary>
    /// Staff enter a student's paper answers against the key. The attempt is created already submitted (no timer), graded like
    /// a digital one, and counts towards MaxAttempts, so a second entry for the same student is refused unless the exam allows
    /// more attempts. Send an Idempotency-Key to make a retried request return the same attempt.
    /// </summary>
    public async Task<AttemptResultDto> EnterPaperAttemptAsync(Guid examId, PaperAttemptRequest r, CancellationToken ct)
    {
        var e = await GetManageableExamAsync(examId, ct);
        if (e.Status == ExamStatus.Draft) throw new DomainException("Publish the assessment before entering answers.", "exam_draft");
        if (!await db.Students.AnyAsync(s => s.Id == r.StudentId, ct)) throw new NotFoundException("Student");
        // A teacher may enter answers for the assessment's own students, or for students they teach (who are then added as recipients).
        var recipient = e.Recipients.Any(x => x.StudentId == r.StudentId);
        if (!recipient && !access.IsAdmin && !await access.TeacherTeachesStudentAsync(e.TeacherId, r.StudentId, ct)) throw new NotFoundException("Student");
        if (!recipient) e.Recipients.Add(new ExamRecipient { StudentId = r.StudentId });

        var attempts = await db.ExamAttempts.Where(a => a.ExamId == e.Id && a.StudentId == r.StudentId).ToListAsync(ct);
        if (attempts.Count >= e.MaxAttempts) throw new DomainException("This student has already used every attempt for this assessment.", "no_attempts_left");
        var questionIds = e.Questions.Select(q => q.Id).ToHashSet();
        var unknown = r.Answers.Select(a => a.ExamQuestionId).FirstOrDefault(id => !questionIds.Contains(id));
        if (unknown != default) throw RequestValidationException.For("answers", "An answer refers to a question that is not in this assessment.");
        if (r.Answers.GroupBy(a => a.ExamQuestionId).Any(g => g.Count() > 1)) throw RequestValidationException.For("answers", "Each question can be answered once.");

        var now = clock.UtcNow;
        var attempt = new ExamAttempt
        {
            ExamId = e.Id,
            StudentId = r.StudentId,
            AttemptNumber = attempts.Count + 1,
            StartedAtUtc = now,
            DeadlineUtc = now,
            EntryMode = AttemptEntryMode.PaperEntered,
            EnteredByUserId = user.UserId,
            QuestionOrder = string.Join(',', e.Questions.OrderBy(q => q.SortOrder).Select(q => q.Id)),
        };
        foreach (var a in r.Answers)
        {
            var json = JsonSerializer.Serialize(a.Response ?? new ExamResponse(), Json);
            if (json.Length > 60_000) throw RequestValidationException.For("answers", "An answer is too long.");
            attempt.Answers.Add(new ExamAnswer { ExamQuestionId = a.ExamQuestionId, ResponseJson = json });
        }
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(ct); // answers need ids before grading fills the gaps
        await FinaliseAsync(attempt, now, ct);
        audit.Record("exam.paper_attempt_entered", "ExamAttempt", attempt.Id, $"{e.Title} for student {r.StudentId}");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new ConflictException("An attempt for this student was recorded at the same time. Reload and check.", "attempt_exists"); }
        return await ResultAsync(attempt.Id, ct);
    }

    /// <summary>Per-subject and per-chapter result of an attempt. Staff who manage the exam always; the student (or guardian) once results are released.</summary>
    public async Task<AttemptBreakdownDto> BreakdownAsync(Guid attemptId, CancellationToken ct)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).Include(x => x.Student!.User).Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == attemptId, ct) ?? throw new NotFoundException("Attempt");
        var isStaff = access.IsAdmin || (access.IsTeacher && a.Exam!.TeacherId == user.UserId);
        var isOwner = a.StudentId == user.UserId;
        var isGuardian = access.IsParent && (await access.ChildIdsAsync(ct)).Contains(a.StudentId);
        if (!isStaff && !isOwner && !isGuardian) throw new NotFoundException("Attempt");
        if (!isStaff && !a.Exam!.ResultsReleased) throw new NotFoundException("Attempt");

        var questions = await db.ExamQuestions.Where(q => q.ExamId == a.ExamId).Include(q => q.Question).ToListAsync(ct);
        var answers = a.Answers.ToDictionary(x => x.ExamQuestionId);
        var items = questions.Select(eq =>
        {
            answers.TryGetValue(eq.Id, out var ans);
            var answered = ans is not null && ans.ResponseJson != "{}" && JsonSerializer.Deserialize<ExamResponse>(ans.ResponseJson, Json) is { } r
                && (r.OptionIds is { Count: > 0 } || !string.IsNullOrWhiteSpace(r.Text) || r.Number is not null || r.Matches is { Count: > 0 });
            return new DiagnosticItem(eq.Question!.SubjectId, eq.Question.ChapterId, eq.Marks, ans?.AwardedMarks, ans?.IsCorrect, answered);
        }).ToList();
        var subjectIds = items.Select(i => i.SubjectId).Distinct().ToList();
        var chapterIds = items.Where(i => i.ChapterId is not null).Select(i => i.ChapterId!.Value).Distinct().ToList();
        var subjectNames = await db.Subjects.Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var chapterTitles = await db.Chapters.Where(c => chapterIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var openNeeds = await db.LearningNeeds.Where(n => n.StudentId == a.StudentId && n.Status == LearningNeedStatus.Open)
            .Select(n => new { n.SubjectId, n.ChapterId }).ToListAsync(ct);
        var threshold = DiagnosticBreakdown.DefaultThresholdPercent;
        DiagnosticRowDto Dto(DiagnosticRow row) => new(row.SubjectId, subjectNames.GetValueOrDefault(row.SubjectId, "Subject"), row.ChapterId,
            row.ChapterId is { } c ? chapterTitles.GetValueOrDefault(c) : null, row.Questions, row.Answered, row.Correct, row.Marks, row.Awarded, row.Percent, row.Pending,
            DiagnosticBreakdown.NeedsAttention(row, threshold), openNeeds.Any(n => n.SubjectId == row.SubjectId && n.ChapterId == row.ChapterId));
        var bySubject = DiagnosticBreakdown.BySubject(items).OrderBy(r => subjectNames.GetValueOrDefault(r.SubjectId)).Select(Dto).ToList();
        var byChapter = DiagnosticBreakdown.ByChapter(items).OrderBy(r => subjectNames.GetValueOrDefault(r.SubjectId)).ThenBy(r => r.ChapterId is null).ThenBy(r => r.ChapterId is { } c ? chapterTitles.GetValueOrDefault(c) : "").Select(Dto).ToList();
        return new AttemptBreakdownDto(a.Id, a.ExamId, a.Exam!.Title, a.Exam.Purpose, a.StudentId, a.Student!.User!.FullName, a.Status, a.Score, questions.Sum(q => q.Marks),
            threshold, bySubject, byChapter, isStaff && user.Has(Permissions.LearningNeedsManage) && a.Status != AttemptStatus.InProgress);
    }

    /// <summary>
    /// Turns chosen breakdown rows into learning needs with source Diagnostic (addendum C: results are evidence, staff decide).
    /// Rows that are already open needs are skipped, so the same request can be sent again without duplicates.
    /// </summary>
    public async Task<ApplyDiagnosticResultDto> ApplyDiagnosticNeedsAsync(Guid attemptId, ApplyDiagnosticRequest r, CancellationToken ct)
    {
        access.Require(Permissions.LearningNeedsManage);
        var a = await db.ExamAttempts.Include(x => x.Exam).FirstOrDefaultAsync(x => x.Id == attemptId, ct) ?? throw new NotFoundException("Attempt");
        await GetManageableExamAsync(a.ExamId, ct);
        if (a.Status == AttemptStatus.InProgress) throw new DomainException("The attempt has not been submitted yet.", "attempt_in_progress");
        var note = Text.Optional(r.Note, "note", 1000);
        var allowed = await db.ExamQuestions.Where(q => q.ExamId == a.ExamId).Select(q => new { q.Question!.SubjectId, q.Question.ChapterId }).Distinct().ToListAsync(ct);
        var items = (r.Items ?? []).Distinct().ToList();
        if (items.Count == 0) throw RequestValidationException.For("items", "Choose at least one subject or chapter.");
        if (items.Count > 50) throw RequestValidationException.For("items", "Too many items.");
        foreach (var it in items)
            if (!allowed.Any(x => x.SubjectId == it.SubjectId && (it.ChapterId is null || x.ChapterId == it.ChapterId)))
                throw RequestValidationException.For("items", "Only subjects and chapters assessed by this attempt can be recorded.");
        var open = await db.LearningNeeds.Where(n => n.StudentId == a.StudentId && n.Status == LearningNeedStatus.Open).Select(n => new { n.SubjectId, n.ChapterId }).ToListAsync(ct);
        int created = 0, skipped = 0;
        foreach (var it in items)
        {
            if (open.Any(n => n.SubjectId == it.SubjectId && n.ChapterId == it.ChapterId)) { skipped++; continue; }
            db.LearningNeeds.Add(new StudentLearningNeed
            {
                StudentId = a.StudentId,
                SubjectId = it.SubjectId,
                ChapterId = it.ChapterId,
                Source = LearningNeedSource.Diagnostic,
                Note = note ?? $"From the diagnostic \"{a.Exam!.Title}\".",
                RecordedByUserId = user.UserId
            });
            created++;
        }
        if (created > 0)
        {
            audit.Record("student.learning_needs_from_diagnostic", "Student", a.StudentId, $"{a.Exam!.Title}: {created} recorded, {skipped} already open");
            await db.SaveChangesAsync(ct);
        }
        return new ApplyDiagnosticResultDto(created, skipped);
    }

    /// <summary>Background: finalises attempts whose deadline passed without submission. Runs per tenant.</summary>
    public async Task<int> FinaliseExpiredAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var expired = await db.ExamAttempts.Where(a => a.Status == AttemptStatus.InProgress && a.DeadlineUtc < now.AddSeconds(-30)).Take(200).ToListAsync(ct);
        foreach (var a in expired) await FinaliseAsync(a, now, ct);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
