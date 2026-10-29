$ErrorActionPreference = 'Stop'
$P = 'C:\Projects\JoyNect'
$stderr = "$P\test-firstrun-stderr.txt"
$stdout = "$P\test-firstrun-stdout.txt"

$X = Start-Process -FilePath 'dotnet' -ArgumentList 'run --project src/JoyNect.App --configuration Debug --no-build -- --tray' -PassThru -RedirectStandardError $stderr -RedirectStandardOutput $stdout
Start-Sleep -Seconds 14

Write-Output '=== stderr ==='
$stderrContents = Get-Content $stderr -Raw
if ($stderrContents) { $stderrContents } else { Write-Output '(empty)' }

Write-Output '=== stdout ==='
$stdoutContents = Get-Content $stdout -Raw
if ($stdoutContents) { $stdoutContents } else { Write-Output '(empty)' }

$X | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item $stderr, $stdout -Force -ErrorAction SilentlyContinue
Write-Output 'done'
