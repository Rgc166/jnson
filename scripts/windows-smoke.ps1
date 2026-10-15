#requires -Version 7.0
#requires -RunAsAdministrator
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$teacher=Join-Path $root 'artifacts/VeyonSync-Teacher'
$student=Join-Path $root 'artifacts/VeyonSync-Student'
foreach($role in @('Teacher','Student')) {
    if((Get-Service "VeyonSync$role" -ErrorAction SilentlyContinue) -or (Test-Path "$env:ProgramData/VeyonSync/$role")) {
        throw 'Run this smoke test only on a disposable Windows machine with no existing VeyonSync installation.'
    }
}
function Invoke-Local {
    param([string]$Role,[string]$Action,[object]$Data=$null)
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',"VeyonSync.$Role.v1",[IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(30000)
        $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),1024,$true)
        $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,1024,$true)
        $writer.AutoFlush=$true
        $request=@{action=$Action}
        if($null -ne $Data){$request.data=ConvertTo-Json -InputObject $Data -Depth 20 -Compress}
        $writer.WriteLine(($request | ConvertTo-Json -Compress))
        $read=$reader.ReadLineAsync()
        if(-not $read.Wait(80000)){throw 'Local RPC timed out.'}
        $reply=$read.Result | ConvertFrom-Json
        if(-not $reply.ok){throw $reply.error}
        return $reply.data | ConvertFrom-Json
    } finally {$pipe.Dispose()}
}
try {
    & "$teacher/install-teacher.ps1" -Lab CI
    & "$student/install-student.ps1"
    $code=Invoke-Local Teacher new-code
    $enrollment=@{teacherUrl="https://$($env:COMPUTERNAME):5443";lab='CI';position='PC01';certificatePath="$env:ProgramData/VeyonSync/Teacher/professor.cer";code=$code.code;confirmReplacement=$false}
    $null=Invoke-Local Student pair $enrollment
    Remove-Variable code,enrollment
    $null=Invoke-Local Student send
    $confirmed=$false
    for($i=0;$i -lt 20;$i++) {
        Start-Sleep -Seconds 3
        $snapshot=Invoke-Local Student snapshot
        if($snapshot.agent.lastConfirmed){$confirmed=$true;break}
    }
    if(-not $confirmed){throw 'No report acknowledged by teacher.'}
    $inventory=Invoke-Local Teacher snapshot
    if($inventory.machines.Count -ne 1){throw 'Expected exactly one enrolled machine.'}
    $identity=$inventory.machines[0].data.agentId
    $null=Invoke-Local Student diagnose
    # Exercise repair and restart without replacing enrollment or clearing pending state.
    & "$teacher/install-teacher.ps1" -Lab CI
    & "$student/install-student.ps1"
    $settings=Invoke-Local Student settings
    if($settings.agentId -ne $identity){throw 'Repair changed the agent identity.'}
    $null=Invoke-Local Student send
    Start-Sleep -Seconds 8
    $after=Invoke-Local Teacher snapshot
    if($after.machines.Count -ne 1){throw 'Repair duplicated inventory.'}
    Write-Host 'PASS: install, TLS, enrollment, report, persistence, restart and repair.'
} finally {
    foreach($role in @('Student','Teacher')) {
        $package=Join-Path $root "artifacts/VeyonSync-$role"
        if(Get-Service "VeyonSync$role" -ErrorAction SilentlyContinue) { & "$package/uninstall.ps1" -Role $role -RemoveData -Confirm:$false }
    }
}
