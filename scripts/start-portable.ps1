param([switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $packageRoot 'app\Klausurfix.Web.exe'
$appDirectory = Split-Path -Parent $appPath
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
        if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) { throw 'Das Programm wurde nicht gefunden. Bitte das gesamte ZIP-Archiv erneut entpacken.' }
        $backend = Start-Process -FilePath $appPath -WorkingDirectory $appDirectory -WindowStyle Hidden -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        do {
            if (Test-KlausurfixReady) { break }
            $backend.Refresh()
            if ($backend.HasExited) { throw 'Klausurfix konnte nicht gestartet werden. Bitte die passende Version fuer die Windows-Architektur verwenden.' }
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
