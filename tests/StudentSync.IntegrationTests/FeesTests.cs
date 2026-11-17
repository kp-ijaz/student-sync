using System.Net;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Prepaid hour packages (addendum A): the admin sells hours, records payments, and sees who owes money or is running out
/// of hours. Hours used come from attendance (present/late) exactly once; absences and cancelled classes use nothing.
/// </summary>
public sealed class FeesTests(ApiFixture api)
{
    private static CreateStudentRequest NewStudent(string tag) => new(
        "Rafna Fees (Demo Student)", $"fees-{tag}@horizon.example.test", $"FE-{tag}"[..12], null, "Fictional Public School", "Asia/Dubai", null, null,
        null, null, null, new DateOnly(2026, 1, 5), [], null, null);

    [Fact]
    public async Task Admin_sells_hours_records_payments_and_sees_what_is_due()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var id = (await admin.Post<CreatedPersonResponse>("students", NewStudent(Guid.NewGuid().ToString("N")))).Id;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 2 hours for 1000, 400 paid now. A retried request with the same Idempotency-Key creates one package.
        admin.Http.DefaultRequestHeaders.Add("Idempotency-Key", $"pkg-{id}");
        var sale = new SavePackageRequest(2, 1000, today.AddDays(-360), today.AddDays(-300), PaidNow: 400, Method: PaymentMethod.Upi, Reference: "UPI-123");
        var packageId = (await admin.Post<IdResponse>($"students/{id}/packages", sale)).Id;
        Assert.Equal(packageId, (await admin.Post<IdResponse>($"students/{id}/packages", sale)).Id);
        admin.Http.DefaultRequestHeaders.Remove("Idempotency-Key");

        var fees = await admin.Get<StudentFeesDto>($"students/{id}/fees");
        Assert.Equal((2m, 0m, 2m, 1000m, 400m, 600m), (fees.HoursBought, fees.HoursUsed, fees.HoursLeft, fees.Billed, fees.Paid, fees.Due));
        var package = Assert.Single(fees.Packages);
        Assert.Equal(("2 hours", "UPI-123"), (package.Name, package.Payments.Single().Reference));

        // A 90-minute class attended (late) uses 1.5 h; an absence and a cancelled class use nothing. Marking twice counts once.
        var amira = await api.LoginAsync(Accounts.Amira);
        var maths = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest("Maths"))).Id;
        await admin.Post($"academics/subjects/{maths}/teachers", new AssignTeacherRequest(amira.UserId)); // teachers book only their own subjects
        async Task<Guid> PastClass(int daysAgo, int minutes) => (await amira.Post<CreateSessionResponse>("sessions", new CreateSessionRequest(
            "Maths · Rafna", SessionType.OneToOne, null, null, null, null, maths, null, [id], "Asia/Dubai",
            today.AddDays(-daysAgo), new TimeOnly(Random.Shared.Next(1, 22), 5 * Random.Shared.Next(0, 6)), minutes, DeliveryMode.InPerson, null, null, null, null))).SessionIds.Single();
        var attended = await PastClass(Random.Shared.Next(20, 120), 90);
        // Classes must fit the hours added (2 h): 1.5 h booked leaves room for one 30-minute class.
        var missed = await PastClass(Random.Shared.Next(130, 250), 30);
        await amira.Put($"sessions/{attended}/attendance", new MarkAttendanceRequest([new AttendanceEntry(id, AttendanceStatus.Present, null)]));
        await amira.Put($"sessions/{attended}/attendance", new MarkAttendanceRequest([new AttendanceEntry(id, AttendanceStatus.Late, null)]));
        await amira.Put($"sessions/{missed}/attendance", new MarkAttendanceRequest([new AttendanceEntry(id, AttendanceStatus.Absent, null)]));

        fees = await admin.Get<StudentFeesDto>($"students/{id}/fees");
        Assert.Equal((1.5m, 0.5m), (fees.HoursUsed, fees.HoursLeft));
        Assert.Equal(attended, Assert.Single(fees.RecentUsage).SessionId);

        // Overdue and low on hours: on the front-desk list.
        var due = await admin.Get<FeesDueDto>("fees/due?take=200");
        var row = Assert.Single(due.Items, i => i.StudentId == id);
        Assert.Equal((600m, 0.5m), (row.Due, row.HoursLeft));
        Assert.True(row.DaysOverdue >= 299);

        // Payments cannot exceed what is due; paying the rest clears it; a mistaken payment is voided, kept and no longer counts.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"students/{id}/packages/{packageId}/payments", new RecordPaymentRequest(700))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"students/{id}/packages/{packageId}/payments", new RecordPaymentRequest(10, today.AddDays(3)))).StatusCode);
        var paymentId = (await admin.Post<IdResponse>($"students/{id}/packages/{packageId}/payments", new RecordPaymentRequest(600, Method: PaymentMethod.Cash))).Id;
        Assert.Equal(0m, (await admin.Get<StudentFeesDto>($"students/{id}/fees")).Due);
        await admin.Post($"students/{id}/payments/{paymentId}/void", new VoidPaymentRequest("Entered twice"));
        fees = await admin.Get<StudentFeesDto>($"students/{id}/fees");
        Assert.Equal(600m, fees.Due);
        Assert.Contains(fees.Packages.Single().Payments, p => p.Id == paymentId && p.Voided && p.VoidReason == "Entered twice");

        // A package with valid payments cannot be cancelled; validation rejects nonsense.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostRaw($"students/{id}/packages/{packageId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"students/{id}/packages", new SavePackageRequest(0, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"students/{id}/packages", new SavePackageRequest(10, 100, PaidNow: 150))).StatusCode);

        // Fees are the admin's: teachers and students are refused, other institutions cannot see the student.
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.GetRaw($"students/{id}/fees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.GetRaw("fees/due")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.PostRaw($"students/{id}/packages", new SavePackageRequest(10, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Ben)).GetRaw("fees/due")).StatusCode);
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"students/{id}/fees")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostRaw($"students/{id}/packages/{packageId}/payments", new RecordPaymentRequest(10))).StatusCode);
        Assert.DoesNotContain((await other.Get<FeesDueDto>("fees/due?take=200")).Items, i => i.StudentId == id);
    }
}
