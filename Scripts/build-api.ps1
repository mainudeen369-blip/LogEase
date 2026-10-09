param(
  [switch]$NoRestore,
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Debug"
)

$root = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $root "LogEase.Api"
$csproj = Join-Path $apiDir "LogEase.Api.csproj"
$killScript = Join-Path $PSScriptRoot "kill-logease-api.ps1"

if (!(Test-Path $csproj)) {
  throw "Could not find csproj at: $csproj"
}

if (Test-Path $killScript) {
  & $killScript -Port 5055 -Force:$true
} else {
  Write-Host "Warning: kill script not found, continuing without it."
}

Write-Host "Running: dotnet build `"$csproj`" -c $Configuration $(if($NoRestore){'--no-restore'} )"

if ($NoRestore) {
  & dotnet build $csproj -c $Configuration --no-restore
} else {
  & dotnet build $csproj -c $Configuration
}

