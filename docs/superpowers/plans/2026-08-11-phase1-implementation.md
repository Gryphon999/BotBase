# Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add conversation detail view, procedures CRUD, and work schedule management to BotBase.

**Architecture:** Three independent vertical slices — each adds entity + EF migration + controller + Blazor page. Migrations auto-apply on Railway via `db.Database.MigrateAsync()` in Program.cs. No tests exist in this project — verify by running the app and checking UI manually.

**Tech Stack:** ASP.NET Core .NET 10, Blazor WASM, MudBlazor 9, PostgreSQL (EF Core + Npgsql), Railway Docker deploy.

## Global Constraints

- All controllers: `[ApiController]`, `[Authorize]`, primary constructor DI, ownership validated via `User.GetBusinessId()`
- All Blazor pages: check JWT from localStorage → redirect to `/login` if missing; call `Api.SetToken(token)` before requests
- Entities: Guid PK, `DateTime CreatedAt = DateTime.UtcNow`
- Migrations run automatically on startup — no manual `dotnet ef database update` needed on Railway
- Deploy: `git push origin master` → Railway auto-redeploys

---

### Task 1: Conversations Detail View

**Files:**
- Modify: `BotBase.Api/Controllers/ConversationsController.cs`
- Modify: `BotBase.BlazorUI/Services/ApiClient.cs`
- Modify: `BotBase.BlazorUI/Pages/Conversations.razor`

**Interfaces:**
- Produces: `GET /api/conversations/{id}/messages` → `[{ role, content, createdAt }]`

- [ ] **Step 1: Add GetMessages endpoint to ConversationsController**

Replace the file content:

```csharp
using BotBase.Api.Data;
using BotBase.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var businessId = User.GetBusinessId();
        var conversations = await db.Conversations
            .Where(c => c.BusinessId == businessId)
            .OrderByDescending(c => c.StartedAt)
            .Select(c => new { c.Id, c.TelegramChatId, c.StartedAt })
            .ToListAsync();
        return Ok(conversations);
    }

    [HttpGet("{id}/messages")]
    public async Task<IActionResult> GetMessages(Guid id)
    {
        var businessId = User.GetBusinessId();
        var conv = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == id && c.BusinessId == businessId);
        if (conv is null) return NotFound();

        var messages = await db.Messages
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content, m.CreatedAt })
            .ToListAsync();
        return Ok(messages);
    }
}
```

- [ ] **Step 2: Add GetMessagesAsync to ApiClient**

Add one line to `BotBase.BlazorUI/Services/ApiClient.cs` after `GetConversationsAsync`:

```csharp
    public Task<HttpResponseMessage> GetMessagesAsync(Guid conversationId) =>
        http.GetAsync($"api/conversations/{conversationId}/messages");
```

- [ ] **Step 3: Rewrite Conversations.razor with drawer**

Replace the entire file:

```razor
@page "/conversations"
@inject ApiClient Api
@inject IJSRuntime JS
@inject NavigationManager Nav

<MudText Typo="Typo.h4" Class="mb-4">Разговоры</MudText>

@if (_conversations is null)
{
    <MudProgressLinear Indeterminate="true" />
}
else if (_conversations.Count == 0)
{
    <MudAlert Severity="Severity.Info">
        Диалогов пока нет. Клиенты начнут писать боту — разговоры появятся здесь.
    </MudAlert>
}
else
{
    <MudTable T="ConversationResponse" Items="_conversations" Hover="true"
              OnRowClick="OpenDrawer" Style="cursor:pointer">
        <HeaderContent>
            <MudTh>Telegram Chat ID</MudTh>
            <MudTh>Начало диалога</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>@context.TelegramChatId</MudTd>
            <MudTd>@context.StartedAt.ToString("dd.MM.yyyy HH:mm")</MudTd>
        </RowTemplate>
    </MudTable>
}

<MudDrawer @bind-Open="_drawerOpen" Anchor="Anchor.End" Elevation="2"
           Variant="DrawerVariant.Temporary" Width="420px">
    <MudDrawerHeader>
        <MudText Typo="Typo.h6">Диалог с клиентом @_selected?.TelegramChatId</MudText>
    </MudDrawerHeader>
    <div style="padding:16px; overflow-y:auto; height:calc(100% - 64px); display:flex; flex-direction:column; gap:8px;">
        @if (_messages is null)
        {
            <MudProgressLinear Indeterminate="true" />
        }
        else if (_messages.Count == 0)
        {
            <MudText>Сообщений нет.</MudText>
        }
        else
        {
            @foreach (var msg in _messages)
            {
                var isUser = msg.Role == "user";
                <div style="display:flex; justify-content:@(isUser ? "flex-end" : "flex-start");">
                    <div style="max-width:80%; background:@(isUser ? "#1976d2" : "#f5f5f5");
                                color:@(isUser ? "white" : "#333"); padding:10px 14px;
                                border-radius:@(isUser ? "16px 16px 4px 16px" : "16px 16px 16px 4px");
                                font-size:14px;">
                        <div>@msg.Content</div>
                        <div style="font-size:11px; opacity:0.65; margin-top:4px; text-align:right;">
                            @msg.CreatedAt.ToLocalTime().ToString("HH:mm")
                        </div>
                    </div>
                </div>
            }
        }
    </div>
</MudDrawer>

@code {
    List<ConversationResponse>? _conversations;
    List<MessageResponse>? _messages;
    ConversationResponse? _selected;
    bool _drawerOpen;

    protected override async Task OnInitializedAsync()
    {
        var token = await JS.InvokeAsync<string?>("localStorage.getItem", "jwt");
        if (token is null) { Nav.NavigateTo("/login"); return; }
        Api.SetToken(token);

        var resp = await Api.GetConversationsAsync();
        if (resp.IsSuccessStatusCode)
            _conversations = await resp.Content.ReadFromJsonAsync<List<ConversationResponse>>();
        else
            _conversations = [];
    }

    async Task OpenDrawer(TableRowClickEventArgs<ConversationResponse> args)
    {
        _selected = args.Item;
        _messages = null;
        _drawerOpen = true;

        var resp = await Api.GetMessagesAsync(_selected.Id);
        if (resp.IsSuccessStatusCode)
            _messages = await resp.Content.ReadFromJsonAsync<List<MessageResponse>>();
        else
            _messages = [];
    }

    record ConversationResponse(Guid Id, long TelegramChatId, DateTime StartedAt);
    record MessageResponse(string Role, string Content, DateTime CreatedAt);
}
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build BotBase.Api
dotnet build BotBase.BlazorUI
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
cd C:\Users\user\source\repos\BotBase
git add BotBase.Api/Controllers/ConversationsController.cs
git add BotBase.BlazorUI/Services/ApiClient.cs
git add BotBase.BlazorUI/Pages/Conversations.razor
git commit -m "feat: conversations detail view with message drawer"
```

---

### Task 2: Procedure Entity + Backend

**Files:**
- Create: `BotBase.Api/Data/Entities/Procedure.cs`
- Modify: `BotBase.Api/Data/AppDbContext.cs`
- Create: `BotBase.Api/Controllers/ProceduresController.cs`
- Create: migration `AddProcedures`

**Interfaces:**
- Produces:
  - `GET /api/procedures` → `[{ id, name, durationMinutes, price }]`
  - `POST /api/procedures` body: `{ name, durationMinutes, price }`
  - `PUT /api/procedures/{id}` body: `{ name, durationMinutes, price }`
  - `DELETE /api/procedures/{id}` → soft delete

- [ ] **Step 1: Create Procedure entity**

Create `BotBase.Api/Data/Entities/Procedure.cs`:

```csharp
namespace BotBase.Api.Data.Entities;

public class Procedure
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
```

- [ ] **Step 2: Add DbSet to AppDbContext**

Replace `BotBase.Api/Data/AppDbContext.cs`:

```csharp
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
}
```

- [ ] **Step 3: Add EF migration**

```bash
cd C:\Users\user\source\repos\BotBase
dotnet ef migrations add AddProcedures --project BotBase.Api --startup-project BotBase.Api
```

Expected: new file `BotBase.Api/Migrations/..._AddProcedures.cs` created.

- [ ] **Step 4: Create ProceduresController**

Create `BotBase.Api/Controllers/ProceduresController.cs`:

```csharp
using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProceduresController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var businessId = User.GetBusinessId();
        var procedures = await db.Procedures
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.DurationMinutes, p.Price })
            .ToListAsync();
        return Ok(procedures);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProcedureRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.DurationMinutes <= 0 || req.Price < 0)
            return BadRequest(new { error = "Проверьте данные: название обязательно, длительность > 0, цена >= 0" });

        var procedure = new Procedure
        {
            Id = Guid.NewGuid(),
            BusinessId = User.GetBusinessId(),
            Name = req.Name.Trim(),
            DurationMinutes = req.DurationMinutes,
            Price = req.Price
        };
        db.Procedures.Add(procedure);
        await db.SaveChangesAsync();
        return Ok(new { procedure.Id, procedure.Name, procedure.DurationMinutes, procedure.Price });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, ProcedureRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.DurationMinutes <= 0 || req.Price < 0)
            return BadRequest(new { error = "Проверьте данные: название обязательно, длительность > 0, цена >= 0" });

        var businessId = User.GetBusinessId();
        var procedure = await db.Procedures
            .FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == businessId && p.IsActive);
        if (procedure is null) return NotFound();

        procedure.Name = req.Name.Trim();
        procedure.DurationMinutes = req.DurationMinutes;
        procedure.Price = req.Price;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = User.GetBusinessId();
        var procedure = await db.Procedures
            .FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == businessId && p.IsActive);
        if (procedure is null) return NotFound();

        procedure.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    record ProcedureRequest(string Name, int DurationMinutes, decimal Price);
}
```

- [ ] **Step 5: Build**

```bash
dotnet build BotBase.Api
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add BotBase.Api/Data/Entities/Procedure.cs
git add BotBase.Api/Data/AppDbContext.cs
git add BotBase.Api/Controllers/ProceduresController.cs
git add BotBase.Api/Migrations/
git commit -m "feat: Procedure entity + CRUD API"
```

---

### Task 3: Procedures Blazor Page

**Files:**
- Create: `BotBase.BlazorUI/Pages/Procedures.razor`
- Modify: `BotBase.BlazorUI/Services/ApiClient.cs`
- Modify: `BotBase.BlazorUI/Layout/NavMenu.razor`

**Interfaces:**
- Consumes: `GET/POST/PUT/DELETE /api/procedures` from Task 2

- [ ] **Step 1: Add ApiClient methods for procedures**

Add to `BotBase.BlazorUI/Services/ApiClient.cs` after `GetMessagesAsync`:

```csharp
    public Task<HttpResponseMessage> GetProceduresAsync() =>
        http.GetAsync("api/procedures");

    public Task<HttpResponseMessage> CreateProcedureAsync(string name, int durationMinutes, decimal price) =>
        http.PostAsJsonAsync("api/procedures", new { name, durationMinutes, price });

    public Task<HttpResponseMessage> UpdateProcedureAsync(Guid id, string name, int durationMinutes, decimal price) =>
        http.PutAsJsonAsync($"api/procedures/{id}", new { name, durationMinutes, price });

    public Task<HttpResponseMessage> DeleteProcedureAsync(Guid id) =>
        http.DeleteAsync($"api/procedures/{id}");
```

- [ ] **Step 2: Create Procedures.razor**

Create `BotBase.BlazorUI/Pages/Procedures.razor`:

```razor
@page "/procedures"
@inject ApiClient Api
@inject IJSRuntime JS
@inject NavigationManager Nav

<MudText Typo="Typo.h4" Class="mb-4">Процедуры</MudText>

@if (_procedures is null)
{
    <MudProgressLinear Indeterminate="true" />
}
else
{
    <MudButton Variant="Variant.Filled" Color="Color.Primary" Class="mb-4"
               OnClick="ShowAddForm">+ Добавить процедуру</MudButton>

    @if (_showForm)
    {
        <MudCard Class="mb-4" Elevation="2">
            <MudCardContent>
                <MudText Typo="Typo.h6" Class="mb-3">@(_editId is null ? "Новая процедура" : "Редактировать")</MudText>
                <MudTextField @bind-Value="_formName" Label="Название" Required="true" Class="mb-3" />
                <MudNumericField @bind-Value="_formDuration" Label="Длительность (минуты)" Min="1" Class="mb-3" />
                <MudNumericField @bind-Value="_formPrice" Label="Цена (₽)" Min="0" Format="F0" Class="mb-3" />
                @if (_formError is not null)
                {
                    <MudAlert Severity="Severity.Error" Class="mb-2">@_formError</MudAlert>
                }
            </MudCardContent>
            <MudCardActions>
                <MudButton Variant="Variant.Filled" Color="Color.Primary"
                           OnClick="SaveProcedure" Disabled="_saving">Сохранить</MudButton>
                <MudButton Variant="Variant.Text" OnClick="CancelForm" Class="ml-2">Отмена</MudButton>
            </MudCardActions>
        </MudCard>
    }

    @if (_procedures.Count == 0)
    {
        <MudAlert Severity="Severity.Info">Процедур пока нет. Добавьте первую.</MudAlert>
    }
    else
    {
        <MudTable Items="_procedures" Hover="true">
            <HeaderContent>
                <MudTh>Название</MudTh>
                <MudTh>Длительность</MudTh>
                <MudTh>Цена</MudTh>
                <MudTh></MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd>@context.Name</MudTd>
                <MudTd>@context.DurationMinutes мин</MudTd>
                <MudTd>@context.Price.ToString("F0") ₽</MudTd>
                <MudTd>
                    <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                                   OnClick="() => ShowEditForm(context)" />
                    <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small"
                                   Color="Color.Error" OnClick="() => DeleteProcedure(context.Id)" />
                </MudTd>
            </RowTemplate>
        </MudTable>
    }
}

@code {
    List<ProcedureDto>? _procedures;
    bool _showForm;
    bool _saving;
    Guid? _editId;
    string _formName = "";
    int _formDuration = 60;
    decimal _formPrice = 0;
    string? _formError;

    protected override async Task OnInitializedAsync()
    {
        var token = await JS.InvokeAsync<string?>("localStorage.getItem", "jwt");
        if (token is null) { Nav.NavigateTo("/login"); return; }
        Api.SetToken(token);
        await LoadProcedures();
    }

    async Task LoadProcedures()
    {
        var resp = await Api.GetProceduresAsync();
        _procedures = resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<List<ProcedureDto>>()
            : [];
    }

    void ShowAddForm()
    {
        _editId = null;
        _formName = "";
        _formDuration = 60;
        _formPrice = 0;
        _formError = null;
        _showForm = true;
    }

    void ShowEditForm(ProcedureDto p)
    {
        _editId = p.Id;
        _formName = p.Name;
        _formDuration = p.DurationMinutes;
        _formPrice = p.Price;
        _formError = null;
        _showForm = true;
    }

    void CancelForm() => _showForm = false;

    async Task SaveProcedure()
    {
        _formError = null;
        _saving = true;
        HttpResponseMessage resp;

        if (_editId is null)
            resp = await Api.CreateProcedureAsync(_formName, _formDuration, _formPrice);
        else
            resp = await Api.UpdateProcedureAsync(_editId.Value, _formName, _formDuration, _formPrice);

        _saving = false;

        if (resp.IsSuccessStatusCode)
        {
            _showForm = false;
            await LoadProcedures();
        }
        else
        {
            _formError = "Ошибка сохранения. Проверьте данные.";
        }
    }

    async Task DeleteProcedure(Guid id)
    {
        await Api.DeleteProcedureAsync(id);
        await LoadProcedures();
    }

    record ProcedureDto(Guid Id, string Name, int DurationMinutes, decimal Price);
}
```

- [ ] **Step 3: Update NavMenu**

Replace `BotBase.BlazorUI/Layout/NavMenu.razor`:

```razor
<div class="top-row ps-3 navbar navbar-dark">
    <div class="container-fluid">
        <a class="navbar-brand" href="">BotBase</a>
        <button title="Navigation menu" class="navbar-toggler" @onclick="ToggleNavMenu">
            <span class="navbar-toggler-icon"></span>
        </button>
    </div>
</div>

<div class="@NavMenuCssClass nav-scrollable" @onclick="ToggleNavMenu">
    <nav class="nav flex-column">
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="dashboard" Match="NavLinkMatch.All">
                🏠 Дашборд
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="bot-setup">
                🤖 Настройка бота
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="knowledge">
                📂 База знаний
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="procedures">
                💅 Процедуры
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="schedule">
                📅 Расписание
            </NavLink>
        </div>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="conversations">
                💬 Разговоры
            </NavLink>
        </div>
    </nav>
</div>

@code {
    private bool collapseNavMenu = true;
    private string? NavMenuCssClass => collapseNavMenu ? "collapse" : null;
    private void ToggleNavMenu() => collapseNavMenu = !collapseNavMenu;
}
```

- [ ] **Step 4: Build**

```bash
dotnet build BotBase.BlazorUI
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BotBase.BlazorUI/Pages/Procedures.razor
git add BotBase.BlazorUI/Services/ApiClient.cs
git add BotBase.BlazorUI/Layout/NavMenu.razor
git commit -m "feat: Procedures page with add/edit/delete"
```

---

### Task 4: WorkSchedule Entity + Backend

**Files:**
- Create: `BotBase.Api/Data/Entities/WorkSchedule.cs`
- Modify: `BotBase.Api/Data/AppDbContext.cs`
- Create: `BotBase.Api/Controllers/ScheduleController.cs`
- Modify: `BotBase.Api/Services/AuthService.cs`
- Create: migration `AddWorkSchedule`

**Interfaces:**
- Produces:
  - `GET /api/schedule` → `[{ dayOfWeek, isWorkingDay, startTime, endTime }]` (times as "HH:mm" strings)
  - `PUT /api/schedule` body: `[{ dayOfWeek, isWorkingDay, startTime?, endTime? }]`

- [ ] **Step 1: Create WorkSchedule entity**

Create `BotBase.Api/Data/Entities/WorkSchedule.cs`:

```csharp
namespace BotBase.Api.Data.Entities;

public class WorkSchedule
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public int DayOfWeek { get; set; }  // 0=Monday, 6=Sunday
    public bool IsWorkingDay { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public Business Business { get; set; } = null!;
}
```

- [ ] **Step 2: Add DbSet and navigation to AppDbContext**

Replace `BotBase.Api/Data/AppDbContext.cs`:

```csharp
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
}
```

- [ ] **Step 3: Add EF migration**

```bash
cd C:\Users\user\source\repos\BotBase
dotnet ef migrations add AddWorkSchedule --project BotBase.Api --startup-project BotBase.Api
```

Expected: new migration file created.

- [ ] **Step 4: Create ScheduleController**

Create `BotBase.Api/Controllers/ScheduleController.cs`:

```csharp
using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ScheduleController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var businessId = User.GetBusinessId();
        var schedule = await db.WorkSchedules
            .Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.DayOfWeek)
            .Select(s => new
            {
                s.DayOfWeek,
                s.IsWorkingDay,
                StartTime = s.StartTime.HasValue ? s.StartTime.Value.ToString("HH:mm") : null,
                EndTime = s.EndTime.HasValue ? s.EndTime.Value.ToString("HH:mm") : null
            })
            .ToListAsync();
        return Ok(schedule);
    }

    [HttpPut]
    public async Task<IActionResult> Update(List<ScheduleRowRequest> rows)
    {
        if (rows.Count != 7)
            return BadRequest(new { error = "Должно быть ровно 7 строк (Пн–Вс)" });

        foreach (var row in rows.Where(r => r.IsWorkingDay))
        {
            if (string.IsNullOrEmpty(row.StartTime) || string.IsNullOrEmpty(row.EndTime))
                return BadRequest(new { error = "Для рабочего дня укажите время начала и окончания" });
            if (!TimeOnly.TryParse(row.StartTime, out var start) || !TimeOnly.TryParse(row.EndTime, out var end))
                return BadRequest(new { error = "Неверный формат времени. Используйте HH:mm" });
            if (start >= end)
                return BadRequest(new { error = "Время начала должно быть меньше времени окончания" });
        }

        var businessId = User.GetBusinessId();
        var existing = await db.WorkSchedules
            .Where(s => s.BusinessId == businessId)
            .ToListAsync();
        db.WorkSchedules.RemoveRange(existing);

        var newRows = rows.Select(r =>
        {
            TimeOnly.TryParse(r.StartTime, out var start);
            TimeOnly.TryParse(r.EndTime, out var end);
            return new WorkSchedule
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                DayOfWeek = r.DayOfWeek,
                IsWorkingDay = r.IsWorkingDay,
                StartTime = r.IsWorkingDay ? start : null,
                EndTime = r.IsWorkingDay ? end : null
            };
        });
        db.WorkSchedules.AddRange(newRows);
        await db.SaveChangesAsync();
        return NoContent();
    }

    record ScheduleRowRequest(int DayOfWeek, bool IsWorkingDay, string? StartTime, string? EndTime);
}
```

- [ ] **Step 5: Seed WorkSchedule on registration**

Replace `BotBase.Api/Services/AuthService.cs`:

```csharp
using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BotBase.Api.Services;

public class AuthService(AppDbContext db, IConfiguration config)
{
    public async Task<Business?> RegisterAsync(string email, string password, string businessName)
    {
        if (await db.Businesses.AnyAsync(b => b.Email == email.ToLower()))
            return null;

        var business = new Business
        {
            Id = Guid.NewGuid(),
            Email = email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            BusinessName = businessName
        };
        db.Businesses.Add(business);

        var defaultSchedule = Enumerable.Range(0, 7).Select(day => new WorkSchedule
        {
            Id = Guid.NewGuid(),
            BusinessId = business.Id,
            DayOfWeek = day,
            IsWorkingDay = day < 5,
            StartTime = day < 5 ? new TimeOnly(9, 0) : null,
            EndTime = day < 5 ? new TimeOnly(18, 0) : null
        });
        db.WorkSchedules.AddRange(defaultSchedule);

        await db.SaveChangesAsync();
        return business;
    }

    public async Task<Business?> LoginAsync(string email, string password)
    {
        var business = await db.Businesses
            .FirstOrDefaultAsync(b => b.Email == email.ToLower());
        if (business is null) return null;
        return BCrypt.Net.BCrypt.Verify(password, business.PasswordHash) ? business : null;
    }

    public string GenerateJwt(Business business)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, business.Id.ToString()),
            new Claim(ClaimTypes.Email, business.Email)
        };
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 6: Build**

```bash
dotnet build BotBase.Api
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add BotBase.Api/Data/Entities/WorkSchedule.cs
git add BotBase.Api/Data/AppDbContext.cs
git add BotBase.Api/Controllers/ScheduleController.cs
git add BotBase.Api/Services/AuthService.cs
git add BotBase.Api/Migrations/
git commit -m "feat: WorkSchedule entity + schedule API + seed on registration"
```

---

### Task 5: Schedule Blazor Page + Deploy

**Files:**
- Create: `BotBase.BlazorUI/Pages/Schedule.razor`
- Modify: `BotBase.BlazorUI/Services/ApiClient.cs`

**Interfaces:**
- Consumes: `GET/PUT /api/schedule` from Task 4

- [ ] **Step 1: Add ApiClient methods for schedule**

Add to `BotBase.BlazorUI/Services/ApiClient.cs`:

```csharp
    public Task<HttpResponseMessage> GetScheduleAsync() =>
        http.GetAsync("api/schedule");

    public Task<HttpResponseMessage> SaveScheduleAsync(object rows) =>
        http.PutAsJsonAsync("api/schedule", rows);
```

- [ ] **Step 2: Create Schedule.razor**

Create `BotBase.BlazorUI/Pages/Schedule.razor`:

```razor
@page "/schedule"
@inject ApiClient Api
@inject IJSRuntime JS
@inject NavigationManager Nav

<MudText Typo="Typo.h4" Class="mb-4">Рабочее расписание</MudText>

@if (_rows is null)
{
    <MudProgressLinear Indeterminate="true" />
}
else
{
    <MudTable Items="_rows" Hover="false" Elevation="1">
        <HeaderContent>
            <MudTh>День</MudTh>
            <MudTh>Рабочий день</MudTh>
            <MudTh>Начало</MudTh>
            <MudTh>Конец</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>@DayName(context.DayOfWeek)</MudTd>
            <MudTd>
                <MudSwitch @bind-Value="context.IsWorkingDay" Color="Color.Primary" />
            </MudTd>
            <MudTd>
                <MudTimePicker Time="context.StartTime"
                               TimeChanged="t => context.StartTime = t"
                               Disabled="!context.IsWorkingDay"
                               AmPm="false" />
            </MudTd>
            <MudTd>
                <MudTimePicker Time="context.EndTime"
                               TimeChanged="t => context.EndTime = t"
                               Disabled="!context.IsWorkingDay"
                               AmPm="false" />
            </MudTd>
        </RowTemplate>
    </MudTable>

    @if (_saveError is not null)
    {
        <MudAlert Severity="Severity.Error" Class="mt-3">@_saveError</MudAlert>
    }
    @if (_saved)
    {
        <MudAlert Severity="Severity.Success" Class="mt-3">Расписание сохранено!</MudAlert>
    }

    <MudButton Variant="Variant.Filled" Color="Color.Primary"
               Class="mt-4" OnClick="Save" Disabled="_saving">
        Сохранить расписание
    </MudButton>
}

@code {
    List<ScheduleRow>? _rows;
    bool _saving;
    bool _saved;
    string? _saveError;

    static string DayName(int day) => day switch
    {
        0 => "Понедельник",
        1 => "Вторник",
        2 => "Среда",
        3 => "Четверг",
        4 => "Пятница",
        5 => "Суббота",
        6 => "Воскресенье",
        _ => ""
    };

    protected override async Task OnInitializedAsync()
    {
        var token = await JS.InvokeAsync<string?>("localStorage.getItem", "jwt");
        if (token is null) { Nav.NavigateTo("/login"); return; }
        Api.SetToken(token);

        var resp = await Api.GetScheduleAsync();
        if (!resp.IsSuccessStatusCode) { _rows = []; return; }

        var dtos = await resp.Content.ReadFromJsonAsync<List<ScheduleDto>>();
        _rows = dtos?.Select(d => new ScheduleRow
        {
            DayOfWeek = d.DayOfWeek,
            IsWorkingDay = d.IsWorkingDay,
            StartTime = d.StartTime is not null ? TimeSpan.Parse(d.StartTime) : null,
            EndTime = d.EndTime is not null ? TimeSpan.Parse(d.EndTime) : null
        }).ToList() ?? [];
    }

    async Task Save()
    {
        _saving = true;
        _saved = false;
        _saveError = null;

        var payload = _rows!.Select(r => new
        {
            r.DayOfWeek,
            r.IsWorkingDay,
            StartTime = r.IsWorkingDay && r.StartTime.HasValue
                ? $"{r.StartTime.Value.Hours:D2}:{r.StartTime.Value.Minutes:D2}" : (string?)null,
            EndTime = r.IsWorkingDay && r.EndTime.HasValue
                ? $"{r.EndTime.Value.Hours:D2}:{r.EndTime.Value.Minutes:D2}" : (string?)null
        }).ToList();

        var resp = await Api.SaveScheduleAsync(payload);
        _saving = false;

        if (resp.IsSuccessStatusCode)
            _saved = true;
        else
            _saveError = "Ошибка сохранения. Проверьте что для рабочих дней указано время.";
    }

    class ScheduleRow
    {
        public int DayOfWeek { get; set; }
        public bool IsWorkingDay { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
    }

    record ScheduleDto(int DayOfWeek, bool IsWorkingDay, string? StartTime, string? EndTime);
}
```

- [ ] **Step 3: Build both projects**

```bash
dotnet build BotBase.Api
dotnet build BotBase.BlazorUI
```

Expected: 0 errors in both.

- [ ] **Step 4: Commit and push to deploy**

```bash
git add BotBase.BlazorUI/Pages/Schedule.razor
git add BotBase.BlazorUI/Services/ApiClient.cs
git commit -m "feat: Schedule page with working days and hours"
git push origin master
```

Expected: Railway picks up the push, builds Docker image (~3 min), applies migrations automatically, starts new container.

- [ ] **Step 5: Smoke test on production**

1. Open https://botbase-production-22ed.up.railway.app
2. Login → check NavMenu has: Дашборд, Настройка бота, База знаний, Процедуры, Расписание, Разговоры
3. Процедуры → добавить «Маникюр, 60 мин, 2000₽» → убедиться что появился в таблице
4. Расписание → снять галочку с Субботы → Сохранить → перезагрузить страницу → галочка снята
5. Разговоры → кликнуть строку → открылся drawer с сообщениями

Phase 1 complete. Phase 2 (calendar + booking bot) — следующая сессия.
