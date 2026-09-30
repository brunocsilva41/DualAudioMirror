@echo off
title DualAudioMirror - Audio em Dois Dispositivos

if exist "%~dp0publish\win-x64\DualAudioMirror.exe" (
    start "" "%~dp0publish\win-x64\DualAudioMirror.exe"
    exit /b 0
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Erro: DualAudioMirror nao encontrado em publish\win-x64 e o dotnet nao esta disponivel.
    echo Gere a publicacao executando scripts\build.ps1 e tente de novo.
    pause
    exit /b 1
)

echo Publicacao nao encontrada; executando pelo codigo-fonte com dotnet run...
dotnet run --project "%~dp0src\DualAudioMirror\DualAudioMirror.csproj"
if errorlevel 1 (
    echo Erro ao executar o DualAudioMirror.
    pause
    exit /b 1
)
exit /b 0
