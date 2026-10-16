# Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
# SPDX-License-Identifier: MPL-2.0

[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'ARM64')]
    [string] $Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'MediaExport\MediaExport.csproj'
$framework = 'net10.0-windows10.0.19041.0'
$runtime = "win-$($Platform.ToLowerInvariant())"
$output = Join-Path $repository "MediaExport\bin\$Platform\Debug\$framework\$runtime"
$executable = Join-Path $output 'MediaExport.exe'
$intermediate = "obj\dev\$PID\"

# A previous development instance locks the executable that the next build replaces.
Get-Process MediaExport -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($repository, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force

$arguments = @(
    'build', $project,
    "-p:Platform=$Platform",
    '-p:WindowsPackageType=None',
    '-p:WindowsAppSDKSelfContained=true',
    '-p:AppxPackage=false',
    '-p:EnableMsixTooling=false',
    "-p:IntermediateOutputPath=$intermediate",
    '-m:1',
    '-nr:false'
)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "MediaExport development build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $executable)) {
    throw "The development executable was not produced at $executable."
}

$process = Start-Process -FilePath $executable -PassThru
[pscustomobject]@{
    ProcessId = $process.Id
    Executable = (Resolve-Path -LiteralPath $executable).Path
    BuiltAt = (Get-Item -LiteralPath $executable).LastWriteTime
}
