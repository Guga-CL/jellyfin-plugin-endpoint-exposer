param(
  [string]$Configuration = "Release"
)

$proj = ".\src\Jellyfin.Plugin.EndpointExposer\Jellyfin.Plugin.EndpointExposer.csproj"

if (-not (Test-Path $proj)) {
  Write-Error "Project not found at $proj"
  exit 1
}

Write-Host "Building $proj ($Configuration)"
dotnet build $proj -c $Configuration

$buildDir = Join-Path -Path (Split-Path $proj -Parent) -ChildPath "bin\$Configuration\net9.0"
Write-Host "Build output: $buildDir"
