param(
    [switch]$SkipBuild,
    [string]$DataDirectory = (Join-Path $PSScriptRoot '.data'),
    [int]$Port = 5057
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $env:STITCH_DATA_DIR = [System.IO.Path]::GetFullPath($DataDirectory)
    if (-not $SkipBuild) {
        & npm ci --prefix web
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
        & npm run build --prefix web
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        & dotnet build server -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }
    }
    Write-Host "Open http://127.0.0.1:$Port in your browser. Press Ctrl+C here to stop."
    Write-Host "Your data: $env:STITCH_DATA_DIR"
    & dotnet run --project server -c Release --no-build -- --urls "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'Stitch Helper could not start. Check the message above (the port may already be in use).' }
}
finally { Pop-Location }
