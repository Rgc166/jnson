#requires -Version 7.0
#requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$package=Join-Path $root 'artifacts/VeyonSync-Professor-THC'
$app=Join-Path $env:ProgramFiles 'VeyonSync\Teacher'
$data=Join-Path $env:ProgramData 'VeyonSync\Teacher'
if((Get-Service VeyonSyncTeacher -ErrorAction SilentlyContinue) -or (Test-Path $app) -or (Test-Path $data)) {
    throw 'This fixture smoke test requires a disposable runner without VeyonSync teacher files or service.'
}
$createdService=$false
try {
    # A stopped service fixture verifies that installing the companion never
    # reconfigures or starts the teacher. It is not a real etapa 2 server test.
    New-Item -ItemType Directory -Force -Path $app,$data | Out-Null
    Copy-Item "$package/THC/VeyonSync.Teacher.THC.exe" "$app/VeyonSync.Teacher.exe"
    Set-Content "$app/appsettings.json" '{"Lab":"B","Port":5443,"Tls":{"Thumbprint":""}}'
    Set-Content "$data/teacher.db" 'installer-preservation-fixture'
    New-Service -Name VeyonSyncTeacher -BinaryPathName ('"'+"$app\VeyonSync.Teacher.exe"+'"') -StartupType Manual | Out-Null
    $createdService=$true
    $before=@{}
    foreach($file in @("$app/VeyonSync.Teacher.exe","$app/appsettings.json","$data/teacher.db")){$before[$file]=(Get-FileHash $file -Algorithm SHA256).Hash}
    $definition=(Get-CimInstance Win32_Service -Filter "Name='VeyonSyncTeacher'").PathName
    & "$package/install-thc.ps1" -NoLaunch
    foreach($file in $before.Keys){if((Get-FileHash $file -Algorithm SHA256).Hash -ne $before[$file]){throw "Existing file changed: $file"}}
    if((Get-CimInstance Win32_Service -Filter "Name='VeyonSyncTeacher'").PathName -ne $definition){throw 'Service configuration changed.'}
    if((Get-Service VeyonSyncTeacher).Status -ne 'Stopped'){throw 'Installer started the existing service.'}
    $shortcut=Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'VeyonSync THC.lnk'
    if(-not (Test-Path $shortcut)){throw 'Desktop shortcut missing.'}
    $exe=Join-Path $app 'THC/VeyonSync.Teacher.THC.exe'
    $usersRules=(Get-Acl $exe).Access | Where-Object {$_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq 'S-1-5-32-545'}
    if(-not $usersRules){throw 'Users must be able to read the executable and its UAC manifest.'}
    foreach($rule in $usersRules){if($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::WriteData){throw 'Users must not be able to modify the interface.'}}
    $process=Start-Process -FilePath $exe -ArgumentList '--smoke-test' -PassThru -WorkingDirectory $app
    if(-not $process.WaitForExit(30000)){$process.Kill();throw 'WPF smoke test timed out.'}
    if($process.ExitCode -ne 0){
        if(Test-Path "$app/thc-smoke-error.txt"){Get-Content "$app/thc-smoke-error.txt" | Write-Host}
        throw 'WPF window could not be constructed, shown and closed.'
    }
    Write-Host 'PASS: add-on install, unchanged service/data, shortcuts, read-only user access and WPF window smoke.'
} finally {
    if($createdService){sc.exe delete VeyonSyncTeacher | Out-Host}
    foreach($folder in @([Environment]::GetFolderPath('CommonDesktopDirectory'),[Environment]::GetFolderPath('CommonPrograms'))) {
        $link=Join-Path $folder 'VeyonSync THC.lnk';if(Test-Path $link){Remove-Item $link -Force}
    }
    if(Test-Path $app){Remove-Item $app -Recurse -Force}
    if(Test-Path $data){Remove-Item $data -Recurse -Force}
}
