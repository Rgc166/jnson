#requires -Version 7.0
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Push-Location $root
try {
    $package=Join-Path $root 'artifacts/VeyonSync-Professor-THC'
    if(Test-Path $package){Remove-Item $package -Recurse -Force}
    New-Item -ItemType Directory -Force -Path "$package/THC" | Out-Null
    dotnet publish src/VeyonSync.Teacher.THC/VeyonSync.Teacher.THC.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=true -o "$package/THC"
    if($LASTEXITCODE -ne 0){throw 'THC publish failed.'}
    Copy-Item "$PSScriptRoot/install-thc.ps1","$PSScriptRoot/INSTALAR-INTERFACE-THC.bat","$PSScriptRoot/Common.ps1" $package
    Copy-Item "$root/docs/instalar-thc.md" "$package/LEIA-ME.md"
    Copy-Item "$root/NOTICE.md" $package
    Compress-Archive -Path "$package/*" -DestinationPath "$root/artifacts/VeyonSync-Professor-THC-win-x64.zip" -Force
    $hash=Get-FileHash "$root/artifacts/VeyonSync-Professor-THC-win-x64.zip" -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  VeyonSync-Professor-THC-win-x64.zip" | Set-Content "$root/artifacts/THC-SHA256SUMS.txt"
} finally {Pop-Location}
