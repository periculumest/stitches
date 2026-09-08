param([switch]$SkipBuild, [switch]$Migrate, [int]$Port = 5057)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    if (-not $SkipBuild) {
        & npm ci --prefix web
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
        & npm run build --prefix web
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        & dotnet build server -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }
    }
    if ($Migrate) {
        & dotnet run --project server -c Release --no-build -- --migrate
        if ($LASTEXITCODE -ne 0) { throw 'Migration failed. Start PostgreSQL and check the connection configuration.' }
    }
    Write-Host "Open http://127.0.0.1:$Port. PostgreSQL and Google OAuth must be configured; see docs/phase 2/PREPROD-READINESS.md."
    & dotnet run --project server -c Release --no-build -- --urls "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'Stitch Helper could not start. Check the configuration and port.' }
}
finally { Pop-Location }
