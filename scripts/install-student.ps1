#requires -Version 5.1
#requires -RunAsAdministrator
param()
. "$PSScriptRoot\Common.ps1"
Assert-Platform
Start-InstallLog
try {
$paths = Install-Payload 'Student'
Start-Service VeyonSyncStudent
Wait-Administration 'Student'
Write-Host 'Student installed. Open VeyonSync Student from the Start menu and complete the pairing wizard.'
Write-Host 'Existing identity, pending reports, configuration and enrollment are preserved during repair.'

} finally { Stop-Transcript | Out-Null }
