param(
    [switch]$SkipInstall,
    [int]$Port = 5068,
    [string]$PostgresConnection = $env:STITCH_TEST_POSTGRES
)
$ErrorActionPreference = 'Stop'
if (-not $PostgresConnection) { $PostgresConnection = 'Host=127.0.0.1;Port=5433;Database=postgres;Username=stitch;Password=local-development-only' }
$testServer = $null
$names = @('STITCH_TEST_POSTGRES','STITCH_BROWSER_POSTGRES','STITCH_TEST_URL','STITCH_TEST_AUTH','STITCH_FIXTURE_PATH','ConnectionStrings__StitchHelper','Storage__LocalRoot','ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS','PublicBaseUrl','Storage__Provider')
$savedEnvironment = @{}
foreach ($name in $names) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $PSScriptRoot
try {
    if (-not $SkipInstall) {
        & npm ci --prefix web
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    }
    & npm run build --prefix web
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    $env:STITCH_TEST_POSTGRES = $PostgresConnection
    $env:STITCH_FIXTURE_PATH = Join-Path $PSScriptRoot 'artifacts/structured-sampler.pdf'
    & dotnet test tests/StitchHelper.Tests.csproj -c Test
    if ($LASTEXITCODE -ne 0) { throw 'Server tests failed. PostgreSQL must allow creation of isolated test databases.' }
    $testRoot = Join-Path $PSScriptRoot ('artifacts/test-run-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $connectionBuilder = New-Object System.Data.Common.DbConnectionStringBuilder
    $connectionBuilder.set_ConnectionString($PostgresConnection)
    $connectionBuilder['Database'] = 'stitch_test_browser_' + [Guid]::NewGuid().ToString('N')
    $env:STITCH_BROWSER_POSTGRES = $connectionBuilder.get_ConnectionString()
    $env:ConnectionStrings__StitchHelper = $env:STITCH_BROWSER_POSTGRES
    $env:Storage__LocalRoot = Join-Path $testRoot 'private'
    $env:Storage__Provider = 'Local'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:PublicBaseUrl = $null
    $env:STITCH_TEST_URL = "http://127.0.0.1:$Port"
    $env:ASPNETCORE_URLS = $env:STITCH_TEST_URL
    $env:STITCH_TEST_AUTH = Join-Path $testRoot 'browser-auth.json'
    & dotnet run --project tests/TestSupport -c Test -- $env:STITCH_TEST_AUTH
    if ($LASTEXITCODE -ne 0) { throw 'Browser fixture creation failed.' }
    $serverDll = Join-Path $PSScriptRoot 'server/bin/Test/net8.0/StitchHelper.dll'
    function Start-TestServer([string]$label) {
        $process = Start-Process dotnet -ArgumentList @(('"' + $serverDll + '"')) -WorkingDirectory (Join-Path $PSScriptRoot 'server') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot "$label.log") -RedirectStandardError (Join-Path $testRoot "$label-error.log")
        for ($attempt = 0; $attempt -lt 80; $attempt++) {
            if ($process.HasExited) { throw "Server exited. See $testRoot/$label-error.log" }
            try { if ((Invoke-RestMethod "$env:STITCH_TEST_URL/health/ready").status -eq 'ready') { return $process } } catch { }
            Start-Sleep -Milliseconds 250
        }
        Stop-Process -Id $process.Id
        throw 'Test server did not become ready.'
    }
    $testServer = Start-TestServer 'server'
    & npm test --prefix web
    if ($LASTEXITCODE -ne 0) { throw 'Browser tests failed. See web/test-results.' }
    foreach ($kind in @('daily','weekly')) {
        & dotnet $serverDll --backup $kind
        if ($LASTEXITCODE -ne 0) { throw "$kind backup command failed." }
    }
    $auth = Get-Content -Raw -Encoding UTF8 $env:STITCH_TEST_AUTH | ConvertFrom-Json
    $webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $cookie = New-Object System.Net.Cookie('StitchHelper.Session', $auth.cookies[0].value, '/', ([Uri]$env:STITCH_TEST_URL).Host)
    $webSession.Cookies.Add($cookie)
    $before = Invoke-RestMethod "$env:STITCH_TEST_URL/api/projects" -WebSession $webSession | ConvertTo-Json -Depth 20 -Compress
    $inventoryBefore = Invoke-RestMethod "$env:STITCH_TEST_URL/api/inventory" -WebSession $webSession | ConvertTo-Json -Depth 4 -Compress
    $backups = Invoke-RestMethod "$env:STITCH_TEST_URL/api/backups" -WebSession $webSession
    if ($backups.Count -ne 2) { throw 'Expected one daily and one weekly retained backup.' }
    Stop-Process -Id $testServer.Id; $testServer.WaitForExit()
    $testServer = Start-TestServer 'restart'
    $after = Invoke-RestMethod "$env:STITCH_TEST_URL/api/projects" -WebSession $webSession | ConvertTo-Json -Depth 20 -Compress
    $inventoryAfter = Invoke-RestMethod "$env:STITCH_TEST_URL/api/inventory" -WebSession $webSession | ConvertTo-Json -Depth 4 -Compress
    if ($before -ne $after -or $inventoryBefore -ne $inventoryAfter) { throw 'State differed after server restart.' }
    foreach ($backup in $backups) {
        Invoke-WebRequest "$env:STITCH_TEST_URL/api/backups/$($backup.id)/download" -WebSession $webSession -OutFile (Join-Path $testRoot "$($backup.kind).zip") -UseBasicParsing
    }
    Write-Host "All checks passed, including shared cookies, state, and backup downloads after restart. Artifacts: $testRoot"
    Write-Host 'The isolated stitch_test_browser_* database is retained for inspection; remove it when finished.'
}
finally {
    if ($testServer -and -not $testServer.HasExited) { Stop-Process -Id $testServer.Id }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    Pop-Location
}
