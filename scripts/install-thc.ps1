#requires -Version 5.1
#requires -RunAsAdministrator
param([switch]$NoLaunch)
. "$PSScriptRoot\Common.ps1"
Assert-Platform
$legacy=Join-Path $env:ProgramFiles 'VeyonSync\Teacher'
$teacher=Join-Path $legacy 'VeyonSync.Teacher.exe'
$database=Join-Path $env:ProgramData 'VeyonSync\Teacher\teacher.db'
$configuration=Join-Path $legacy 'appsettings.json'
foreach($file in @($teacher,$database,$configuration)) {
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw 'Install the etapa 2 teacher service first. This package adds only the THC interface.'}
}
$service=Get-CimInstance Win32_Service -Filter "Name='VeyonSyncTeacher'"
if(-not $service -or $service.PathName.Trim().Trim('"') -ine $teacher){throw 'The installed service does not match etapa 2. No service or data was changed.'}
$source=Join-Path $PSScriptRoot 'THC'
if(-not (Test-Path "$source\VeyonSync.Teacher.THC.exe")){throw 'THC executable missing. Extract the complete ZIP before installing.'}
if(Get-Process -Name 'VeyonSync.Teacher.THC' -ErrorAction SilentlyContinue){throw 'Close the THC window before updating the interface.'}
$target=Join-Path $legacy 'THC'
if(Test-Path -LiteralPath $target) {
    if((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'The THC directory cannot be a reparse point.'}
} else {New-Item -ItemType Directory -Path $target | Out-Null}
Protect-Tree $target
Copy-Item "$source\*" $target -Recurse -Force
Protect-Tree $target
# Executables contain no credentials. Users need read/execute access to load the
# requireAdministrator manifest and receive the UAC prompt; only admins may write.
foreach($item in @((Get-Item -LiteralPath $target)) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)) {
    $acl=Get-Acl -LiteralPath $item.FullName
    $users=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
    if($item.PSIsContainer){$rule=[Security.AccessControl.FileSystemAccessRule]::new($users,'ReadAndExecute','ContainerInherit, ObjectInherit','None','Allow')}
    else {$rule=[Security.AccessControl.FileSystemAccessRule]::new($users,'ReadAndExecute','Allow')}
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $item.FullName -AclObject $acl
}
$shell=New-Object -ComObject WScript.Shell
foreach($folder in @([Environment]::GetFolderPath('CommonDesktopDirectory'),[Environment]::GetFolderPath('CommonPrograms'))) {
    $shortcut=$shell.CreateShortcut((Join-Path $folder 'VeyonSync THC.lnk'))
    $shortcut.TargetPath=Join-Path $target 'VeyonSync.Teacher.THC.exe'
    $shortcut.WorkingDirectory=$target
    $shortcut.Description='VeyonSync THC - Professor - Etapa 2'
    $shortcut.Save()
}
Write-Host 'THC interface installed. Open VeyonSync THC from the desktop or Start menu.'
Write-Host 'Existing service, configuration, certificates, database and enrollment were preserved.'
if(-not $NoLaunch){Start-Process -FilePath (Join-Path $target 'VeyonSync.Teacher.THC.exe') -WorkingDirectory $target}
