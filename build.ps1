[CmdletBinding(PositionalBinding=$false)]
param(
    [bool] $CreatePackages = $true,
    [bool] $RunTests = $true
)

Write-Host "Run Parameters:" -ForegroundColor Cyan
Write-Host "  CreatePackages: $CreatePackages"
Write-Host "  RunTests: $RunTests"
Write-Host "  dotnet --version:" (dotnet --version)

$packageOutputFolder = "$PSScriptRoot\.nupkgs"
$projectsToBuild = 'StackExchange.NetGain'

foreach ($project in $projectsToBuild) {
    Write-Host "Building $project (dotnet build)..." -ForegroundColor "Magenta"
    dotnet build ".\src\$project\$project.csproj" -c Release /p:CI=true
    Write-Host ""
}

if ($CreatePackages) {
    mkdir -Force $packageOutputFolder | Out-Null
    Write-Host "Clearing existing $packageOutputFolder..." -NoNewline
    Get-ChildItem $packageOutputFolder | Remove-Item
    Write-Host "done." -ForegroundColor "Green"

    Write-Host "Building all packages" -ForegroundColor "Green"

    foreach ($project in $projectsToBuild) {
        Write-Host "Packing $project (dotnet pack)..." -ForegroundColor "Magenta"
        dotnet pack ".\src\$project\$project.csproj" -c Release /p:PackageOutputPath=$packageOutputFolder /p:NoPackageAnalysis=true /p:CI=true
        Write-Host ""
    }
}

Write-Host "Done."