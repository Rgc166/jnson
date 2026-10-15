Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Platform {
    if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Windows is required.' }
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
    $build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    if ($build -lt 19045) { throw 'Windows 10 22H2 (19045) or a supported Windows 11 version is required.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run PowerShell as administrator.'
    }
}
function Invoke-Native {
    param([string]$File, [string[]]$Arguments)
    $output = & $File @Arguments
    $result = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($result -ne 0) { throw "$File returned exit code $result." }
}
function Protect-Tree {
    param([string]$Path, [switch]$RootOnly)
    # Reject links before changing permissions. Never traverse a reparse point.
    $all = [Collections.Generic.List[IO.FileSystemInfo]]::new()
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($Path)
    while ($pending.Count -gt 0) {
        $current = Get-Item -LiteralPath $pending.Dequeue() -Force
        if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point rejected: $($current.FullName)" }
        $all.Add($current)
        if ($current.PSIsContainer -and -not $RootOnly) {
            foreach ($child in Get-ChildItem -LiteralPath $current.FullName -Force) { $pending.Enqueue($child.FullName) }
        }
    }
    foreach ($item in $all) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point rejected: $($item.FullName)" }
    }
    foreach ($item in $all) {
        if ($item.PSIsContainer) { $acl = [Security.AccessControl.DirectorySecurity]::new() }
        else { $acl = [Security.AccessControl.FileSecurity]::new() }
        $acl.SetAccessRuleProtection($true, $false)
        $admin = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
        $acl.SetOwner($admin)
        foreach ($sid in @($admin, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
            if ($item.PSIsContainer) {
                $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit, ObjectInherit','None','Allow')
            } else { $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','Allow') }
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $item.FullName -AclObject $acl
        $check = Get-Acl -LiteralPath $item.FullName
        foreach ($rule in $check.Access) {
            $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($sid -notin @('S-1-5-18','S-1-5-32-544')) { throw "Unexpected ACL on $($item.FullName)" }
        }
    }
}
function Write-JsonAtomic {
    param([string]$Path, [object]$Value)
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
function Install-Payload {
    param([string]$Role)
    $source = Join-Path $PSScriptRoot $Role
    $target = Join-Path $env:ProgramFiles "VeyonSync\$Role"
    $data = Join-Path $env:ProgramData "VeyonSync\$Role"
    if (-not (Test-Path "$source\Service\VeyonSync.$Role.exe")) { throw "Published service missing: $source. Run scripts\build.ps1 first." }
    if (-not (Test-Path "$source\App\VeyonSync.$Role.App.exe")) { throw "Published interface missing: $source" }
    if (Get-Process -Name "VeyonSync.$Role.App" -ErrorAction SilentlyContinue) { throw 'Close the VeyonSync interface before upgrading.' }
    $service = Get-Service "VeyonSync$Role" -ErrorAction SilentlyContinue
    if ($service) { Stop-Service $service -Force; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
    foreach ($parent in @((Split-Path $target),(Split-Path $data))) {
        if (Test-Path -LiteralPath $parent) {
            if ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point rejected: $parent" }
        } else { New-Item -ItemType Directory -Path $parent | Out-Null }
        Protect-Tree $parent -RootOnly
    }
    New-Item -ItemType Directory -Force -Path $target,$data | Out-Null
    Protect-Tree $target
    Protect-Tree $data
    # Snapshot configuration/state/database while the service is stopped; never log their contents.
    $backup = Join-Path $data ('upgrade-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $data -File -Force) { Copy-Item -LiteralPath $file.FullName -Destination $backup -Force }
    foreach ($folder in @('Service','App')) {
        $destination = Join-Path $target $folder
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Copy-Item "$source\$folder\*" $destination -Recurse -Force
    }
    Protect-Tree $target
    $binary = '"' + "$target\Service\VeyonSync.$Role.exe" + '"'
    if (-not $service) { New-Service -Name "VeyonSync$Role" -BinaryPathName $binary -DisplayName "VeyonSync $Role" -StartupType Automatic | Out-Null }
    else {
        $definition = Get-CimInstance Win32_Service -Filter "Name='VeyonSync$Role'"
        $change = Invoke-CimMethod -InputObject $definition -MethodName Change -Arguments @{PathName=$binary;StartMode='Automatic'}
        if ($change.ReturnValue -ne 0) { throw "Service configuration failed: $($change.ReturnValue)" }
    }
    Invoke-Native -File sc.exe -Arguments @('failure',"VeyonSync$Role",'reset=','86400','actions=','restart/5000/restart/15000/restart/60000')
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonPrograms')) "VeyonSync $Role.lnk"))
    $shortcut.TargetPath = "$target\App\VeyonSync.$Role.App.exe"
    $shortcut.WorkingDirectory = "$target\App"
    $shortcut.Save()
    return @{Target=$target;Data=$data}
}
function Wait-Administration {
    param([string]$Role)
    $service = Get-Service "VeyonSync$Role"
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', "VeyonSync.$Role.v1", [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(30000)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 1024, $true)
        $writer.AutoFlush = $true
        $writer.WriteLine('{"action":"snapshot"}')
        $task = $reader.ReadLineAsync()
        if (-not $task.Wait(10000)) { throw 'Administration readiness check timed out.' }
        $reply = $task.Result | ConvertFrom-Json
        if (-not $reply.ok) { throw 'Administration readiness check failed.' }
        Write-Host 'Service administration responded successfully.'
    } finally { $pipe.Dispose() }
}

function Start-InstallLog {
    $root = Join-Path $env:ProgramData 'VeyonSync'
    if (Test-Path -LiteralPath $root) {
        if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Data root cannot be a reparse point.' }
    } else { New-Item -ItemType Directory -Path $root | Out-Null }
    Protect-Tree $root -RootOnly
    $logs = Join-Path $root 'install-logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    Protect-Tree $logs
    # Keep only the latest twenty installation transcripts.
    Get-ChildItem $logs -Filter '*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -Skip 19 | Remove-Item -Force
    $file = Join-Path $logs ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N') + '.txt')
    Start-Transcript -Path $file | Out-Null
    Write-Host "Installation diagnostic log: $file"
}
