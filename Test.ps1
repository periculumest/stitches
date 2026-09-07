param([switch]$SkipInstall, [int]$Port = 5068)
$ErrorActionPreference = 'Stop'
$testServer = $null
$oldData = $env:STITCH_DATA_DIR
$oldUrl = $env:STITCH_TEST_URL
Push-Location $PSScriptRoot
try {
    if (-not $SkipInstall) {
        & npm ci --prefix web
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    }
    & npm run build --prefix web
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    $env:STITCH_FIXTURE_PATH = Join-Path $PSScriptRoot 'artifacts/structured-sampler.pdf'
    & dotnet test tests/StitchHelper.Tests.csproj -c Test
    if ($LASTEXITCODE -ne 0) { throw 'Server tests failed.' }
    $testRoot = Join-Path $PSScriptRoot ('artifacts/test-run-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $env:STITCH_DATA_DIR = Join-Path $testRoot 'data'
    $env:STITCH_TEST_URL = "http://127.0.0.1:$Port"
    $serverDll = Join-Path $PSScriptRoot 'server/bin/Test/net8.0/StitchHelper.dll'
    $testServer = Start-Process dotnet -ArgumentList @(('"' + $serverDll + '"'), '--urls', $env:STITCH_TEST_URL) -WorkingDirectory (Join-Path $PSScriptRoot 'server') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'server.log') -RedirectStandardError (Join-Path $testRoot 'server-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($testServer.HasExited) { throw "Test server exited. See $testRoot/server-error.log" }
        try { $health = Invoke-RestMethod "$env:STITCH_TEST_URL/api/health"; if ($health.status -eq 'ok') { $ready = $true; break } } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Test server did not become ready.' }
    & npm test --prefix web
    if ($LASTEXITCODE -ne 0) { throw 'Browser tests failed. See web/test-results.' }
    $before = @(Invoke-RestMethod "$env:STITCH_TEST_URL/api/projects" | ForEach-Object { $_ } | Select-Object id,name,status,completed,total) | ConvertTo-Json -Depth 4 -Compress
    $inventoryBefore = Invoke-RestMethod "$env:STITCH_TEST_URL/api/inventory" | ConvertTo-Json -Depth 4 -Compress
    Stop-Process -Id $testServer.Id
    $testServer.WaitForExit()
    $testServer = Start-Process dotnet -ArgumentList @(('"' + $serverDll + '"'), '--urls', $env:STITCH_TEST_URL) -WorkingDirectory (Join-Path $PSScriptRoot 'server') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'restart.log') -RedirectStandardError (Join-Path $testRoot 'restart-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($testServer.HasExited) { throw 'Restarted test server exited.' }
        try { $health = Invoke-RestMethod "$env:STITCH_TEST_URL/api/health"; if ($health.status -eq 'ok') { $ready = $true; break } } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Restarted test server did not become ready.' }
    $after = @(Invoke-RestMethod "$env:STITCH_TEST_URL/api/projects" | ForEach-Object { $_ } | Select-Object id,name,status,completed,total) | ConvertTo-Json -Depth 4 -Compress
    $inventoryAfter = Invoke-RestMethod "$env:STITCH_TEST_URL/api/inventory" | ConvertTo-Json -Depth 4 -Compress
    if ($before -ne $after -or $inventoryBefore -ne $inventoryAfter) { throw 'State differed after the server process restart.' }
    Write-Host 'Server process restart preserved all project progress and inventory.'
    $archive = Get-ChildItem -LiteralPath (Join-Path $env:STITCH_DATA_DIR 'backups') -Filter 'manual-*.zip' | Sort-Object Name -Descending | Select-Object -First 1
    & dotnet $serverDll --restore $archive.FullName (Join-Path $testRoot 'restored')
    if ($LASTEXITCODE -ne 0) { throw 'CLI backup restore failed.' }
    Write-Host "All tests passed. Screenshots and synthetic PDF: $PSScriptRoot/artifacts"
}
finally {
    if ($testServer -and -not $testServer.HasExited) { Stop-Process -Id $testServer.Id }
    $env:STITCH_DATA_DIR = $oldData
    $env:STITCH_TEST_URL = $oldUrl
    Pop-Location
}
