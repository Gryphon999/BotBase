# Phase 1 Design: Conversations Detail + Procedures + Work Schedule

**Date:** 2026-08-11  
**Status:** Approved  
**Scope:** Three independent features before the booking system (Phase 2)

---

## Overview

Phase 1 adds three features:
1. **Conversations detail** — click a conversation to read all messages
2. **Procedures** — owner manages the service menu (name, duration, price)
3. **Work schedule** — owner sets working days and hours per weekday

Phase 2 (next session) will use the data from features 2 and 3 to power the AI booking dialog.

---

## Feature 1: Conversations Detail View

### What changes

**Backend — new endpoint:**
```
GET /api/conversations/{id}/messages
Authorization: Bearer {jwt}
Response: [{ role: "user"|"assistant", content: string, createdAt: DateTime }]
```

Returns messages for a conversation that belongs to the authenticated business. Returns 404 if the conversation does not exist or belongs to another business.

**Frontend — Conversations.razor:**
- Table rows become clickable (`OnRowClick`)
- Clicking a row sets `_selectedConversation` and opens a `MudDrawer` anchored to the right
- Drawer loads messages from the new endpoint and renders them as chat bubbles:
  - `user` messages: right-aligned, blue background
  - `assistant` messages: left-aligned, grey background
  - Each bubble shows `createdAt` timestamp below

### Files to change
- `BotBase.Api/Controllers/ConversationsController.cs` — add `GetMessages(Guid id)` action
- `BotBase.BlazorUI/Pages/Conversations.razor` — add drawer + bubble rendering + click handler

---

## Feature 2: Procedures Management

### Database

New entity `Procedure`:

| Column | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| BusinessId | Guid | FK → Businesses |
| Name | string(100) | e.g. "Маникюр" |
| DurationMinutes | int | e.g. 60 |
| Price | decimal | e.g. 2000 |
| IsActive | bool | soft delete flag |

EF migration: `AddProcedures`

### API

```
GET    /api/procedures           → active procedures for current business
POST   /api/procedures           → create { name, durationMinutes, price }
PUT    /api/procedures/{id}      → update { name, durationMinutes, price }
DELETE /api/procedures/{id}      → soft delete (IsActive = false)
```

All endpoints require JWT. Ownership is validated — a business can only access its own procedures.

### Frontend — new page Procedures.razor

- Route: `/procedures`
- Menu item added to layout nav
- Table: Name | Duration | Price | Actions (Edit / Delete)
- «Добавить процедуру» button → opens `MudDialog` with form
- Edit button → same dialog pre-filled
- Delete button → `MudDialog` confirm → soft delete
- `ApiClient` gets: `GetProceduresAsync`, `CreateProcedureAsync`, `UpdateProcedureAsync`, `DeleteProcedureAsync`

### Files to create/change
- `BotBase.Api/Data/Entities/Procedure.cs` — new entity
- `BotBase.Api/Data/AppDbContext.cs` — add `DbSet<Procedure>`
- `BotBase.Api/Controllers/ProceduresController.cs` — new controller
- `BotBase.Api/Migrations/` — new migration
- `BotBase.BlazorUI/Pages/Procedures.razor` — new page
- `BotBase.BlazorUI/Services/ApiClient.cs` — add procedure methods
- `BotBase.BlazorUI/Layout/NavMenu.razor` — add menu item

---

## Feature 3: Work Schedule

### Database

New entity `WorkSchedule`:

| Column | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| BusinessId | Guid | FK → Businesses |
| DayOfWeek | int | 0=Monday … 6=Sunday |
| IsWorkingDay | bool | false = day off |
| StartTime | TimeOnly? | null when IsWorkingDay=false |
| EndTime | TimeOnly? | null when IsWorkingDay=false |

**Seed on business registration:** 7 rows created automatically — Mon–Fri 09:00–18:00 working, Sat–Sun days off.

EF migration: `AddWorkSchedule`

### API

```
GET /api/schedule     → all 7 rows for current business, ordered by DayOfWeek
PUT /api/schedule     → replace all 7 rows at once
                        body: [{ dayOfWeek, isWorkingDay, startTime?, endTime? }]
```

Validation: if `isWorkingDay=true` then `startTime` and `endTime` are required; `startTime` must be before `endTime`.

### Frontend — new page Schedule.razor

- Route: `/schedule`
- Menu item added to layout nav
- Table with 7 fixed rows (Monday–Sunday, Russian names)
- Each row: toggle `IsWorkingDay` (MudSwitch) | MudTimePicker StartTime | MudTimePicker EndTime
- When `IsWorkingDay=false` → time pickers are disabled and greyed out
- «Сохранить» button at bottom → PUT /api/schedule
- Success/error snackbar feedback

### Files to create/change
- `BotBase.Api/Data/Entities/WorkSchedule.cs` — new entity
- `BotBase.Api/Data/AppDbContext.cs` — add `DbSet<WorkSchedule>`
- `BotBase.Api/Controllers/ScheduleController.cs` — new controller
- `BotBase.Api/Migrations/` — new migration
- `BotBase.BlazorUI/Pages/Schedule.razor` — new page
- `BotBase.BlazorUI/Services/ApiClient.cs` — add schedule methods
- `BotBase.BlazorUI/Layout/NavMenu.razor` — add menu item

---

## What Phase 2 will use from this

| Phase 1 output | Used by Phase 2 |
|---|---|
| `Procedure.DurationMinutes` | Slot length calculation |
| `Procedure.Name` | Bot reads procedure list, matches client intent |
| `WorkSchedule` | Defines which slots exist each day |
| Both combined | Free slot search with 10-min buffer between appointments |

---

## Out of scope for Phase 1

- Appointment entity and calendar UI (Phase 2)
- Bot booking dialog (Phase 2)
- Procedure categories or photos
- Client name/phone capture
