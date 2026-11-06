using Microsoft.AspNetCore.Components;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Web.Services;

namespace StudentSync.Web.Components;

/// <summary>Base for interactive pages: API access, current user, time zone and consistent error/toast handling.</summary>
public abstract class AppPage : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected ToastService Toasts { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected CurrentUserDto? Me { get; private set; }
    protected string Tz => Me?.TimeZoneId ?? "UTC";
    protected bool IsAdmin => Me?.Role == UserRole.InstitutionAdmin;
    protected bool IsTeacher => Me?.Role == UserRole.Teacher;
    protected bool IsStudent => Me?.Role == UserRole.Student;
    protected bool IsParent => Me?.Role == UserRole.Parent;
    protected bool IsStaff => IsAdmin || IsTeacher;
    protected bool Busy { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try { Me = await Api.MeAsync(); }
        catch (ApiException ex) when (ex.Status == 401) { Nav.NavigateTo("/login", forceLoad: true); return; }
        await LoadAsync();
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Loads data into a page slot, capturing API errors for the DataView.</summary>
    protected async Task<(T? Value, ApiException? Error)> Fetch<T>(Func<Task<T>> load)
    {
        try { return (await load(), null); }
        catch (ApiException ex) when (ex.Status == 401) { Nav.NavigateTo("/login", forceLoad: true); return (default, ex); }
        catch (ApiException ex) { return (default, ex); }
    }

    /// <summary>Runs a command with a busy flag; shows a success toast or the API's error message.</summary>
    protected async Task<bool> Run(Func<Task> action, string? success = null)
    {
        if (Busy) return false;
        Busy = true;
        try
        {
            await action();
            if (success is not null) Toasts.Show(success);
            return true;
        }
        catch (ApiException ex) when (ex.Status == 401) { Nav.NavigateTo("/login", forceLoad: true); return false; }
        catch (ApiException ex) { Toasts.Show(ex.Describe(), ToastKind.Error); return false; }
        finally { Busy = false; StateHasChanged(); }
    }
}
