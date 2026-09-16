# BotBase Database Backup Script
# Usage: .\scripts\backup.ps1
# Requires: railway CLI installed and logged in

$ErrorActionPreference = "Stop"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupDir = Join-Path $PSScriptRoot "..\backups"
$backupFile = Join-Path $backupDir "backup_$timestamp.sql.gz"

# Create backups directory if not exists
if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir | Out-Null
}

Write-Host "Starting backup at $timestamp..." -ForegroundColor Cyan

# Run pg_dump inside the Postgres container via railway ssh
# stderr (railway SSH key notice) goes to null, stdout is the SQL
try {
    railway ssh --service Postgres -- pg_dump -U postgres -d railway --no-owner --no-privileges 2>$null | gzip | Set-Content -Path $backupFile -AsByteStream
} catch {
    Write-Host "ERROR: pg_dump failed: $_" -ForegroundColor Red
    exit 1
}

# Verify backup is not empty (should contain CREATE TABLE statements)
$tableCount = (& gunzip -c $backupFile 2>$null | Select-String "^CREATE TABLE" | Measure-Object).Count
if ($tableCount -eq 0) {
    Write-Host "ERROR: Backup appears empty (0 CREATE TABLE statements). Removing invalid file." -ForegroundColor Red
    Remove-Item $backupFile -Force
    exit 1
}

$sizeMB = [math]::Round((Get-Item $backupFile).Length / 1MB, 2)
Write-Host "Backup successful: $backupFile ($sizeMB MB, $tableCount tables)" -ForegroundColor Green

# Delete backups older than 7 days
$cutoff = (Get-Date).AddDays(-7)
$oldFiles = Get-ChildItem $backupDir -Filter "backup_*.sql.gz" | Where-Object { $_.LastWriteTime -lt $cutoff }
if ($oldFiles.Count -gt 0) {
    $oldFiles | Remove-Item -Force
    Write-Host "Deleted $($oldFiles.Count) old backup(s) older than 7 days" -ForegroundColor Yellow
}
