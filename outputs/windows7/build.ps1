param([string]$ReferenceDirectory, [string]$OutputName = 'HLS-Sequencer.exe')
$ErrorActionPreference = 'Stop'
$releaseDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or [IO.Path]::GetExtension($OutputName) -ne '.exe') { throw 'OutputName must be an executable file name inside this release folder.' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Install .NET Framework 4 and its C# compiler before rebuilding.' }
if (-not $ReferenceDirectory) {
    $ReferenceDirectory = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0'
}
$references = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll')
foreach ($assembly in $references) { if (-not (Test-Path -LiteralPath (Join-Path $ReferenceDirectory $assembly))) { throw ('Supply the FULL .NET4.0 references. Missing: ' + $assembly) } }
$expectedReferenceHashes = @{
    'System.Core.dll' = '152e1f68ef7c982795faa5a68b1f087750b05bcbac2603442a3b23bac43e69c3'
    'System.Drawing.dll' = 'a8a134b773bdb25be802c76e3f4990b87c663748e8a71a596de62cd197aaab33'
    'System.Windows.Forms.dll' = '204e5165c79151c134121b602cf47d0009e3763de92f88b07896ca67fd5c83af'
    'System.Xml.dll' = '6c13e62f31bb034f82acd06446444611418b15c8be9633a6d4f9189759b1811e'
    'System.dll' = 'b6dc62e2437b39f48b5c3368e0d14f86cf00fbf0c76d2685026aafca7e9b50f7'
    'mscorlib.dll' = 'f7a62c9392d6762f5b46c76149173e2f265d6ad05aae54290ff40573ab32190e'
}
foreach ($assembly in $references) {
    $referenceStream = [IO.File]::OpenRead((Join-Path $ReferenceDirectory $assembly))
    $referenceHasher = [Security.Cryptography.SHA256]::Create()
    try { $actualReferenceHash = [BitConverter]::ToString($referenceHasher.ComputeHash($referenceStream)).Replace('-','').ToLowerInvariant() }
    finally { $referenceStream.Dispose(); $referenceHasher.Dispose() }
    if ($actualReferenceHash -ne $expectedReferenceHashes[$assembly]) { throw ('Use the pinned official full .NET4.0 reference pack; unexpected hash: ' + $assembly) }
}
$assets = @('demo.wav','display-demo.hlsr','display-demo.hlsrpreview','custom-demo.hlsr','handcrafted.hlsrlib')
foreach ($asset in $assets) { if (-not (Test-Path -LiteralPath (Join-Path $releaseDirectory $asset))) { throw ('Missing bundled example: ' + $asset) } }
$arguments = @('/nologo','/noconfig','/nostdlib+','/langversion:4','/target:winexe','/platform:x86','/optimize+','/warn:4')
$arguments += '/out:' + (Join-Path $releaseDirectory $OutputName)
$arguments += '/win32manifest:' + (Join-Path $releaseDirectory 'source\app.manifest')
foreach ($assembly in $references) { $arguments += '/reference:' + (Join-Path $ReferenceDirectory $assembly) }
foreach ($asset in $assets) { $arguments += '/resource:' + (Join-Path $releaseDirectory $asset) + ',HlsStyleSequencer.Examples.' + $asset }
foreach ($source in (Get-ChildItem -LiteralPath (Join-Path $releaseDirectory 'source') -Filter '*.cs' | Sort-Object Name)) { $arguments += $source.FullName }
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
Write-Output 'PASS: standalone x86 executable with embedded examples; exact full .NET4.0 references; no .exe.config required.'
