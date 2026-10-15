#requires -Version 7.0
$ErrorActionPreference='Stop'
Get-ChildItem "$PSScriptRoot/*.ps1" | ForEach-Object {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ($errors | Out-String)}
    Write-Host "PASS parse: $($_.Name)"
}
. "$PSScriptRoot/Common.ps1"
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('VeyonSync-script-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $path=Join-Path $temporary 'state.json'
    Write-JsonAtomic $path @{sequence=100;status='pending'}
    Write-JsonAtomic $path @{sequence=101;status='confirmed'}
    $state=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if($state.sequence -ne 101 -or $state.status -ne 'confirmed'){throw 'Atomic write did not replace the state.'}
    if(@(Get-ChildItem -LiteralPath $temporary -Filter '*.tmp').Count){throw 'Temporary files leaked.'}
    Write-Host 'PASS atomic JSON creation and replacement'
    $executable=Join-Path $PSHOME $(if($IsWindows){'pwsh.exe'}else{'pwsh'})
    $caught=$false
    try {Invoke-Native -File $executable -Arguments @('-NoProfile','-Command','exit 7')}
    catch {if($_.Exception.Message -notlike '*exit code 7*'){throw};$caught=$true}
    if(-not $caught){throw 'Native command failure was not propagated.'}
    Write-Host 'PASS native command exit status preserved'
} finally {Remove-Item -LiteralPath $temporary -Recurse -Force}
