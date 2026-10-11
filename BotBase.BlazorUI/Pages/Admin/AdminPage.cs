using BotBase.BlazorUI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net;

namespace BotBase.BlazorUI.Pages.Admin;

// Общая база страниц админки: берёт токен администратора и выкидывает на вход, если он истёк.
public abstract class AdminPage : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected async Task<bool> EnsureAdminAsync()
    {
        var token = await JS.InvokeAsync<string?>("localStorage.getItem", "admin_jwt");
        if (string.IsNullOrEmpty(token)) { Nav.NavigateTo("/admin/login"); return false; }
        Api.SetToken(token);
        return true;
    }

    protected async Task<bool> CheckAuthAsync(HttpResponseMessage resp)
    {
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await JS.InvokeVoidAsync("localStorage.removeItem", "admin_jwt");
            Nav.NavigateTo("/admin/login");
            return false;
        }
        return true;
    }
}
