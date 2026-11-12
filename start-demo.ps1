param([switch]$Stop, [ValidateRange(1024,65535)][int]$WebPort = 3006, [ValidateRange(1024,65535)][int]$ApiPort = 5086)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$local = Join-Path $root '.local'
$config = Join-Path $local 'docker'
$processFile = Join-Path $local 'demo-processes.json'
$settingsFile = Join-Path $local 'demo-settings.json'
$container = 'quedrink-demo-postgres'
New-Item -ItemType Directory -Force -Path $local,$config | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $config 'config.json'))) { '{"auths":{}}' | Set-Content -LiteralPath (Join-Path $config 'config.json') -Encoding ASCII }

if ($Stop) {
    if (Test-Path -LiteralPath $processFile) {
        $saved = Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json
        foreach ($processId in @($saved.api, $saved.web)) {
            $process = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
            if ($process -and $process.CommandLine -and $process.CommandLine.IndexOf($root, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                & taskkill /PID $processId /T /F > $null
            }
        }
        Remove-Item -LiteralPath $processFile
    }
    $exists = & docker --config $config ps -a --filter "name=^/$container$" --format '{{.Names}}'
    if ($exists) {
        $labels = (& docker --config $config inspect --format '{{json .Config.Labels}}' $container) | ConvertFrom-Json
        if ($labels.'quedrink.demo' -eq 'true') { & docker --config $config stop $container > $null }
    }
    Write-Host 'QueDrink demo stopped. Database volume is preserved.'
    return
}
foreach ($port in @($WebPort,$ApiPort)) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw "Port $port is busy. Stop this demo with .\start-demo.ps1 -Stop, or choose other ports." }
}
& docker --config $config info --format '{{.ServerVersion}}' > $null
if ($LASTEXITCODE -ne 0) { throw 'Open Docker Desktop and wait until the engine is ready, then run this script again.' }
$exists = & docker --config $config ps -a --filter "name=^/$container$" --format '{{.Names}}'
if ($exists -and !(Test-Path -LiteralPath $settingsFile)) { throw 'Existing demo database has no matching .local/demo-settings.json. Restore that file; no data has been changed.' }
if (!(Test-Path -LiteralPath $settingsFile)) {
    @{ database = [Guid]::NewGuid().ToString('N'); jwt = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N'); payment = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N') } |
        ConvertTo-Json | Set-Content -LiteralPath $settingsFile -Encoding UTF8
}
$settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
if ($exists) {
    $labels = (& docker --config $config inspect --format '{{json .Config.Labels}}' $container) | ConvertFrom-Json
    if ($labels.'quedrink.demo' -ne 'true') { throw 'The container name is already owned by another task. No container was changed.' }
    & docker --config $config start $container > $null
} else {
    @("POSTGRES_DB=quedrink", "POSTGRES_USER=quedrink", "POSTGRES_PASSWORD=$($settings.database)") | Set-Content -LiteralPath (Join-Path $local 'demo-postgres.env') -Encoding ASCII
    & docker --config $config run -d --name $container --label quedrink.demo=true --env-file (Join-Path $local 'demo-postgres.env') -p '127.0.0.1:55439:5432' --mount 'type=volume,source=quedrink_dotnet_demo_data,target=/var/lib/postgresql/data' postgres:16-alpine > $null
}
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL did not start. Check port 55439 and Docker Desktop.' }
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    & docker --config $config exec $container pg_isready -U quedrink > $null 2>&1
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Milliseconds 500
}
if (!$ready) { throw 'PostgreSQL was not ready within 30 seconds.' }
$apiProject = Join-Path $root 'backend/QueDrink.Api.csproj'
$workspaceCache = Join-Path (Split-Path $root) '.cache/quedrink-nuget'
$env:NUGET_PACKAGES = if (Test-Path -LiteralPath $workspaceCache) { $workspaceCache } else { Join-Path $local 'nuget' }
& dotnet restore $apiProject --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed. Check network connectivity and .NET SDK 10.' }
& dotnet build $apiProject --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'ASP.NET build failed.' }
$frontend = Join-Path $root 'frontend'
if (!(Test-Path -LiteralPath (Join-Path $frontend 'node_modules/next/dist/bin/next'))) {
    Push-Location $frontend
    try { & npm.cmd ci --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' } } finally { Pop-Location }
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$ApiPort"
$env:ConnectionStrings__Database = "Host=127.0.0.1;Port=55439;Database=quedrink;Username=quedrink;Password=$($settings.database)"
$env:JWT_SECRET = $settings.jwt
$env:PAYMENT_CALLBACK_SECRET = $settings.payment
$env:ADMIN_EMAIL = 'admin@quedrink.local'
$env:ADMIN_PASSWORD = 'QueDrink!2026Demo'
$env:AUTH_EXPOSE_RESET_TOKEN = 'true'
$env:DEMO_FRONTEND_URL = "http://127.0.0.1:$WebPort"
if (Test-Path -LiteralPath (Join-Path $local 'integrations.json')) { $env:INTEGRATIONS_FILE = Join-Path $local 'integrations.json' }
$env:API_INTERNAL_URL = "http://127.0.0.1:$ApiPort"
$env:NEXT_PUBLIC_API_URL = '/api/v1'
$env:NEXT_PUBLIC_STORE_NAME = 'Quê Drinks'
$env:Logging__LogLevel__Default = 'Warning'
$apiDll = Join-Path $root 'backend/bin/Debug/net10.0/QueDrink.Api.dll'
$apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$apiDll`"") -WorkingDirectory (Join-Path $root 'backend') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $local 'api.log') -RedirectStandardError (Join-Path $local 'api.error.log')
$nextBin = Join-Path $frontend 'node_modules/next/dist/bin/next'
$webProcess = Start-Process -FilePath 'node' -ArgumentList @("`"$nextBin`"", 'dev', '--hostname', '127.0.0.1', '--port', $WebPort) -WorkingDirectory $frontend -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $local 'web.log') -RedirectStandardError (Join-Path $local 'web.error.log')
@{ api = $apiProcess.Id; web = $webProcess.Id } | ConvertTo-Json | Set-Content -LiteralPath $processFile -Encoding UTF8
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try { $health = Invoke-RestMethod "http://127.0.0.1:$ApiPort/actuator/health" -TimeoutSec 2; if ($health.status -eq 'UP') { $ready = $true; break } } catch { }
    Start-Sleep -Milliseconds 500
}
if (!$ready) { throw "API did not become ready. Check .local/api.log and .local/api.error.log. Stop with .\start-demo.ps1 -Stop." }
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try { $web = Invoke-WebRequest "http://127.0.0.1:$WebPort" -UseBasicParsing -TimeoutSec 2; if ($web.StatusCode -eq 200) { $ready = $true; break } } catch { }
    Start-Sleep -Milliseconds 500
}
if (!$ready) { throw "Web did not become ready. Check .local/web.log and .local/web.error.log. Stop with .\start-demo.ps1 -Stop." }
Write-Host "Open http://127.0.0.1:$WebPort"
Write-Host 'Demo admin: admin@quedrink.local / QueDrink!2026Demo'
$store = (Invoke-RestMethod "http://127.0.0.1:$ApiPort/api/v1/store" -TimeoutSec 5).data
Write-Host "Payment provider: $($store.paymentProvider); mode: $($store.paymentMode). Stop: .\start-demo.ps1 -Stop"

