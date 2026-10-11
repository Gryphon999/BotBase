# Бэкапы базы BotBase

Бэкап делает GitHub Actions (`.github/workflows/backup.yml`):
- каждую ночь в 05:00 по Еревану;
- при каждом push в `master` — до того, как Railway выкатит новую версию (в Railway включено **Settings → Wait for CI**);
- вручную: GitHub → Actions → Database backup → Run workflow.

Копии хранятся 30 дней во вкладке Actions (артефакты) и **зашифрованы AES-256**: репозиторий публичный, без пароля файл бесполезен.

## Первичная настройка (один раз)
1. Railway → сервис Postgres → Variables → скопировать `DATABASE_PUBLIC_URL`.
2. GitHub → репозиторий → Settings → Secrets and variables → Actions → New repository secret:
   - `BOTBASE_DATABASE_URL` = значение из п.1;
   - `BACKUP_PASSPHRASE` = длинный пароль. **Сохраните его в менеджере паролей**: без него бэкап не расшифровать.
3. Railway → сервис BotBase → Settings → включить **Wait for CI**.
4. GitHub → Actions → Database backup → Run workflow — проверить, что бэкап проходит.

## Восстановление
```bash
gpg --decrypt botbase_YYYYMMDD_HHMMSS.sql.gz.gpg | gunzip > dump.sql
psql "<DATABASE_PUBLIC_URL>" -f dump.sql   # в ПУСТУЮ базу; в рабочую — только после сверки
```

## Правило для новых миграций
Тест `New_migrations_never_drop_or_alter_existing_data` падает, если в новой миграции есть удаление
или изменение таблиц/столбцов. Такую миграцию выкатывать только после свежего бэкапа и ручной проверки.
