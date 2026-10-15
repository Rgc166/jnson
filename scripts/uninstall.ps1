#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
param([Parameter(Mandatory)][ValidateSet('Teacher','Student')][string]$Role,[switch]$RemoveData)
. "$PSScriptRoot\Common.ps1"
Assert-Platform
if (-not $PSCmdlet.ShouldProcess("VeyonSync $Role",'Uninstall service and application')) { return }
if (Get-Process -Name "VeyonSync.$Role.App" -ErrorAction SilentlyContinue) { throw 'Close the interface before uninstalling.' }
$service = Get-Service "VeyonSync$Role" -ErrorAction SilentlyContinue
if ($service) { Stop-Service $service -Force; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30)); Invoke-Native -File sc.exe -Arguments @('delete',"VeyonSync$Role") }
if ($Role -eq 'Teacher') { Remove-NetFirewallRule -DisplayName 'VeyonSync Professor' -ErrorAction SilentlyContinue }
$target = Join-Path $env:ProgramFiles "VeyonSync\$Role"
if (Test-Path -LiteralPath $target) { Protect-Tree $target; Remove-Item -LiteralPath $target -Recurse -Force }
$link = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) "VeyonSync $Role.lnk"
if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force }
if ($RemoveData) {
    $data = Join-Path $env:ProgramData "VeyonSync\$Role"
    if (Test-Path -LiteralPath $data) { Protect-Tree $data; Remove-Item -LiteralPath $data -Recurse -Force }
} else { Write-Host 'Data, history, backups and enrollment were preserved.' }
Write-Host 'Certificates in Windows stores were not removed; remove only certificates whose ownership and remaining usage you have verified.'
