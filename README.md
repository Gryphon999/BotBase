# BotBase 🤖

> SaaS-платформа для создания AI-ботов в Telegram на базе ваших документов

Бизнес загружает файлы со своей информацией (прайс, FAQ, условия работы) — и получает готового Telegram-бота, который отвечает клиентам 24/7 без участия сотрудников.

**Live demo:** https://botbase-production-22ed.up.railway.app

---

## Что умеет платформа

- **Регистрация и авторизация** — JWT, отдельный аккаунт для каждого бизнеса
- **Подключение Telegram-бота** — вставляешь токен от @BotFather, платформа сама регистрирует webhook
- **База знаний** — загрузка PDF, Word (.docx), Excel (.xlsx), TXT-файлов; текст извлекается автоматически
- **AI-ответы** — каждое сообщение клиента обрабатывается Gemini с учётом загруженных документов и истории диалога
- **История разговоров** — все диалоги сохраняются, можно просмотреть через интерфейс
- **Blazor UI** — встроенный веб-интерфейс для управления ботом и знаниями

---

## Технологический стек

| Слой | Технология |
|---|---|
| Backend | ASP.NET Core .NET 10 |
| Frontend | Blazor WebAssembly + MudBlazor |
| База данных | PostgreSQL (EF Core) |
| AI | Google Gemini 3.6 Flash (Interactions API) |
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
│   │                     │  Webhook /          │  │  │
│   │                     │  Conversations      │  │  │
│   │                     └─────────┬──────────┘  │  │
│   │                               │              │  │
│   │                     ┌─────────▼──────────┐  │  │
│   │                     │      Services       │  │  │
│   │                     │  AnthropicService  │  │  │
│   │                     │  TelegramService   │  │  │
│   │                     │  FileParserService │  │  │
│   │                     │  GeminiRateLimiter │  │  │
│   │                     └─────────┬──────────┘  │  │
│   └───────────────────────────────┼──────────────┘  │
│                                   │                  │
│   ┌───────────────────────────────▼──────────────┐  │
│   │           PostgreSQL (Railway)               │  │
│   │  Businesses · KnowledgeChunks ·              │  │
│   │  Conversations · Messages                    │  │
│   └──────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
         │                          │
         ▼                          ▼
  Telegram API              Google Gemini API
  (webhook)                 (chat completions)
```

---

## Как это работает

1. Бизнес регистрируется → создаёт Telegram-бота через @BotFather → вводит токен в BotBase
2. Платформа вызывает `setWebhook` — Telegram начинает присылать сообщения на наш endpoint
3. Бизнес загружает файлы → `FileParserService` извлекает текст → сохраняется в `KnowledgeChunks`
4. Клиент пишет боту → Telegram → `POST /webhook/{businessId}`
5. `WebhookController` собирает системный промпт (база знаний) + историю диалога → отправляет в Gemini
6. Ответ Gemini → отправляется клиенту через Telegram API → сохраняется в БД

---

## Структура проекта

```
BotBase/
├── BotBase.Api/
│   ├── Controllers/
│   │   ├── AuthController.cs        # POST /api/auth/register, /login
│   │   ├── BotsController.cs        # POST /api/bots/setup (регистрирует webhook)
│   │   ├── KnowledgeController.cs   # POST /api/knowledge/upload
│   │   ├── ConversationsController.cs # GET /api/conversations
│   │   └── WebhookController.cs     # POST /webhook/{businessId} ← Telegram сюда шлёт
│   ├── Services/
│   │   ├── AnthropicService.cs      # Клиент Gemini API
│   │   ├── TelegramService.cs       # setWebhook, sendMessage
│   │   ├── FileParserService.cs     # PDF / Word / Excel / TXT парсинг
│   │   └── GeminiRateLimiter.cs    # Sliding window 14 req/min
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   └── Entities/
│   │       ├── Business.cs          # Клиент платформы (email, botToken, businessName)
│   │       ├── KnowledgeChunk.cs    # Загруженный документ (extractedText)
│   │       ├── Conversation.cs      # Диалог (businessId + telegramChatId)
│   │       └── Message.cs           # Сообщение (role: user/assistant, content)
│   └── Program.cs
├── BotBase.BlazorUI/
│   └── Pages/
│       ├── Login.razor
│       ├── Register.razor
│       ├── Dashboard.razor
│       ├── BotSetup.razor
│       ├── Knowledge.razor
│       └── Conversations.razor
└── Dockerfile                       # Multi-stage: BlazorUI → API → запуск
```

---

## Запуск локально

### Требования
- .NET 10 SDK
- Docker (для PostgreSQL) или LocalDB

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

### Переменные окружения (Railway → Variables)

| Переменная | Описание |
|---|---|
| `DATABASE_URL` | Автоматически от Railway PostgreSQL |
| `Anthropic__ApiKey` | API-ключ Gemini (aistudio.google.com) |
| `Jwt__Key` | Секрет для JWT (минимум 32 символа) |
| `WebhookBaseUrl` | URL вашего Railway-сервиса |
| `PORT` | `8080` |

### Деплой

Railway подключён к GitHub. Каждый `git push origin master` запускает автоматический деплой.

```bash
git push origin master
# Railway автоматически пересобирает Docker образ и запускает
```

### Dockerfile (multi-stage)

```
1. Build BlazorUI → wwwroot/
2. Build API       → /app/
3. Copy wwwroot → /app/wwwroot/
4. ENTRYPOINT ["dotnet", "BotBase.Api.dll"]
```

---

## API

| Метод | Endpoint | Описание | Auth |
|---|---|---|---|
| `POST` | `/api/auth/register` | Регистрация бизнеса | — |
| `POST` | `/api/auth/login` | Вход, получение JWT | — |
| `POST` | `/api/bots/setup` | Подключить Telegram-бота | JWT |
| `POST` | `/api/knowledge/upload` | Загрузить файл знаний | JWT |
| `GET` | `/api/conversations` | Список диалогов | JWT |
| `POST` | `/webhook/{businessId}` | Входящие сообщения Telegram | — |

---

## Gemini API

Используется **Interactions API** (OpenAI-совместимый эндпоинт):

```
POST https://generativelanguage.googleapis.com/v1beta/openai/chat/completions
Authorization: Bearer {apiKey}

{
  "model": "gemini-3.6-flash",
  "messages": [
    { "role": "system", "content": "Ты ассистент компании..." },
    { "role": "user",   "content": "Сколько стоит услуга?" }
  ]
}
```

**Rate limiting:** встроенный `GeminiRateLimiter` ограничивает 14 запросов/минуту (sliding window), чтобы не превысить лимит Gemini Free Tier (15 req/min).

---

## Известные ограничения

- **Gemini Free Tier:** 15 req/min, 1 500 req/day — при превышении бот отвечает «Сервис временно недоступен» до сброса лимита (00:00 UTC)
- **Файлы знаний:** текст извлекается целиком, без разбивки на чанки — при очень больших документах промпт может стать слишком длинным
- **История диалога:** берутся последние 10 сообщений (`Take(10)`)
- **Stateless webhook:** каждый запрос загружает историю из БД заново

---

## Лицензия

MIT
