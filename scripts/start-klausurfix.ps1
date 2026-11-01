param([switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\Klausurfix.Web\Klausurfix.Web.csproj'
$appUrl = 'http://localhost:5000/'
$healthUrl = 'http://127.0.0.1:5000/api/health'
$backend = $null
$serviceReady = $false

function Test-KlausurfixReady {
    try {
        $request = [System.Net.HttpWebRequest]::Create($healthUrl)
        $request.Proxy = $null
        $request.Timeout = 1000
        $response = $request.GetResponse()
        try {
            $reader = [System.IO.StreamReader]::new($response.GetResponseStream())
            try { return ($reader.ReadToEnd() -eq 'Klausurfix/1') }
            finally { $reader.Dispose() }
        } finally { $response.Dispose() }
    } catch { return $false }
}

function Test-PortOccupied {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connection = $client.BeginConnect([System.Net.IPAddress]::Loopback, 5000, $null, $null)
        if (-not $connection.AsyncWaitHandle.WaitOne(500)) { return $false }
        try { $client.EndConnect($connection); return $true } catch { return $false }
    } finally { $client.Dispose() }
}

$mutex = [System.Threading.Mutex]::new($false, 'Local\KlausurfixStarter-v1')
$held = $false
try {
    try { $held = $mutex.WaitOne([TimeSpan]::FromSeconds(120)) }
    catch [System.Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { throw 'Der Start dauert zu lange. Bitte erneut versuchen.' }

    if (-not (Test-KlausurfixReady)) {
        if (Test-PortOccupied) { throw 'Port 5000 wird bereits von einem anderen Programm verwendet. Bitte dieses Programm beenden und erneut starten.' }
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw 'Die Klausurfix-Projektdatei wurde nicht gefunden.' }
        $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
        if (-not $dotnet) { throw 'Das .NET 10 SDK wurde nicht gefunden. Bitte die Installation anhand der README pruefen.' }
        $installedSdks = @(& $dotnet.Source --list-sdks 2>$null)
        if (-not ($installedSdks | Where-Object { $_ -match '^10\.' })) {
            throw 'Das .NET 10 SDK wurde nicht gefunden. Bitte die Installation anhand der README pruefen.'
        }

        $assetsPath = Join-Path $projectRoot 'src\Klausurfix.Web\obj\project.assets.json'
        $assetsAreCurrent = $false
        if (Test-Path -LiteralPath $assetsPath -PathType Leaf) {
            try {
                $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
                $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
                $assetsAreCurrent = ($assets.project.frameworks.PSObject.Properties.Name -contains 'net10.0') -and
                    $packageFolders.Count -gt 0 -and
                    @($packageFolders | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Container) }).Count -eq 0
            } catch { $assetsAreCurrent = $false }
        }
        if (-not $assetsAreCurrent) {
            $restoreArgs = @('restore', ('"' + $projectPath + '"'), '--ignore-failed-sources')
            $restore = Start-Process -FilePath $dotnet.Source -ArgumentList $restoreArgs -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
            if (-not $restore.WaitForExit(120000)) {
                Stop-Process -Id $restore.Id -ErrorAction SilentlyContinue
                throw 'Die einmalige Einrichtung der Projektabhaengigkeiten dauerte zu lange.'
            }
            if ($restore.ExitCode -ne 0) { throw 'Die Projektabhaengigkeiten konnten nicht eingerichtet werden. Fuer die erstmalige Einrichtung kann eine Internetverbindung erforderlich sein.' }
        }

        $buildArgs = @('build', ('"' + $projectPath + '"'), '--no-restore', '--nologo')
        $build = Start-Process -FilePath $dotnet.Source -ArgumentList $buildArgs -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
        if (-not $build.WaitForExit(120000)) {
            Stop-Process -Id $build.Id -ErrorAction SilentlyContinue
            throw 'Die Vorbereitung von Klausurfix dauerte zu lange.'
        }
        if ($build.ExitCode -ne 0) { throw 'Klausurfix konnte nicht vorbereitet werden. Bitte .NET SDK und Projektabhaengigkeiten pruefen.' }

        # The child is hidden and belongs to the signed-in Windows user, so DPAPI
        # uses the same profile as an interactive launch.
        $webRoot = Split-Path -Parent $projectPath
        $assemblyPath = Join-Path $webRoot 'bin\Debug\net10.0\Klausurfix.Web.dll'
        $backend = Start-Process -FilePath $dotnet.Source -ArgumentList ('"' + $assemblyPath + '"') -WorkingDirectory $webRoot -WindowStyle Hidden -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        do {
            if (Test-KlausurfixReady) { break }
            $backend.Refresh()
            if ($backend.HasExited) { throw 'Klausurfix konnte nicht gestartet werden. Bitte .NET SDK und Projektabhaengigkeiten pruefen.' }
            Start-Sleep -Milliseconds 300
        } while ([DateTime]::UtcNow -lt $deadline)
        if (-not (Test-KlausurfixReady)) { throw 'Klausurfix wurde nicht rechtzeitig bereit. Bitte erneut versuchen.' }
    }

    $serviceReady = $true
    if ($NoBrowser) {
        if ($backend) { Write-Output $backend.Id } else { Write-Output 0 }
    } else {
        $edgeCandidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
            (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
        )
        $edge = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if ($edge) { Start-Process -FilePath $edge -ArgumentList $appUrl }
        else { Start-Process $appUrl }
    }
} catch {
    if ($backend -and -not $serviceReady) {
        $backend.Refresh()
        if (-not $backend.HasExited) { Stop-Process -Id $backend.Id -ErrorAction SilentlyContinue }
    }
    if ($NoBrowser) { [Console]::Error.WriteLine($_.Exception.Message) }
    else {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Klausurfix', 'OK', 'Error') | Out-Null
    }
    exit 1
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
