param([string]$ReferenceDirectory, [string]$OutputName = 'HlsStyleSequencer-0.4.exe')
$ErrorActionPreference = 'Stop'
$releaseDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or [IO.Path]::GetExtension($OutputName) -ne '.exe') { throw 'OutputName must be an executable file name inside this release folder.' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Install .NET Framework 4 and its C# compiler before rebuilding.' }
if (-not $ReferenceDirectory) {
    $ReferenceDirectory = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0'
}
if (-not (Test-Path -LiteralPath (Join-Path $ReferenceDirectory 'mscorlib.dll'))) {
    throw 'Supply -ReferenceDirectory with the FULL .NET Framework 4.0 reference assembly folder; see README.txt. Runtime assemblies are deliberately not used as a substitute.'
}
$references = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll')
$arguments = @('/nologo','/noconfig','/nostdlib+','/langversion:4','/target:winexe','/platform:x86','/optimize+','/warn:4')
$arguments += '/out:' + (Join-Path $releaseDirectory $OutputName)
$arguments += '/win32manifest:' + (Join-Path $releaseDirectory 'source\app.manifest')
foreach ($assembly in $references) { $arguments += '/reference:' + (Join-Path $ReferenceDirectory $assembly) }
foreach ($source in (Get-ChildItem -LiteralPath (Join-Path $releaseDirectory 'source') -Filter '*.cs')) { $arguments += $source.FullName }
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
if ($OutputName -ne 'HlsStyleSequencer.exe') { Copy-Item -LiteralPath (Join-Path $releaseDirectory 'HlsStyleSequencer.exe.config') -Destination (Join-Path $releaseDirectory ($OutputName + '.config')) -Force }
Write-Output 'PASS: native x86 build against .NET Framework 4.0 reference assemblies.'
