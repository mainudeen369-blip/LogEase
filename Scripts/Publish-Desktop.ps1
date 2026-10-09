# Publish LogEase.Desktop as a Windows exe you can put on the Desktop.
# Requires .NET 8 Runtime installed (or use -SelfContained).
param(
    [switch]$SelfContained,
    [string]$OutDir = "$PSScriptRoot\..\publish\LogEase.Desktop"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\LogEase.Desktop\LogEase.Desktop.csproj"

# If the tray app is running, publish may fail locking the exe. Prefer tray → Exit first.
# Do NOT auto-kill here — that looked like the app "closing by itself" during updates.
$running = @(Get-Process -Name "LogEase.Desktop" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Warning "LogEase.Desktop is running (PID: $($running.Id -join ', ')). Close it from the tray (Exit), then run publish again."
}

$dotnetArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $OutDir,
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true"
)

if ($SelfContained) {
    $dotnetArgs += @("--self-contained", "true")
} else {
    $dotnetArgs += @("--self-contained", "false")
}

Write-Host "Publishing to $OutDir ..."
dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $OutDir "LogEase.Desktop.exe"
$ico = Join-Path $OutDir "Assets\logease.ico"
if (-not (Test-Path $ico)) {
    $ico = Join-Path $PSScriptRoot "..\LogEase.Desktop\Assets\logease.ico"
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcut = Join-Path $desktop "LogEase.lnk"

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $OutDir
$lnk.Description = "LogEase Worklog Copilot (system tray)"
if (Test-Path $ico) { $lnk.IconLocation = $ico }
$lnk.Save()

# Also refresh web download assets
$webDl = Join-Path $PSScriptRoot "..\logease-web\public\downloads"
New-Item -ItemType Directory -Force -Path $webDl | Out-Null
Copy-Item -Force $exe (Join-Path $webDl "LogEase.Desktop.exe")
$png = Join-Path $PSScriptRoot "..\LogEase.Desktop\Assets\logease-icon.png"
if (Test-Path $png) { Copy-Item -Force $png (Join-Path $webDl "logease-icon.png") }
if (Test-Path $ico) { Copy-Item -Force $ico (Join-Path $webDl "logease.ico") }

# Also refresh installed copy under %LocalAppData%\LogEase (if present)
$installDir = Join-Path $env:LOCALAPPDATA "LogEase"
$installExe = Join-Path $installDir "LogEase.Desktop.exe"
if (Test-Path $installDir) {
    try {
        Copy-Item -Force $exe $installExe
        Write-Host "  Updated installed copy: $installExe"
    } catch {
        Write-Warning "Could not update installed copy (is LogEase running?): $_"
    }
}

Write-Host ""
Write-Host "Done."
Write-Host "  Exe:      $exe"
Write-Host "  Shortcut: $shortcut"
Write-Host "  Web DL:   $webDl"
Write-Host ""
Write-Host "Note: First run asks Would you like to install? Tray icon appears near the clock."
Write-Host "API must be running: cd LogEase.Api ; dotnet run"
Write-Host "IMPORTANT: Restart LogEase.Api after parser changes so From/To date rules apply."
