using Microsoft.AspNetCore.Components;
using StudentSync.Contracts;
using StudentSync.Web.Services;

namespace StudentSync.Web.Components;

/// <summary>Base for feature components embedded in pages (session hub tabs, panels).</summary>
public abstract class AppComponent : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected ToastService Toasts { get; set; } = default!;
    [Parameter, EditorRequired] public CurrentUserDto Me { get; set; } = default!;
    protected string Tz => Me.TimeZoneId;
    protected bool Busy { get; set; }

    protected async Task<(T? Value, ApiException? Error)> Fetch<T>(Func<Task<T>> load)
    {
        try { return (await load(), null); }
        catch (ApiException ex) { return (default, ex); }
    }

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
        catch (ApiException ex) { Toasts.Show(ex.Describe(), ToastKind.Error); return false; }
        finally { Busy = false; StateHasChanged(); }
    }
}
