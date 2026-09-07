$procs = Get-Process -Name ChatGPT, Codex -ErrorAction SilentlyContinue
foreach ($p in $procs) {
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId = $($p.Id)"
    $cmd = if ($cim) { $cim.CommandLine } else { "<unknown>" }
    $parent = if ($cim) { $cim.ParentProcessId } else { 0 }
    
    # Check if sandbox / type
    $type = "Main/Browser"
    if ($cmd -match "--type=([^\s]+)") {
        $type = $matches[1]
    }
    
    Write-Host "PID: $($p.Id), Parent: $parent, Name: $($p.ProcessName), Type: $type"
    Write-Host "  CommandLine: $cmd"
}
