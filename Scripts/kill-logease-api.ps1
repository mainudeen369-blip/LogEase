param(
  [int]$Port = 5055,
  [switch]$Force
)

# Stops LogEase.Api to avoid Windows file-lock errors during `dotnet build`.
# This targets either:
# 1) the process owning the GraphQL port, or
# 2) any process whose name/path includes `LogEase.Api`.

$killed = New-Object System.Collections.Generic.List[int]

function TryStopProcess([int]$procId) {
  if ($procId -le 0) { return }
  try {
    Stop-Process -Id $procId -Force:$Force -ErrorAction Stop
    [void]$killed.Add($procId)
  } catch {
    Write-Host "Warning: could not stop PID $procId. $($_.Exception.Message)"
  }
}

# Port-based stop (preferred)
try {
  $pids =
    @(Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
      Select-Object -ExpandProperty OwningProcess -Unique)
  foreach ($procId in $pids) { TryStopProcess $procId }
} catch {
  # Get-NetTCPConnection may be unavailable on some setups; fall back to process search.
}

# Name/path-based stop (fallback)
try {
  $procs = Get-Process -ErrorAction SilentlyContinue |
    Where-Object {
      $_.ProcessName -eq "LogEase.Api" -or
      ($_.Path -ne $null -and $_.Path -like "*LogEase.Api*") -or
      $_.ProcessName -like "*LogEase.Api*"
    }

  foreach ($p in $procs) { TryStopProcess $p.Id }
} catch {
  Write-Host "Warning: could not enumerate processes. $($_.Exception.Message)"
}

if ($killed.Count -eq 0) {
  Write-Output "No running LogEase.Api process found to kill."
} else {
  Write-Output ("Killed LogEase.Api process(es): " + ($killed -join ", "))
}

