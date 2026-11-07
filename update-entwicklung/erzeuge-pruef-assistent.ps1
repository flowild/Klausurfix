param(
    [Parameter(Mandatory)][string]$Paket,
    [Parameter(Mandatory)][ValidateSet('win-arm64', 'win-x64')][string]$Architektur,
    [Parameter(Mandatory)][string]$Ausgabedatei
)

$ErrorActionPreference = 'Stop'
$packagePath = (Resolve-Path -LiteralPath $Paket).Path
$outputPath = [IO.Path]::GetFullPath($Ausgabedatei)
if ([IO.Path]::GetExtension($outputPath) -ne '.exe') { throw 'Die Ausgabedatei muss auf .exe enden.' }
if (Test-Path -LiteralPath $outputPath) { throw 'Die Ausgabedatei existiert bereits.' }
$outputDirectory = Split-Path -Parent $outputPath
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) { throw 'Der Ausgabeordner existiert nicht.' }

$verifier = Join-Path $PSScriptRoot 'Paketpruefung\Paketpruefung.csproj'
$project = Join-Path $PSScriptRoot 'UpdateAssistent\UpdateAssistent.csproj'
& dotnet run --project $verifier -- $packagePath $Architektur
if ($LASTEXITCODE -ne 0) { throw 'Das Paket wurde nicht erstellt, da seine Prüfung fehlgeschlagen ist.' }

$work = Join-Path $outputDirectory ('.klausurfix-update-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    & dotnet publish $project -c Release -r $Architektur --self-contained true -o $work `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:UpdatePackagePath=$packagePath" -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw 'Der Build des Prüf-Assistenten ist fehlgeschlagen.' }
    $builtExe = Join-Path $work 'UpdateAssistent.exe'
    if (-not (Test-Path -LiteralPath $builtExe -PathType Leaf)) { throw 'Die ausführbare Datei fehlt.' }
    [IO.File]::Move($builtExe, $outputPath)
    Write-Output "Update-Assistent erstellt: $outputPath"
    Write-Warning 'Entwicklungsfassung: Diese EXE kann Versionen aktivieren und ist noch nicht für den produktiven Einsatz freigegeben.'
} finally {
    $resolvedWork = (Resolve-Path -LiteralPath $work).Path
    $resolvedParent = (Resolve-Path -LiteralPath $outputDirectory).Path.TrimEnd('\') + '\'
    if ($resolvedWork.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedWork) -match '^\.klausurfix-update-build-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
