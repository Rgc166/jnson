#requires -Version 7.0
param([ValidateSet('win-x64')][string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
& "$PSScriptRoot/check-scripts.ps1"
$root=Split-Path $PSScriptRoot
$artifacts=Join-Path $root 'artifacts'
Push-Location $root
try {
    dotnet restore VeyonSync.slnx --locked-mode -p:EnableWindowsTargeting=true
    if($LASTEXITCODE -ne 0){throw 'Restore failed.'}
    dotnet test tests/VeyonSync.Tests/VeyonSync.Tests.csproj -c Release --no-restore
    if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
    foreach($role in @('Teacher','Student')) {
        $package=Join-Path $artifacts "VeyonSync-$role"
        if(Test-Path $package){Remove-Item $package -Recurse -Force}
        New-Item -ItemType Directory -Path $package -Force | Out-Null
        foreach($kind in @('Service','App')) {
            $project=if($kind -eq 'Service'){"VeyonSync.$role"}else{"VeyonSync.$role.App"}
            dotnet publish "src/$project/$project.csproj" -c Release -r $Runtime --self-contained true -p:EnableWindowsTargeting=true -p:RestoreLockedMode=true -o "$package/$role/$kind"
            if($LASTEXITCODE -ne 0){throw "Publish failed: $project"}
        }
        Copy-Item "$PSScriptRoot/Common.ps1","$PSScriptRoot/uninstall.ps1","$PSScriptRoot/INSTALL.bat" $package
        Copy-Item "$PSScriptRoot/install-$($role.ToLowerInvariant()).ps1" $package
        Copy-Item "$root/README.md","$root/NOTICE.md","$root/SECURITY.md" $package
        Copy-Item "$root/docs" $package -Recurse -Force
        Compress-Archive -Path "$package/*" -DestinationPath "$artifacts/VeyonSync-$role-$Runtime.zip" -Force
    }
    Get-ChildItem "$artifacts/*.zip" | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content "$artifacts/SHA256SUMS.txt"
} finally {Pop-Location}
