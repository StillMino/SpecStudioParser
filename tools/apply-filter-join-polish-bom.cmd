@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -Command "$script = Join-Path (Get-Location) 'tools\apply-filter-join-polish.ps1'; $text = [System.IO.File]::ReadAllText($script, [System.Text.Encoding]::UTF8); [System.IO.File]::WriteAllText($script, $text, [System.Text.UTF8Encoding]::new($true))"
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -ExecutionPolicy Bypass -File tools\apply-filter-join-polish.ps1
