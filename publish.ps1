param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "RobotFleetSimulation.csproj"
$output = Join-Path $PSScriptRoot "artifacts\publish\$Runtime"

dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $output

Write-Host "Published application to $output"
