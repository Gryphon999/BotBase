# BotBase 🤖

> SaaS-платформа для создания AI-ботов в Telegram с автоматической записью клиентов

Бизнес загружает файлы со своей информацией (прайс, FAQ, условия работы) — и получает готового Telegram-бота, который отвечает клиентам 24/7, самостоятельно записывает их на процедуры и уведомляет владельца о каждой новой записи.

**Live demo:** https://botbase-production-22ed.up.railway.app

---

## Что умеет платформа

### 🔐 Аккаунт и бот
- **Регистрация и авторизация** — JWT, отдельный аккаунт для каждого бизнеса
- **Подключение Telegram-бота** — вставляешь токен от @BotFather, платформа сама регистрирует webhook
- **Уведомления владельца** — отправь `/start` своему боту → получаешь Telegram-уведомление при каждой новой записи. Кнопка сброса в настройках

### 🧠 AI и база знаний
- **База знаний** — загрузка PDF, Word (.docx), Excel (.xlsx), TXT; текст извлекается автоматически
- **AI-ответы** — каждое сообщение клиента обрабатывается Gemini с учётом документов и истории диалога
- **История разговоров** — все диалоги сохраняются, просматриваются через интерфейс с боковой панелью

### 📅 Запись клиентов
- **Процедуры** — список услуг с длительностью и ценой; бот предлагает только их при записи
- **Расписание** — рабочие дни и часы; бот не запишет на выходной или нерабочее время
- **AI-запись** — бот собирает 4 поля (процедура, дата/время, имя, телефон), проверяет свободные слоты и создаёт запись через маркер `[[BOOK:{...}]]`
- **Валидация на сервере** — проверка обязательных полей, рабочего дня/часов, пересечений с учётом длительности
- **Календарь записей** — месячный вид, клик на день раскрывает детали, подтверждение/отмена

### 🔗 CRM-интеграция
- **Webhook-события** — `appointment.created/confirmed/cancelled` отправляются на URL вашей CRM
- **API Key** — долгоживущий JWT (1 год) для внешних систем
- **Фильтр `updatedSince`** — для синхронизации записей с CRM

---

## Технологический стек

| Слой | Технология |
|---|---|
| Backend | ASP.NET Core .NET 10 |
| Frontend | Blazor WebAssembly + MudBlazor |
| База данных | PostgreSQL (EF Core) |
| AI | Google Gemini Flash (OpenAI-compatible endpoint) |
| Telegram | Telegram.Bot |
| Парсинг PDF | PdfPig |
| Парсинг Word | DocumentFormat.OpenXml |
| Парсинг Excel | ExcelDataReader |
| Хостинг | Railway (Docker) |

---

## Архитектура

```
┌─────────────────────────────────────────────────────┐
│                   Railway (Docker)                   │
│                                                      │
│   ┌──────────────────────────────────────────────┐  │
│   │              ASP.NET Core API                │  │
│   │                                              │  │
│   │  ┌─────────────┐    ┌────────────────────┐  │  │
│   │  │ Blazor WASM │    │    Controllers      │  │  │
│   │  │  (embedded) │    │  Auth / Bots /      │  │  │
│   │  └─────────────┘    │  Knowledge /        │  │  │
│   │                     │  Procedures /       │  │  │
│   │                     │  Schedule /         │  │  │
│   │                     │  Appointments /     │  │  │
│   │                     │  Conversations /    │  │  │
│   │                     │  Integration /      │  │  │
│   │                     │  Webhook            │  │  │
│   │                     └─────────┬──────────┘  │  │
│   │                               │              │  │
│   │                     ┌─────────▼──────────┐  │  │
│   │                     │      Services       │  │  │
│   │                     │  AnthropicService  │  │  │
│   │                     │  TelegramService   │  │  │
│   │                     │  FileParserService │  │  │
│   │                     │  CrmWebhookService │  │  │
│   │                     │  GeminiRateLimiter │  │  │
│   │                     └─────────┬──────────┘  │  │
│   └───────────────────────────────┼──────────────┘  │
│                                   │                  │
│   ┌───────────────────────────────▼──────────────┐  │
│   │           PostgreSQL (Railway)               │  │
│   │  Businesses · KnowledgeChunks ·              │  │
│   │  Conversations · Messages ·                  │  │
│   │  Procedures · WorkSchedules · Appointments   │  │
│   └──────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
         │                          │
         ▼                          ▼
  Telegram API              Google Gemini API
  (webhook)                 (chat completions)
```

---

## Как работает запись через бота

```
Клиент: "Хочу записаться на маникюр"
  ↓
Бот уточняет: процедуру → дату/время → имя → телефон
  ↓
AI генерирует: "Вы записаны! [[BOOK:{"procedure_name":"Маникюр","scheduled_at":"..."}]]"
  ↓
Сервер:
  1. Проверяет рабочий день и часы
  2. Проверяет пересечение с существующими записями (с учётом длительности)
  3. Создаёт Appointment в БД
  4. Отправляет webhook в CRM (если настроен)
  5. Уведомляет владельца в Telegram
  ↓
Клиент получает: "Вы записаны!" (без служебного маркера)
Владелец получает: "📅 Новая запись! Клиент: ..."
```

---

## Структура проекта

```
BotBase/
├── BotBase.Api/
│   ├── Controllers/
│   │   ├── AuthController.cs          # POST /api/auth/register, /login
│   │   ├── BotsController.cs          # Бот + уведомления владельца
│   │   ├── KnowledgeController.cs     # Загрузка файлов базы знаний
│   │   ├── ProceduresController.cs    # CRUD процедур
│   │   ├── ScheduleController.cs      # Рабочее расписание
│   │   ├── AppointmentsController.cs  # Записи (с фильтром по дате/месяцу)
│   │   ├── ConversationsController.cs # История диалогов
│   │   ├── IntegrationController.cs   # Webhook CRM + API Key
│   │   └── WebhookController.cs       # POST /webhook/{businessId} ← Telegram
│   ├── Services/
│   │   ├── AnthropicService.cs        # Клиент Gemini API
│   │   ├── TelegramService.cs         # getMe, setWebhook, sendMessage
│   │   ├── FileParserService.cs       # PDF / Word / Excel / TXT парсинг
│   │   ├── CrmWebhookService.cs       # POST на вебхук CRM при событиях
│   │   └── GeminiRateLimiter.cs       # Sliding window 14 req/min
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   └── Entities/
│   │       ├── Business.cs            # email, botToken, crmWebhookUrl, ownerNotificationChatId
│   │       ├── KnowledgeChunk.cs      # Загруженный документ (extractedText)
│   │       ├── Procedure.cs           # Услуга (name, durationMinutes, price)
│   │       ├── WorkSchedule.cs        # Расписание (dayOfWeek, startTime, endTime)
│   │       ├── Appointment.cs         # Запись (clientName, phone, status, scheduledAt)
│   │       ├── Conversation.cs        # Диалог (businessId + telegramChatId)
│   │       └── Message.cs             # Сообщение (role: user/assistant, content)
│   └── Program.cs
├── BotBase.BlazorUI/
│   └── Pages/
│       ├── Login.razor / Register.razor
│       ├── Dashboard.razor            # Обзор статуса бота
│       ├── BotSetup.razor             # Токен + подключение уведомлений
│       ├── Knowledge.razor            # Загрузка файлов
│       ├── Procedures.razor           # Список процедур
│       ├── Schedule.razor             # Рабочее расписание
│       ├── Appointments.razor         # Календарь записей
│       └── Conversations.razor        # История диалогов
└── Dockerfile                         # Multi-stage: BlazorUI → API → запуск
```

---

## Запуск локально

### Требования
- .NET 10 SDK
- PostgreSQL (Docker или локальный)

### 1. Клонируйте репозиторий

```bash
git clone https://github.com/Gryphon999/BotBase.git
cd BotBase
```

### 2. Настройте переменные окружения

Создайте `BotBase.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=botbase;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "Key": "your-secret-key-minimum-32-characters-long"
  },
  "Anthropic": {
    "ApiKey": "ваш-ключ-с-aistudio.google.com"
  },
  "WebhookBaseUrl": "https://your-ngrok-url.ngrok.io"
}
```

> Для локального тестирования webhook используйте [ngrok](https://ngrok.com): `ngrok http 5000`

### 3. Запустите PostgreSQL

```bash
docker run -d \
  --name botbase-db \
  -e POSTGRES_PASSWORD=yourpassword \
  -e POSTGRES_DB=botbase \
  -p 5432:5432 \
  postgres:16
```

### 4. Примените миграции и запустите

```bash
dotnet run --project BotBase.Api
```

Приложение будет доступно на `http://localhost:5000`.

---

## Деплой на Railway

### Переменные окружения

| Переменная | Описание |
|---|---|
| `DATABASE_URL` | Автоматически от Railway PostgreSQL |
| `Anthropic__ApiKey` | API-ключ Gemini (aistudio.google.com) |
| `Jwt__Key` | Секрет для JWT (минимум 32 символа) |
| `WebhookBaseUrl` | URL вашего Railway-сервиса |
| `PORT` | `8080` |

Railway подключён к GitHub — каждый `git push origin master` запускает автоматический деплой.

---

## API

### Публичные
| Метод | Endpoint | Описание |
|---|---|---|
| `POST` | `/api/auth/register` | Регистрация бизнеса |
| `POST` | `/api/auth/login` | Вход, получение JWT |
| `POST` | `/webhook/{businessId}` | Входящие сообщения Telegram |

### Защищённые (Bearer JWT)
| Метод | Endpoint | Описание |
|---|---|---|
| `GET/POST` | `/api/bots` | Статус бота / подключить бота |
| `DELETE` | `/api/bots/owner-notification` | Сбросить подключение уведомлений |
| `GET/POST/DELETE` | `/api/knowledge` | База знаний |
| `GET/POST/PUT/DELETE` | `/api/procedures` | Процедуры |
| `GET/PUT` | `/api/schedule` | Расписание |
| `GET/POST/PATCH` | `/api/appointments` | Записи клиентов |
| `GET` | `/api/conversations` | Список диалогов |
| `GET/PUT` | `/api/integration/webhook` | CRM webhook URL |
| `GET` | `/api/integration/apikey` | API Key для CRM |

---

## Известные ограничения

- **Gemini Free Tier:** 15 req/min — при превышении бот отвечает «Сервис временно недоступен»
- **База знаний:** текст извлекается целиком, без RAG-чанкинга — большие документы увеличивают промпт
- **История диалога:** берутся последние 10 сообщений
- **Timezone:** записи хранятся в UTC, сравнение с расписанием предполагает что сервер в UTC

---

## Лицензия

MIT
