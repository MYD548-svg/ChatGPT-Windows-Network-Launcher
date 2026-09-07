$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Join-Path $PSScriptRoot "..\ChatGPTAntiBanLauncher.exe")
$psi.Arguments = "--help"
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
$p.WaitForExit()
Write-Host "ExitCode: $($p.ExitCode)"
Write-Host "STDOUT: $stdout"
Write-Host "STDERR: $stderr"
