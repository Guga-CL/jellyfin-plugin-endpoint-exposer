param(
  [string]$Configuration = "Release",
  [string]$PluginFolderName = "Jellyfin.Plugin.EndpointExposer"
)

stop-Process -Name "jellyfin" -Verbose -ErrorAction SilentlyContinue

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

Write-Host "Files copied. Restarting Jellyfin service..."
# Restart service (adjust service name if different)
Try {
#   Restart-Service -Name jellyfin -Force -ErrorAction Stop
#   Write-Host "Jellyfin service restarted."
    start-Process -FilePath pwsh -ArgumentList "-Command & 'C:\Program Files\Jellyfin\Server\jellyfin.exe' --datadir '$ENV:LOCALAPPDATA\jellyfin'"
} Catch {
    Write-Warning "Could not start Jellyfin service automatically. Please start Jellyfin manually."
}
