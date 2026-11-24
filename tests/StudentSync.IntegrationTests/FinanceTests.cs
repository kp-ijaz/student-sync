using System.Net;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>Finance (owner request 2026-11-24): expenses CRUD, income − salaries − expenses = profit, and money left open by people who left.</summary>
public sealed class FinanceTests(ApiFixture api)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Expenses_support_full_crud_for_the_admin_only()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var day = Today.AddDays(-Random.Shared.Next(400, 1500));
        var id = (await admin.Post<IdResponse>("finance/expenses", new SaveExpenseRequest(day, ExpenseCategory.Rent, 25000, $"Rent {Tag()}", "Landlord (fictional)", PaymentMethod.BankTransfer, "TRX-1"))).Id;
        var x = await admin.Get<ExpenseDto>($"finance/expenses/{id}");
        Assert.Equal((ExpenseCategory.Rent, 25000m, PaymentMethod.BankTransfer), (x.Category, x.Amount, x.Method));

        await admin.Put($"finance/expenses/{id}", new SaveExpenseRequest(day, ExpenseCategory.Utilities, 3200.50m, "Electricity bill"));
        x = await admin.Get<ExpenseDto>($"finance/expenses/{id}");
        Assert.Equal((ExpenseCategory.Utilities, 3200.50m, "Electricity bill", (string?)null), (x.Category, x.Amount, x.Description, x.PaidTo));

        // Validation: amount, future day, empty description, gateway-only method.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("finance/expenses", new SaveExpenseRequest(day, ExpenseCategory.Rent, 0, "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("finance/expenses", new SaveExpenseRequest(Today.AddDays(2), ExpenseCategory.Rent, 10, "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("finance/expenses", new SaveExpenseRequest(day, ExpenseCategory.Rent, 10, " "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("finance/expenses", new SaveExpenseRequest(day, ExpenseCategory.Rent, 10, "X", Method: PaymentMethod.Online))).StatusCode);

        // Teachers and students are refused; another institution cannot see or change it.
        var teacher = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetRaw("finance/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostRaw("finance/expenses", new SaveExpenseRequest(day, ExpenseCategory.Rent, 10, "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Aisha)).GetRaw("finance/former")).StatusCode);
        var outsider = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetRaw($"finance/expenses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteRaw($"finance/expenses/{id}")).StatusCode);
        Assert.DoesNotContain((await outsider.Get<FinanceEntriesDto>($"finance/entries?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&pageSize=100")).Page.Items, e => e.ExpenseId == id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"finance/expenses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetRaw($"finance/expenses/{id}")).StatusCode);
    }

    [Fact]
    public async Task Summary_adds_up_fees_received_salaries_paid_and_expenses()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var day = Today.AddDays(-Random.Shared.Next(60, 330));
        var student = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Finance {tag} (Demo Student)", $"fin-{tag}@horizon.example.test", $"FN-{tag}", null, null, "Asia/Dubai"))).Id;
        var package = (await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 9000, day.AddDays(-5)))).Id;
        await admin.Post<IdResponse>($"students/{student}/packages/{package}/payments", new RecordPaymentRequest(7000, day, PaymentMethod.Upi));
        var teacher = (await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest($"Finance {tag} (Demo Teacher)", $"fin-t-{tag}@horizon.example.test", null, null, null, "Asia/Dubai"))).Id;
        await admin.Post<IdResponse>($"teachers/{teacher}/pay/payments", new AddTeacherPaymentRequest(2500, day, PaymentMethod.Cash, null, $"Salary {tag}", null, null));
        await admin.Post<IdResponse>("finance/expenses", new SaveExpenseRequest(day, ExpenseCategory.Supplies, 800, $"Books {tag}"));

        var s = await admin.Get<FinanceSummaryDto>($"finance/summary?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}");
        Assert.True(s.Income >= 7000 && s.Salaries >= 2500 && s.Expenses >= 800);
        Assert.Equal(s.Income - s.Salaries - s.Expenses, s.Profit);
        Assert.Equal((day.AddDays(-1), day.AddDays(-1)), (s.Previous.From, s.Previous.To)); // a one-day period compares with the day before
        var entries = (await admin.Get<FinanceEntriesDto>($"finance/entries?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&search={tag}&pageSize=100")).Page.Items;
        Assert.Contains(entries, e => e.Kind == FinanceEntryKind.Salary && e.PersonId == teacher && e.Amount == 2500 && e.RecordId != Guid.Empty);
        Assert.Contains(entries, e => e.Kind == FinanceEntryKind.Expense && e.Title == $"Books {tag}" && e.ExpenseId != null && e.Category == ExpenseCategory.Supplies);
        var income = (await admin.Get<FinanceEntriesDto>($"finance/entries?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&kind=Income&pageSize=100")).Page.Items;
        Assert.Contains(income, e => e.PersonId == student && e.Amount == 7000 && e.Method == PaymentMethod.Upi);
        Assert.Contains(s.ExpensesByCategory, c => c.Category == ExpenseCategory.Supplies && c.Amount >= 800);
        var month = Assert.Single(s.Months, m => m.Month == new DateOnly(day.Year, day.Month, 1));
        Assert.True(month.Income >= 7000 && month.Salaries >= 2500 && month.Expenses >= 800);
        Assert.Equal(12, s.Months.Count);

        // A removed payment no longer counts as income.
        var paymentId = (await admin.Get<StudentFeesDto>($"students/{student}/fees")).Packages.Single().Payments.Single().Id;
        await admin.Post($"students/{student}/payments/{paymentId}/void", new VoidPaymentRequest("Entered by mistake"));
        Assert.DoesNotContain((await admin.Get<FinanceEntriesDto>($"finance/entries?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&kind=Income&pageSize=100")).Page.Items, e => e.PersonId == student);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetRaw($"finance/summary?from={day:yyyy-MM-dd}&to={day.AddDays(-1):yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task People_who_left_with_money_open_are_listed_apart()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Finance {tag}"))).Id;
        var teacher = (await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest($"Leaver {tag} (Demo Teacher)", $"leave-t-{tag}@horizon.example.test", null, null, null, "Asia/Dubai", [subject]))).Id;
        await admin.Put($"teachers/{teacher}/pay/rate", new SetTeacherRateRequest(400, Today.AddDays(-200)));
        var student = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Leaver {tag} (Demo Student)", $"leave-{tag}@horizon.example.test", $"LV-{tag}", null, null, "Asia/Dubai"))).Id;
        await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 5000, Today.AddDays(-150), PaidNow: 1000));

        // One 2-hour class taught and completed: the teacher earned 800, nothing paid yet.
        var session = (await admin.Post<CreateSessionResponse>("sessions", new CreateSessionRequest("Finance class", SessionType.OneToOne, teacher, null, null, null, subject, null, [student], "Asia/Dubai",
            Today.AddDays(-Random.Shared.Next(10, 100)), new TimeOnly(Random.Shared.Next(6, 18), 0), 120, DeliveryMode.InPerson, null, null, null, null))).SessionIds.Single();
        await admin.Post($"sessions/{session}/status", new SetSessionStatusRequest(SessionStatus.Completed));

        var before = await admin.Get<FormerBalancesDto>("finance/former");
        Assert.DoesNotContain(before.Students, s => s.StudentId == student);
        Assert.DoesNotContain(before.Teachers, t => t.TeacherId == teacher);

        await admin.DeleteRaw($"students/{student}");
        await admin.DeleteRaw($"teachers/{teacher}");
        var after = await admin.Get<FormerBalancesDto>("finance/former");
        var s = Assert.Single(after.Students, x => x.StudentId == student);
        Assert.Equal((4000m, PersonStatus.Archived), (s.Due, s.Status));
        var t = Assert.Single(after.Teachers, x => x.TeacherId == teacher);
        Assert.Equal((800m, 0m, 800m, PersonStatus.Archived), (t.Earned, t.Paid, t.Balance, t.Status));
        Assert.True(after.StudentsOwe >= 4000 && after.OwedToTeachers >= 800);

        // Paid more than earned: shown as the teacher owing the centre.
        await admin.Post<IdResponse>($"teachers/{teacher}/pay/payments", new AddTeacherPaymentRequest(1000, Today, PaymentMethod.Cash, null, "Final settlement", null, null));
        Assert.Equal(-200m, (await admin.Get<FormerBalancesDto>("finance/former")).Teachers.Single(x => x.TeacherId == teacher).Balance);

        // Settled: off the list.
        await admin.Post<IdResponse>($"teachers/{teacher}/pay/payments", new AddTeacherPaymentRequest(1, Today, PaymentMethod.Cash, null, null, null, null)); // now −201
        var pay = await admin.Get<TeacherPayDto>($"teachers/{teacher}/pay");
        var last = pay.Payments.First(p => p.Amount == 1);
        await admin.Post($"teachers/{teacher}/pay/payments/{last.Id}/void", new VoidTeacherPaymentRequest("Test"));
        var first = pay.Payments.First(p => p.Amount == 1000);
        await admin.Post($"teachers/{teacher}/pay/payments/{first.Id}/void", new VoidTeacherPaymentRequest("Test"));
        await admin.Post<IdResponse>($"teachers/{teacher}/pay/payments", new AddTeacherPaymentRequest(800, Today, PaymentMethod.Cash, null, "Settled", null, null));
        Assert.DoesNotContain((await admin.Get<FormerBalancesDto>("finance/former")).Teachers, x => x.TeacherId == teacher);
    }

    [Fact]
    public async Task Transactions_can_be_searched_filtered_sorted_paged_and_exported()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var day = Today.AddDays(-Random.Shared.Next(400, 1500));
        var range = $"from={day:yyyy-MM-dd}&to={day.AddDays(2):yyyy-MM-dd}";
        await admin.Post<IdResponse>("finance/expenses", new SaveExpenseRequest(day, ExpenseCategory.Rent, 30000, $"Rent {tag}", "Landlord", PaymentMethod.BankTransfer, $"R-{tag}"));
        await admin.Post<IdResponse>("finance/expenses", new SaveExpenseRequest(day.AddDays(1), ExpenseCategory.Utilities, 1200, $"Power {tag}"));
        await admin.Post<IdResponse>("finance/expenses", new SaveExpenseRequest(day.AddDays(2), ExpenseCategory.Supplies, 450, $"Chalk {tag}"));

        var all = await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search={tag}&pageSize=100");
        Assert.Equal((3, 0, 0, 3), (all.Page.TotalCount, all.IncomeCount, all.SalaryCount, all.ExpenseCount));
        Assert.Equal((0m, 31650m, -31650m), (all.MoneyIn, all.MoneyOut, all.Net));
        Assert.Equal([$"Chalk {tag}", $"Power {tag}", $"Rent {tag}"], all.Page.Items.Select(e => e.Title)); // newest first

        Assert.Equal($"Rent {tag}", (await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search={tag}&sort=Largest")).Page.Items[0].Title);
        Assert.Equal($"Chalk {tag}", (await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search={tag}&sort=Smallest")).Page.Items[0].Title);
        Assert.Equal($"Rent {tag}", (await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search={tag}&sort=Oldest")).Page.Items[0].Title);
        Assert.Equal($"Power {tag}", Assert.Single((await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search={tag}&category=Utilities")).Page.Items).Title);
        Assert.Equal($"Rent {tag}", Assert.Single((await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search=r-{tag}")).Page.Items).Title); // by reference
        Assert.Empty((await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search={tag}&kind=Income")).Page.Items);

        var page2 = await admin.Get<FinanceEntriesDto>($"finance/entries?{range}&search={tag}&pageSize=2&page=2");
        Assert.Equal((3, 2, 2), (page2.Page.TotalCount, page2.Page.TotalPages, page2.Page.Page));
        Assert.Equal($"Rent {tag}", Assert.Single(page2.Page.Items).Title);

        var csv = await admin.Http.GetStringAsync($"/api/v1/finance/entries.csv?{range}&search={tag}");
        var lines = csv.Trim().Split('\n');
        Assert.StartsWith("Date,Type,Who / what", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, l => l.Contains($"Rent {tag}") && l.Contains("Bank transfer") && l.EndsWith("-30000.00"));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).GetRaw($"finance/entries.csv?{range}")).StatusCode);
    }

    [Fact]
    public async Task A_salary_payment_shows_on_the_teacher_salaries_and_finance_and_a_retry_pays_once()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var teacher = (await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest($"Advance {tag} (Demo Teacher)", $"adv-{tag}@horizon.example.test", null, null, null, "Asia/Dubai"))).Id;
        var request = new AddTeacherPaymentRequest(1500, Today, PaymentMethod.Upi, $"U-{tag}", "Advance", null, null);
        admin.Http.DefaultRequestHeaders.Add("Idempotency-Key", $"salary-{tag}");
        var first = (await admin.Post<IdResponse>($"teachers/{teacher}/pay/payments", request)).Id;
        Assert.Equal(first, (await admin.Post<IdResponse>($"teachers/{teacher}/pay/payments", request)).Id); // double click / retry
        admin.Http.DefaultRequestHeaders.Remove("Idempotency-Key");

        var pay = await admin.Get<TeacherPayDto>($"teachers/{teacher}/pay");
        Assert.Equal(first, Assert.Single(pay.Payments).Id);
        Assert.Equal(-1500m, pay.Due); // paid in advance
        var month = new DateOnly(Today.Year, Today.Month, 1);
        Assert.Contains((await admin.Get<SalaryMonthDto>($"salaries?month={month:yyyy-MM-dd}")).Teachers, t => t.TeacherId == teacher && t.PaidInMonth == 1500);
        var found = Assert.Single((await admin.Get<FinanceEntriesDto>($"finance/entries?kind=Salary&search=u-{tag}")).Page.Items);
        Assert.Equal((teacher, first, 1500m), (found.PersonId, found.RecordId, found.Amount));
    }

    /// <summary>Owner request 2026-11-24: the admin can correct or remove any recorded payment; nobody else can.</summary>
    [Fact]
    public async Task Admin_can_correct_fee_and_salary_payments_and_nobody_else_can()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var day = Today.AddDays(-Random.Shared.Next(20, 300));
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Edit pay {tag}"))).Id;
        var teacher = (await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest($"Edit {tag} (Demo Teacher)", $"edit-t-{tag}@horizon.example.test", null, null, null, "Asia/Dubai", [subject]))).Id;
        var student = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Edit {tag} (Demo Student)", $"edit-{tag}@horizon.example.test", $"ED-{tag}", null, null, "Asia/Dubai"))).Id;

        // Fee payment: 2000 of 5000 by cash, corrected to 2500 by UPI on another day.
        await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 5000, day, PaidNow: 2000));
        var feeId = (await admin.Get<StudentFeesDto>($"students/{student}/fees")).Packages.Single().Payments.Single().Id;
        await admin.Put($"students/{student}/payments/{feeId}", new RecordPaymentRequest(2500, day.AddDays(1), PaymentMethod.Upi, "UPI-EDIT"));
        var package = (await admin.Get<StudentFeesDto>($"students/{student}/fees")).Packages.Single();
        var fee = package.Payments.Single();
        Assert.Equal((2500m, day.AddDays(1), PaymentMethod.Upi, "UPI-EDIT"), (fee.Amount, fee.PaidOn, fee.Method, fee.Reference));
        Assert.Equal((2500m, 2500m), (package.Paid, package.Balance));
        // Finance shows the corrected line once.
        var income = (await admin.Get<FinanceEntriesDto>($"finance/entries?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}&kind=Income&pageSize=100")).Page.Items.Where(e => e.PersonId == student).ToList();
        Assert.Equal((2500m, day.AddDays(1)), (Assert.Single(income).Amount, income[0].Date));

        // Never more than the package price, never zero, never a future day, never the gateway-only method.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"students/{student}/payments/{feeId}", new RecordPaymentRequest(5000.01m, day))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"students/{student}/payments/{feeId}", new RecordPaymentRequest(0, day))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"students/{student}/payments/{feeId}", new RecordPaymentRequest(100, Today.AddDays(3)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"students/{student}/payments/{feeId}", new RecordPaymentRequest(100, day, PaymentMethod.Online))).StatusCode);
        // The whole price is allowed (the payment itself does not count against the room).
        await admin.Put($"students/{student}/payments/{feeId}", new RecordPaymentRequest(5000, day));
        Assert.Equal(0m, (await admin.Get<StudentFeesDto>($"students/{student}/fees")).Packages.Single().Balance);

        // Salary payment: 1200 cash corrected to 900 by bank transfer with a period and note.
        var salaryId = (await admin.Post<IdResponse>($"teachers/{teacher}/pay/payments", new AddTeacherPaymentRequest(1200, day, PaymentMethod.Cash, null, "Advance", null, null))).Id;
        await admin.Put($"teachers/{teacher}/pay/payments/{salaryId}", new AddTeacherPaymentRequest(900, day.AddDays(2), PaymentMethod.BankTransfer, "TRX-9", "Corrected", day.AddDays(-30), day));
        var pay = await admin.Get<TeacherPayDto>($"teachers/{teacher}/pay");
        var salary = Assert.Single(pay.Payments);
        Assert.Equal((900m, day.AddDays(2), PaymentMethod.BankTransfer, "TRX-9", "Corrected", (DateOnly?)day.AddDays(-30)), (salary.Amount, salary.PaidOn, salary.Method, salary.Reference, salary.Note, salary.PeriodFrom));
        Assert.Equal(900m, pay.Paid);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{teacher}/pay/payments/{salaryId}", new AddTeacherPaymentRequest(0, day, PaymentMethod.Cash, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{teacher}/pay/payments/{salaryId}", new AddTeacherPaymentRequest(10, day, PaymentMethod.Cash, null, null, day, day.AddDays(-1)))).StatusCode);

        // Another teacher's id in the address does not reach this payment; teachers and students cannot edit at all.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutRaw($"teachers/{Guid.NewGuid()}/pay/payments/{salaryId}", new AddTeacherPaymentRequest(1, day, PaymentMethod.Cash, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutRaw($"students/{Guid.NewGuid()}/payments/{feeId}", new RecordPaymentRequest(1, day))).StatusCode);
        foreach (var account in new[] { Accounts.Amira, Accounts.Aisha })
        {
            var other = await api.LoginAsync(account);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PutRaw($"teachers/{teacher}/pay/payments/{salaryId}", new AddTeacherPaymentRequest(1, day, PaymentMethod.Cash, null, null, null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PutRaw($"students/{student}/payments/{feeId}", new RecordPaymentRequest(1, day))).StatusCode);
        }

        // Every correction is in the audit log with the figures before and after.
        var audit = (await admin.Get<PagedResult<AuditEntryDto>>("institution/audit?pageSize=100")).Items;
        Assert.Contains(audit, a => a.Action == "teacher.salary_edited" && a.EntityId == teacher.ToString() && a.Summary!.Contains("1200") && a.Summary.Contains("900"));
        Assert.Contains(audit, a => a.Action == "fees.payment_edited" && a.EntityId == student.ToString() && a.Summary!.Contains("2000") && a.Summary.Contains("2500"));

        // A removed payment cannot be edited afterwards.
        await admin.Post($"teachers/{teacher}/pay/payments/{salaryId}/void", new VoidTeacherPaymentRequest("Test"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutRaw($"teachers/{teacher}/pay/payments/{salaryId}", new AddTeacherPaymentRequest(1, day, PaymentMethod.Cash, null, null, null, null))).StatusCode);
        await admin.Post($"students/{student}/payments/{feeId}/void", new VoidPaymentRequest("Test"));
        Assert.NotEqual(HttpStatusCode.NoContent, (await admin.PutRaw($"students/{student}/payments/{feeId}", new RecordPaymentRequest(1, day))).StatusCode);
    }
}
