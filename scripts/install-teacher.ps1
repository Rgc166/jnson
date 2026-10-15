#requires -Version 5.1
#requires -RunAsAdministrator
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,32}$')][string]$Lab,
      [ValidateRange(1024,65535)][int]$Port=5443, [switch]$RotateCertificate)
. "$PSScriptRoot\Common.ps1"
Assert-Platform
Start-InstallLog
try {
$data = Join-Path $env:ProgramData 'VeyonSync\Teacher'
$configPath = Join-Path $data 'config.json'
$config = $null
if (Test-Path -LiteralPath $configPath) { $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json }
$legacy = Join-Path $env:ProgramFiles 'VeyonSync\Teacher\appsettings.json'
if (-not $config -and (Test-Path -LiteralPath $legacy)) {
    $old = Get-Content -LiteralPath $legacy -Raw | ConvertFrom-Json
    $config = [pscustomobject]@{lab=$old.Lab;port=$old.Port;thumbprint=$old.Tls.Thumbprint;teacherName=$env:COMPUTERNAME;veyonCli="$env:ProgramFiles\Veyon\veyon-cli.exe";retentionDays=90}
}
if ($config -and ($config.lab -ne $Lab -or $config.port -ne $Port)) { throw 'Existing lab/port differs. This repair does not silently reassign the laboratory.' }
$cert = $null
if ($config) {
    $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object Thumbprint -eq $config.thumbprint | Select-Object -First 1
    if ((-not $cert -or -not $cert.HasPrivateKey -or $cert.NotAfter -lt (Get-Date).AddDays(30) -or $cert.GetNameInfo('DnsName',$false) -ne $env:COMPUTERNAME) -and -not $RotateCertificate) {
        throw 'Certificate missing, expiring or mismatched. Use -RotateCertificate explicitly, then redistribute the public certificate and re-pair students.'
    }
}
$paths = Install-Payload 'Teacher'
if (-not $cert -or $RotateCertificate) {
    $cert = New-SelfSignedCertificate -DnsName $env:COMPUTERNAME -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(2) -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -Type SSLServerAuthentication
}
if (-not $config) { $config = [pscustomobject]@{lab=$Lab;port=$Port;thumbprint=$cert.Thumbprint;teacherName=$env:COMPUTERNAME;veyonCli="$env:ProgramFiles\Veyon\veyon-cli.exe";retentionDays=90} }
$config.thumbprint = $cert.Thumbprint
$config.teacherName = $env:COMPUTERNAME
Write-JsonAtomic $configPath $config
Export-Certificate -Cert $cert -FilePath (Join-Path $data 'professor.cer') -Force | Out-Null
Protect-Tree $data
Remove-NetFirewallRule -DisplayName 'VeyonSync Professor' -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName 'VeyonSync Professor' -Direction Inbound -Protocol TCP -LocalPort $Port -RemoteAddress LocalSubnet -Profile Domain,Private -Action Allow | Out-Null
Start-Service VeyonSyncTeacher
Wait-Administration 'Teacher'
# Use the published .NET client so Windows PowerShell 5.1 and PowerShell 7 share the same TLS checks.
Invoke-Native -File (Join-Path $paths.Target 'Service\VeyonSync.Teacher.exe') -Arguments @('--health-check')
Write-Host "Teacher installed. HTTPS verified. Public certificate: $data\professor.cer"
Write-Host 'Open VeyonSync Teacher from the Start menu to generate a single-use pairing code.'

} finally { Stop-Transcript | Out-Null }
