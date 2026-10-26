@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
set "PROJECT=%ROOT%FirmasPDF\FirmasPDF.csproj"
set "OUTPUT=%ROOT%artifacts\run"
set "APP=%OUTPUT%\FirmasPDF.exe"

if not exist "%PROJECT%" (
    echo ERROR: No se encuentra el proyecto "%PROJECT%".
    goto :fail
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: Instala el SDK de .NET 10 para compilar la aplicacion.
    goto :fail
)

echo Cerrando las instancias anteriores de Firmas PDF...
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference = 'Stop'; $instances = @(Get-Process -Name 'FirmasPDF' -ErrorAction SilentlyContinue); foreach ($instance in $instances) { if (-not $instance.HasExited) { Stop-Process -Id $instance.Id -Force; if (-not $instance.WaitForExit(10000)) { throw 'No se pudo cerrar la instancia anterior.' } } }"
if errorlevel 1 goto :fail

echo Compilando el codigo actual...
dotnet build "%PROJECT%" -c Debug --nologo --output "%OUTPUT%"
if errorlevel 1 goto :fail

if not exist "%APP%" (
    echo ERROR: No se ha generado "%APP%".
    goto :fail
)

echo Abriendo Firmas PDF...
start "" "%APP%" %*
if errorlevel 1 goto :fail
exit /b 0

:fail
echo.
echo No se pudo iniciar la aplicacion. Revisa el error anterior.
pause
exit /b 1
