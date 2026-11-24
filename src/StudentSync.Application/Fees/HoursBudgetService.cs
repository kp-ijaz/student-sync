using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Fees;

/// <summary>
/// "Classes must fit the hours" (owner decision 2026-11-17): a student can be booked only for classes their prepaid hours
/// cover. Free hours = hours added (active packages) − hours used (classes attended) − hours already booked (classes not
/// yet marked: upcoming, or finished without attendance). Every booking path — new classes, weekly plans, adding a student
/// to a class, rescheduling to a longer class — checks the same numbers on the server.
/// </summary>
public sealed class HoursBudgetService(IAppDbContext db)
{
    public sealed record Budget(Guid StudentId, string Name, bool HasHours, decimal Added, decimal Used, decimal Booked)
    {
        public decimal Left => Added - Used;
        public decimal Free => Added - Used - Booked;
    }

    /// <param name="excludeSessionId">A class being changed (rescheduled): its current length is not counted as booked.</param>
    public async Task<Dictionary<Guid, Budget>> ForStudentsAsync(IReadOnlyCollection<Guid> studentIds, Guid? excludeSessionId, CancellationToken ct)
    {
        var ids = studentIds.Distinct().ToList();
        var names = await db.Users.Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var packages = await db.StudentPackages.Where(p => ids.Contains(p.StudentId) && p.Status == PackageStatus.Active)
            .GroupBy(p => p.StudentId).Select(g => new { StudentId = g.Key, Hours = g.Sum(p => p.Hours), From = g.Min(p => p.CountsFromUtc) })
            .ToDictionaryAsync(x => x.StudentId, ct);

        var result = new Dictionary<Guid, Budget>();
        foreach (var id in ids)
        {
            var name = names.GetValueOrDefault(id, "This student");
            if (!packages.TryGetValue(id, out var pkg)) { result[id] = new Budget(id, name, false, 0, 0, 0); continue; }
            var mine = await (from p in db.SessionParticipants
                              where p.StudentId == id && p.Status == ParticipantStatus.Active
                              join s in db.Sessions on p.SessionId equals s.Id
                              where s.StartUtc >= pkg.From && s.Status != SessionStatus.Cancelled && s.Id != excludeSessionId
                              let mark = db.Attendance.Where(a => a.SessionId == s.Id && a.StudentId == id).Select(a => (AttendanceStatus?)a.Status).FirstOrDefault()
                              select new { s.StartUtc, s.EndUtc, s.Status, Mark = mark }).ToListAsync(ct);
            var used = mine.Where(x => x.Mark is AttendanceStatus.Present or AttendanceStatus.Late).Sum(x => FeeService.HoursOf(x.StartUtc, x.EndUtc));
            var booked = mine.Where(x => x.Mark is null && x.Status is SessionStatus.Scheduled or SessionStatus.Live).Sum(x => FeeService.HoursOf(x.StartUtc, x.EndUtc));
            result[id] = new Budget(id, name, true, pkg.Hours, used, booked);
        }
        return result;
    }

    /// <summary>What the new classes need from each student's free hours, and how many of them (in date order) fit.</summary>
    public async Task<List<HoursCheckDto>> CheckAsync(IReadOnlyCollection<Guid> studentIds, IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> classes,
        Guid? excludeSessionId, CancellationToken ct)
    {
        var budgets = await ForStudentsAsync(studentIds, excludeSessionId, ct);
        var ordered = classes.OrderBy(c => c.StartUtc).Select(c => FeeService.HoursOf(c.StartUtc, c.EndUtc)).ToList();
        return budgets.Values.Select(b =>
        {
            int fit = 0;
            decimal running = 0;
            foreach (var h in ordered) { if (running + h > b.Free) break; running += h; fit++; }
            return new HoursCheckDto(b.StudentId, b.Name, b.HasHours, b.Left, b.Booked, b.Free, ordered.Sum(), fit, ordered.Count);
        }).ToList();
    }

    /// <summary>Refuses (409 "hours_exceeded", with the numbers) when any student's free hours do not cover the classes.</summary>
    public async Task EnsureFitsAsync(IReadOnlyCollection<Guid> studentIds, IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> classes,
        Guid? excludeSessionId, CancellationToken ct)
    {
        if (studentIds.Count == 0 || classes.Count == 0) return;
        var short_ = (await CheckAsync(studentIds, classes, excludeSessionId, ct)).Where(h => !h.Fits).ToList();
        if (short_.Count == 0) return;
        throw new ConflictException(Describe(short_[0]), "hours_exceeded", short_);
    }

    public static string Describe(HoursCheckDto h)
    {
        var first = h.StudentName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? h.StudentName;
        if (!h.HasHours) return $"{first} has no hours yet. Add hours before booking classes.";
        var classes = h.Classes == 1 ? "This class needs" : $"These {h.Classes} classes need";
        return $"{first} has {Hrs(Math.Max(0, h.Free))} free ({Hrs(Math.Max(0, h.Left))} left, {Hrs(h.Booked)} already booked). {classes} {Hrs(h.Needed)}. Add hours, or book fewer classes.";
    }

    private static string Hrs(decimal h) => $"{h.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} h";
}
