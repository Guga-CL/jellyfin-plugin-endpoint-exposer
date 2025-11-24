param(
  [string]$Configuration = "Release",
  [string]$PluginFolderName = "Jellyfin.Plugin.EndpointExposer"
)

stop-Process -Name "jellyfin" -Verbose -ErrorAction SilentlyContinue


$desktop_folder = [Environment]::GetFolderPath("Desktop")
$local_appdata_folder = $env:LOCALAPPDATA
$proj = ".\src\Jellyfin.Plugin.EndpointExposer\Jellyfin.Plugin.EndpointExposer.csproj"
$buildDir = Join-Path -Path (Split-Path $proj -Parent) -ChildPath "bin\$Configuration\net9.0"

if (-not (Test-Path $buildDir)) {
  Write-Error "Build output not found. Run .\build.ps1 first."
  exit 1
}

$targetDir = Join-Path -Path $env:LOCALAPPDATA -ChildPath "jellyfin\plugins\$PluginFolderName"
Write-Host "Deploying to $targetDir"

# Ensure target exists
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# Copy artifacts
Copy-Item -Path (Join-Path $buildDir "Jellyfin.Plugin.EndpointExposer.dll") -Destination $targetDir -Force
Copy-Item -Path (Join-Path $buildDir "Jellyfin.Plugin.EndpointExposer.deps.json") -Destination $targetDir -Force
# Optional: pdb for debugging
if (Test-Path (Join-Path $buildDir "Jellyfin.Plugin.EndpointExposer.pdb")) {
  Copy-Item -Path (Join-Path $buildDir "Jellyfin.Plugin.EndpointExposer.pdb") -Destination $targetDir -Force
}
# Copy meta.json from repo root
if (Test-Path ".\meta.json") {
  Copy-Item -Path ".\meta.json" -Destination $targetDir -Force
}

Write-Host "removing all logs before starting the jellyfin process"
Get-ChildItem "$env:LOCALAPPDATA\jellyfin\log\" | remove-item

Write-Host "Files copied. Restarting Jellyfin service..."
# Restart service (adjust service name if different)
Try {
#   Restart-Service -Name jellyfin -Force -ErrorAction Stop
#   Write-Host "Jellyfin service restarted."
    Start-Process -FilePath pwsh -ArgumentList "-Command & 'C:\Program Files\Jellyfin\Server\jellyfin.exe' --datadir '$ENV:LOCALAPPDATA\jellyfin'"
    Start-Sleep 2
    Write-Host "Jellyfin process started:"
    Get-Process -Name "jellyfin"
} Catch {
    Write-Warning "Could not start Jellyfin service automatically. Please start Jellyfin manually."
}

Write-Host "Waiting 16 seconds to make sure all the logs are fully created"
Start-Sleep 16

Get-ChildItem "$env:LOCALAPPDATA\jellyfin\plugins\Jellyfin.Plugin.EndpointExposer" | Select-Object Name, LastWriteTime

$jellyfin_last_log = Get-item "$env:LOCALAPPDATA\jellyfin\log\log_*.log" | Select-Object -First 1

Set-Content -Path "$desktop_folder\jellyfin_last_log.txt" -Value (get-content $jellyfin_last_log -raw)