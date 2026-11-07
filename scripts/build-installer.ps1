param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$PackageVersion,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Suffix
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path (Join-Path $projectRoot 'release') $PackageVersion
if (-not (Test-Path -LiteralPath $outputRoot -PathType Container)) { throw 'Der Release-Ordner fehlt.' }
$runtimes = @('win-arm64', 'win-x64')
foreach ($rid in $runtimes) {
    $output = Join-Path $outputRoot "Klausurfix-Setup-$PackageVersion-$rid-$Suffix.exe"
    $package = Join-Path $outputRoot "Klausurfix-Update-$PackageVersion-$rid.zip"
    if (Test-Path -LiteralPath $output) { throw "Ausgabedatei existiert bereits: $output" }
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Updatepaket fehlt: $package" }
}
foreach ($rid in $runtimes) {
    $output = Join-Path $outputRoot "Klausurfix-Setup-$PackageVersion-$rid-$Suffix.exe"
    $package = (Resolve-Path -LiteralPath (Join-Path $outputRoot "Klausurfix-Update-$PackageVersion-$rid.zip")).Path
    & dotnet run --project (Join-Path $projectRoot 'update-entwicklung\Paketpruefung\Paketpruefung.csproj') -- $package $rid
    if ($LASTEXITCODE -ne 0) { throw "Paketprüfung fehlgeschlagen: $rid" }
    $work = Join-Path $outputRoot ('.setup-build-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        & dotnet publish (Join-Path $projectRoot 'installer\Klausurfix.Installer.csproj') -c Release -r $rid --self-contained true -o $work `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:UpdatePackagePath=$package" -p:DebugType=None -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "Installer-Build fehlgeschlagen: $rid" }
        [IO.File]::Move((Join-Path $work 'Klausurfix.Installer.exe'), $output)
        Write-Output "Erstellt: $output"
    } finally {
        $resolvedWork = (Resolve-Path -LiteralPath $work).Path
        $resolvedOutput = (Resolve-Path -LiteralPath $outputRoot).Path.TrimEnd('\') + '\'
        if ($resolvedWork.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedWork) -match '^\.setup-build-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $resolvedWork -Recurse -Force
        }
    }
}
