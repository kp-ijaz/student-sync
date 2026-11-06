using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;

namespace StudentSync.Application.Communication;

/// <summary>Direct messaging limited to relationships the institution permits (teacher↔their students/parents, admins↔anyone).</summary>
public sealed class MessagingService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, NotificationService notify, IRealtimeNotifier realtime, FileService files)
{
    public async Task<IReadOnlyList<ContactDto>> ContactsAsync(CancellationToken ct)
    {
        access.Require(Permissions.MessagesSend);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == user.TenantId, ct);
        var admins = db.Users.Where(u => u.Role == UserRole.InstitutionAdmin && u.IsActive && u.Id != user.UserId);
        IQueryable<AppUser> allowed;
        if (access.IsAdmin) allowed = db.Users.Where(u => u.IsActive && u.Id != user.UserId && u.TenantId == user.TenantId);
        else if (access.IsTeacher)
        {
            var students = access.StudentsTaughtBy(user.UserId);
            var parents = db.ParentLinks.Where(l => l.IsVerified && students.Contains(l.StudentId)).Select(l => l.ParentId);
            allowed = admins.Union(db.Users.Where(u => u.IsActive && u.Id != user.UserId &&
                (u.Role == UserRole.Teacher
                 || (tenant.StudentsCanMessageTeachers && u.Role == UserRole.Student && students.Contains(u.Id))
                 || (tenant.ParentsCanMessageTeachers && u.Role == UserRole.Parent && parents.Contains(u.Id)))));
        }
        else if (access.IsStudent)
        {
            var teachers = TeachersOf([user.UserId]);
            allowed = tenant.StudentsCanMessageTeachers ? admins.Union(db.Users.Where(u => u.IsActive && teachers.Contains(u.Id))) : admins;
        }
        else if (access.IsParent)
        {
            var children = await access.ChildIdsAsync(ct);
            var teachers = TeachersOf(children);
            allowed = tenant.ParentsCanMessageTeachers ? admins.Union(db.Users.Where(u => u.IsActive && teachers.Contains(u.Id))) : admins;
        }
        else allowed = db.Users.Where(_ => false);
        return await allowed.Where(u => u.TenantId == user.TenantId).OrderBy(u => u.FullName).Take(500)
            .Select(u => new ContactDto(u.Id, u.FullName, u.Role.ToString(), null)).ToListAsync(ct);
    }

    private IQueryable<Guid> TeachersOf(ICollection<Guid> studentIds) =>
        db.SessionParticipants.Where(p => studentIds.Contains(p.StudentId)).Select(p => p.Session!.TeacherId)
            .Union(db.BatchEnrolments.Where(e => studentIds.Contains(e.StudentId) && e.Status == EnrolmentStatus.Active).Select(e => e.Batch!.TeacherId))
            .Union(db.ArrangementStudents.Where(a => studentIds.Contains(a.StudentId) && a.Status == EnrolmentStatus.Active)
                .Join(db.Arrangements, a => a.ArrangementId, x => x.Id, (a, x) => x.TeacherId));

    public async Task<IReadOnlyList<ConversationDto>> ConversationsAsync(CancellationToken ct)
    {
        var me = user.UserId;
        var rows = await db.Conversations.Where(c => c.Participants.Any(p => p.UserId == me)).OrderByDescending(c => c.LastMessageAtUtc).Take(100)
            .Select(c => new
            {
                c.Id,
                c.Type,
                c.Subject,
                c.LastMessageAtUtc,
                Participants = c.Participants.Where(p => p.UserId != me).Select(p => new LookupItem(p.UserId, db.Users.Where(u => u.Id == p.UserId).Select(u => u.FullName).First(), null)).ToList(),
                Last = db.Messages.Where(m => m.ConversationId == c.Id).OrderByDescending(m => m.CreatedAtUtc).Select(m => m.Body).FirstOrDefault(),
                LastRead = c.Participants.Where(p => p.UserId == me).Select(p => p.LastReadAtUtc).First(),
            }).ToListAsync(ct);
        var result = new List<ConversationDto>();
        foreach (var r in rows)
        {
            var unread = await db.Messages.CountAsync(m => m.ConversationId == r.Id && m.SenderUserId != me && (r.LastRead == null || m.CreatedAtUtc > r.LastRead), ct);
            var title = r.Subject ?? string.Join(", ", r.Participants.Select(p => p.Name));
            result.Add(new ConversationDto(r.Id, r.Type, title, r.Participants, r.Last is { Length: > 120 } l ? l[..120] + "…" : r.Last, r.LastMessageAtUtc, unread));
        }
        return result;
    }

    public async Task<Guid> StartAsync(StartConversationRequest r, CancellationToken ct)
    {
        var contacts = await ContactsAsync(ct);
        if (contacts.All(c => c.UserId != r.RecipientUserId)) throw new ForbiddenException("You cannot message this person.");
        var key = Conversation.BuildDirectKey(user.UserId, r.RecipientUserId);
        var convo = await db.Conversations.FirstOrDefaultAsync(c => c.DirectKey == key, ct);
        if (convo is null)
        {
            convo = new Conversation { Type = ConversationType.Direct, DirectKey = key, LastMessageAtUtc = clock.UtcNow };
            convo.Participants.Add(new ConversationParticipant { UserId = user.UserId });
            convo.Participants.Add(new ConversationParticipant { UserId = r.RecipientUserId });
            db.Conversations.Add(convo);
            await db.SaveChangesAsync(ct);
        }
        await SendAsync(convo.Id, new SendMessageRequest(r.Body, null), ct);
        return convo.Id;
    }

    public async Task<IReadOnlyList<MessageDto>> MessagesAsync(Guid conversationId, DateTime? beforeUtc, CancellationToken ct)
    {
        var me = user.UserId;
        var participant = await db.ConversationParticipants.FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.UserId == me, ct) ?? throw new NotFoundException("Conversation");
        var q = db.Messages.Where(m => m.ConversationId == conversationId);
        if (beforeUtc is { } before) q = q.Where(m => m.CreatedAtUtc < before);
        var items = await q.OrderByDescending(m => m.CreatedAtUtc).Take(50)
            .Select(m => new MessageDto(m.Id, m.ConversationId, m.SenderUserId, db.Users.Where(u => u.Id == m.SenderUserId).Select(u => u.FullName).First(), m.Body, m.CreatedAtUtc,
                m.AttachmentFileId == null ? null : db.Files.Where(f => f.Id == m.AttachmentFileId).Select(f => new FileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes)).FirstOrDefault(),
                m.SenderUserId == me)).ToListAsync(ct);
        participant.LastReadAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        items.Reverse();
        return items;
    }

    public async Task<Guid> SendAsync(Guid conversationId, SendMessageRequest r, CancellationToken ct)
    {
        access.Require(Permissions.MessagesSend);
        var convo = await db.Conversations.Include(c => c.Participants).FirstOrDefaultAsync(c => c.Id == conversationId && c.Participants.Any(p => p.UserId == user.UserId), ct)
                    ?? throw new NotFoundException("Conversation");
        var attachment = r.AttachmentFileId is { } fid ? (await files.ClaimAsync([fid], FilePurpose.MessageAttachment, ct)).Single() : (Guid?)null;
        var msg = new Message { ConversationId = convo.Id, SenderUserId = user.UserId, Body = Text.Required(r.Body, "body", 5000), AttachmentFileId = attachment };
        db.Messages.Add(msg);
        convo.LastMessageAtUtc = clock.UtcNow;
        convo.Participants.First(p => p.UserId == user.UserId).LastReadAtUtc = clock.UtcNow;
        var others = convo.Participants.Where(p => p.UserId != user.UserId).Select(p => p.UserId).ToList();
        var senderName = await db.Users.Where(u => u.Id == user.UserId).Select(u => u.FullName).FirstAsync(ct);
        await notify.NotifyAsync(others, NotificationType.MessageReceived, $"Message from {senderName}", msg.Body.Length > 140 ? msg.Body[..140] + "…" : msg.Body,
            $"/messages/{convo.Id}", $"message:{msg.Id}", ct);
        await db.SaveChangesAsync(ct);
        await realtime.SendToUsersAsync(others, "message", new { conversationId = convo.Id, messageId = msg.Id }, ct);
        return msg.Id;
    }

    // ---------------- Announcements ----------------

    public async Task<IReadOnlyList<AnnouncementDto>> AnnouncementsAsync(CancellationToken ct)
    {
        var q = db.Announcements.AsQueryable();
        if (!access.IsAdmin)
        {
            var me = user.UserId;
            var role = user.Role;
            var children = await access.ChildIdsAsync(ct);
            q = q.Where(a => a.Audience == AnnouncementAudience.Everyone
                || (a.Audience == AnnouncementAudience.Teachers && role == UserRole.Teacher)
                || (a.Audience == AnnouncementAudience.Students && role == UserRole.Student)
                || (a.Audience == AnnouncementAudience.Parents && role == UserRole.Parent)
                || (a.Audience == AnnouncementAudience.Batch && (db.Batches.Any(b => b.Id == a.BatchId && b.TeacherId == me)
                    || db.BatchEnrolments.Any(e => e.BatchId == a.BatchId && e.Status == EnrolmentStatus.Active && (e.StudentId == me || children.Contains(e.StudentId))))));
        }
        return await q.OrderByDescending(a => a.PublishedAtUtc).Take(50)
            .Select(a => new AnnouncementDto(a.Id, a.Title, a.Body, a.Audience, a.BatchId, db.Batches.Where(b => b.Id == a.BatchId).Select(b => b.Name).FirstOrDefault(),
                db.Users.Where(u => u.Id == a.AuthorUserId).Select(u => u.FullName).First(), a.PublishedAtUtc)).ToListAsync(ct);
    }

    public async Task<Guid> PublishAnnouncementAsync(CreateAnnouncementRequest r, CancellationToken ct)
    {
        access.Require(Permissions.AnnouncementsPublish);
        if (!access.IsAdmin && r.Audience != AnnouncementAudience.Batch) throw new ForbiddenException("Teachers can announce to their own batches only.");
        List<Guid> recipients;
        if (r.Audience == AnnouncementAudience.Batch)
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == r.BatchId && (access.IsAdmin || b.TeacherId == user.UserId), ct)
                        ?? throw RequestValidationException.For("batchId", "Batch not found.");
            var students = await db.BatchEnrolments.Where(e => e.BatchId == batch.Id && e.Status == EnrolmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct);
            recipients = await notify.WithGuardiansAsync(students, ct);
        }
        else
        {
            var roles = r.Audience switch
            {
                AnnouncementAudience.Teachers => [UserRole.Teacher],
                AnnouncementAudience.Students => [UserRole.Student],
                AnnouncementAudience.Parents => [UserRole.Parent],
                _ => new[] { UserRole.Teacher, UserRole.Student, UserRole.Parent, UserRole.InstitutionAdmin },
            };
            recipients = await db.Users.Where(u => u.TenantId == user.TenantId && u.IsActive && roles.Contains(u.Role)).Select(u => u.Id).ToListAsync(ct);
        }
        var a = new Announcement
        {
            Title = Text.Required(r.Title, "title", 200),
            Body = Text.Required(r.Body, "body", 10_000),
            Audience = r.Audience,
            BatchId = r.Audience == AnnouncementAudience.Batch ? r.BatchId : null,
            AuthorUserId = user.UserId,
            PublishedAtUtc = clock.UtcNow,
        };
        db.Announcements.Add(a);
        await notify.NotifyAsync(recipients.Where(id => id != user.UserId), NotificationType.Announcement, a.Title, a.Body.Length > 200 ? a.Body[..200] + "…" : a.Body,
            "/announcements", $"announcement:{a.Id}", ct);
        await db.SaveChangesAsync(ct);
        return a.Id;
    }
}
