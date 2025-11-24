param(
  [string]$Configuration = "Release"
)

$proj = ".\src\Jellyfin.Plugin.EndpointExposer\Jellyfin.Plugin.EndpointExposer.csproj"

if (-not (Test-Path $proj)) {
  Write-Error "Project not found at $proj"
  exit 1
}

Try{

    Write-Host "Building $proj ($Configuration)"
    dotnet build $proj -c $Configuration
    
}Catch{
Write-Error "Build failed. Inspect generated files in $OutDir"
Throw
}

$buildDir = Join-Path -Path (Split-Path $proj -Parent) -ChildPath "bin\$Configuration\net9.0"
Write-Host "Build output: $buildDir"
