$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Dateisignaturpruefung\Dateisignaturpruefung.csproj'
& dotnet build $project --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
$verifier = Join-Path $PSScriptRoot 'Dateisignaturpruefung\bin\Debug\net10.0-windows\Dateisignaturpruefung.dll'
$target = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$certificate = [Security.Cryptography.X509Certificates.X509Certificate]::CreateFromSignedFile($target)
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash($certificate.GetRawCertData())).Replace('-', '') }
finally { $sha.Dispose(); $certificate.Dispose() }

& dotnet $verifier $target $hash
if ($LASTEXITCODE -ne 0) { throw 'Gültige Signatur wurde abgewiesen.' }
& dotnet $verifier $target ('0' * 64)
if ($LASTEXITCODE -eq 0) { throw 'Falsches Zertifikat wurde akzeptiert.' }

$testFile = Join-Path ([IO.Path]::GetTempPath()) ('klausurfix-signaturtest-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    [IO.File]::Copy($target, $testFile)
    $stream = [IO.File]::Open($testFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { $stream.Position = 4096; $value = $stream.ReadByte(); $stream.Position = 4096; $stream.WriteByte([byte]($value -bxor 1)) }
    finally { $stream.Dispose() }
    & dotnet $verifier $testFile $hash
    if ($LASTEXITCODE -eq 0) { throw 'Manipulierte Datei wurde akzeptiert.' }
    Write-Output 'Signaturtests bestanden.'
} finally {
    if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile -Force }
}
