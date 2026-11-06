using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Contracts;
using StudentSync.Domain.Content;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.IntegrationTests;

public sealed class ProfilePhotoTests(ApiFixture api)
{
    // ---------- Test images (structurally valid; the server never decodes pixels) ----------

    private static byte[] JpegSegment(byte marker, byte[] payload)
    {
        var s = new byte[4 + payload.Length];
        s[0] = 0xFF;
        s[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(s, 4);
        return s;
    }

    /// <summary>A JPEG carrying an EXIF block with GPS coordinates, as a phone camera would produce.</summary>
    private static byte[] JpegWithGps() =>
    [
        0xFF, 0xD8,
        .. JpegSegment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]),
        .. JpegSegment(0xE1, [.. "Exif\0\0"u8, .. "GPSLatitude=25.2048;GPSLongitude=55.2708"u8]),
        .. JpegSegment(0xDB, [0, .. Enumerable.Range(1, 64).Select(i => (byte)i)]),
        .. JpegSegment(0xC0, [8, 0, 16, 0, 16, 1, 1, 0x11, 0]),
        .. JpegSegment(0xDA, [1, 1, 0, 0, 63, 0]),
        0x12, 0x34, 0xFF, 0x00, 0x56,
        0xFF, 0xD9,
    ];

    private static byte[] PngChunk(string type, byte[] data)
    {
        var c = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(c, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(c, 4);
        data.CopyTo(c, 8);
        return c;
    }

    private static byte[] Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        .. PngChunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]),
        .. PngChunk("tEXt", "Comment\0secret"u8.ToArray()),
        .. PngChunk("IDAT", [0x78, 0x9C, 1, 2, 3]),
        .. PngChunk("IEND", []),
    ];

    private static async Task<HttpResponseMessage> PutPhoto(Client c, string path, byte[] bytes, string contentType, string fileName = "photo.jpg")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new(contentType);
        form.Add(file, "file", fileName);
        return await c.Http.PutAsync("/api/v1/" + path, form);
    }

    private static Guid FileIdOf(string photoUrl) => Guid.ParseExact(photoUrl.Split("&v=")[1], "N");

    private static async Task AssertFieldError(HttpResponseMessage r, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var problem = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out _), await r.Content.ReadAsStringAsync());
    }

    // ---------- Tests ----------

    [Fact]
    public async Task Own_photo_upload_strips_metadata_and_is_returned_by_me_until_removed()
    {
        var ben = await api.LoginAsync(Accounts.Ben);
        var r = await PutPhoto(ben, "me/photo", JpegWithGps(), "image/jpeg");
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var updated = (await r.Content.ReadFromJsonAsync<CurrentUserDto>(ApiFixture.Json))!;
        Assert.NotNull(updated.PhotoUrl);
        Assert.StartsWith("/api/v1/files/download?token=", updated.PhotoUrl);

        var me = await ben.Get<CurrentUserDto>("me");
        Assert.NotNull(me.PhotoUrl);
        Assert.Equal(FileIdOf(updated.PhotoUrl!), FileIdOf(me.PhotoUrl!));

        // The stored image is served as JPEG and no longer carries the GPS position.
        var download = await api.Anonymous().GetAsync(me.PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("GPS", Encoding.Latin1.GetString(bytes));
        Assert.Equal([0xFF, 0xD8], bytes[..2]);

        // Replacing soft-deletes the previous file, so its URL stops working.
        var png = await PutPhoto(ben, "me/photo", Png(), "image/png", "photo.png");
        var replaced = (await png.Content.ReadFromJsonAsync<CurrentUserDto>(ApiFixture.Json))!;
        Assert.NotEqual(FileIdOf(me.PhotoUrl!), FileIdOf(replaced.PhotoUrl!));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Anonymous().GetAsync(me.PhotoUrl)).StatusCode);
        Assert.DoesNotContain("secret", Encoding.Latin1.GetString(await (await api.Anonymous().GetAsync(replaced.PhotoUrl)).Content.ReadAsByteArrayAsync()));

        // Removing clears it everywhere.
        var removed = await ben.Http.DeleteAsync("/api/v1/me/photo");
        Assert.True(removed.IsSuccessStatusCode);
        Assert.Null((await removed.Content.ReadFromJsonAsync<CurrentUserDto>(ApiFixture.Json))!.PhotoUrl);
        Assert.Null((await ben.Get<CurrentUserDto>("me")).PhotoUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Anonymous().GetAsync(replaced.PhotoUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetRaw($"files/{FileIdOf(replaced.PhotoUrl!)}/link")).StatusCode);
    }

    [Fact]
    public async Task Photo_upload_rejects_wrong_type_bad_signature_and_oversized_files()
    {
        var hiro = await api.LoginAsync(Accounts.Hiro);
        await AssertFieldError(await PutPhoto(hiro, "me/photo", "%PDF-1.4 test"u8.ToArray(), "application/pdf", "doc.pdf"), "file");
        await AssertFieldError(await PutPhoto(hiro, "me/photo", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], "image/webp", "x.webp"), "file");
        await AssertFieldError(await PutPhoto(hiro, "me/photo", "<script>alert(1)</script>"u8.ToArray(), "image/png", "evil.png"), "file");
        // Right magic number but not a well-formed image.
        await AssertFieldError(await PutPhoto(hiro, "me/photo", [0xFF, 0xD8, 0xFF, 0xE1, 0x7F, 0xFF, 0, 0], "image/jpeg"), "file");
        var big = new byte[5_200_000];
        JpegWithGps().CopyTo(big, 0);
        await AssertFieldError(await PutPhoto(hiro, "me/photo", big, "image/jpeg"), "file");
        Assert.Null((await hiro.Get<CurrentUserDto>("me")).PhotoUrl);
    }

    [Fact]
    public async Task Admin_sets_a_teacher_photo_and_students_see_it_on_their_sessions()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var amira = await api.LoginAsync(Accounts.Amira);
        var r = await PutPhoto(admin, $"teachers/{amira.UserId}/photo", JpegWithGps(), "image/jpeg");
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var teachers = await admin.Get<PagedResult<TeacherDto>>("teachers?pageSize=100");
        var fileId = FileIdOf(teachers.Items.Single(t => t.Id == amira.UserId).PhotoUrl!);

        var aisha = await api.LoginAsync(Accounts.Aisha);
        var sessions = await aisha.Get<List<SessionListItemDto>>($"sessions?fromUtc={DateTime.UtcNow.AddDays(-30):O}&limit=100");
        var amirasClass = sessions.First(s => s.TeacherId == amira.UserId);
        Assert.NotNull(amirasClass.TeacherPhotoUrl);
        Assert.Equal(fileId, FileIdOf(amirasClass.TeacherPhotoUrl!));
        Assert.Equal(HttpStatusCode.OK, (await api.Anonymous().GetAsync(amirasClass.TeacherPhotoUrl)).StatusCode);
        var detail = await aisha.Get<SessionDetailDto>($"sessions/{amirasClass.Id}");
        Assert.Equal(fileId, FileIdOf(detail.TeacherPhotoUrl!));
        var dashboard = await aisha.Get<JsonElement>("dashboard/student");
        Assert.Contains("teacherPhotoUrl", dashboard.GetRawText());
        // Staff photos are visible to anyone at the institution.
        Assert.Equal(HttpStatusCode.OK, (await aisha.GetRaw($"files/{fileId}/link")).StatusCode);

        // The change is audited as an admin action on someone else's profile.
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = amira.UserId.ToString();
        Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "person.photo.updated" && a.EntityId == id && a.UserId == admin.UserId));
    }

    [Fact]
    public async Task Only_admins_of_the_same_institution_can_manage_other_peoples_photos()
    {
        var amira = await api.LoginAsync(Accounts.Amira);
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var priya = await api.LoginAsync(Accounts.Priya);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutPhoto(priya, $"teachers/{amira.UserId}/photo", JpegWithGps(), "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await priya.Http.DeleteAsync($"/api/v1/teachers/{amira.UserId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutPhoto(aisha, $"students/{aisha.UserId}/photo", JpegWithGps(), "image/jpeg")).StatusCode);

        var other = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await PutPhoto(other, $"teachers/{amira.UserId}/photo", JpegWithGps(), "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutPhoto(other, $"students/{aisha.UserId}/photo", JpegWithGps(), "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.DeleteAsync($"/api/v1/teachers/{amira.UserId}/photo")).StatusCode);
    }

    [Fact]
    public async Task Student_photo_is_only_visible_to_the_student_admins_and_their_teachers()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var aisha = await api.LoginAsync(Accounts.Aisha);
        Assert.Equal(HttpStatusCode.NoContent, (await PutPhoto(admin, $"students/{aisha.UserId}/photo", Png(), "image/png", "a.png")).StatusCode);

        var profile = await admin.Get<StudentProfileDto>($"students/{aisha.UserId}");
        Assert.NotNull(profile.Student.PhotoUrl);
        var fileId = FileIdOf(profile.Student.PhotoUrl!);
        Assert.NotNull((await aisha.Get<CurrentUserDto>("me")).PhotoUrl);
        Assert.NotNull((await aisha.Get<StudentProfileDto>($"students/{aisha.UserId}")).Student.PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, (await aisha.GetRaw($"files/{fileId}/link")).StatusCode);

        // Amira teaches Aisha's batch: she sees the photo in her student list and can open it.
        var amira = await api.LoginAsync(Accounts.Amira);
        var amirasStudents = await amira.Get<PagedResult<StudentDto>>("students?pageSize=100");
        Assert.NotNull(amirasStudents.Items.Single(s => s.Id == aisha.UserId).PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, (await amira.GetRaw($"files/{fileId}/link")).StatusCode);

        // Another student and another institution cannot obtain it.
        var ben = await api.LoginAsync(Accounts.Ben);
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetRaw($"files/{fileId}/link")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.OtherTeacher)).GetRaw($"files/{fileId}/link")).StatusCode);

        // Removing clears it.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.DeleteAsync($"/api/v1/students/{aisha.UserId}/photo")).StatusCode);
        Assert.Null((await admin.Get<StudentProfileDto>($"students/{aisha.UserId}")).Student.PhotoUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await aisha.GetRaw($"files/{fileId}/link")).StatusCode);
    }

    [Fact]
    public async Task Avatar_files_cannot_be_uploaded_through_the_generic_file_endpoint()
    {
        var ben = await api.LoginAsync(Accounts.Ben);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(JpegWithGps());
        file.Headers.ContentType = new("image/jpeg");
        form.Add(file, "file", "p.jpg");
        form.Add(new StringContent(nameof(FilePurpose.Avatar)), "purpose");
        Assert.Equal(HttpStatusCode.BadRequest, (await ben.Http.PostAsync("/api/v1/files", form)).StatusCode);
    }
}
