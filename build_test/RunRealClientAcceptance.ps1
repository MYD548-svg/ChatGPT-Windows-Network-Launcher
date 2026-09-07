$ErrorActionPreference = "Stop"

Write-Host "======================================================================"
Write-Host "   REAL CHATGPT CLIENT ACCEPTANCE VERIFICATION (L4 EMPIRICAL SUITE)   "
Write-Host "======================================================================"

$root = (Resolve-Path "$PSScriptRoot\..").Path
$launcherExe = Join-Path $root "ChatGPTAntiBanLauncher.exe"
$pebReaderExe = Join-Path $root "build_test\ProcessEnvironmentReader.exe"
$settingsDir = Join-Path $env:LOCALAPPDATA "ChatGPTAntiBanLauncher"
$settingsFile = Join-Path $settingsDir "settings.json"

# 1. Host System Baseline
Write-Host "`n--- [Step 1/6] Host System & Proxy Baseline ---"
$localTz = [System.TimeZoneInfo]::Local
$localTime = [DateTime]::Now
Write-Host "System Time: $($localTime.ToString('o'))"
Write-Host "System TimeZone: $($localTz.Id) ($($localTz.DisplayName))"
Write-Host "System UTC Offset: $($localTz.GetUtcOffset($localTime))"

# Check Proxy Port 7897
$proxyPort = 7897
$proxyAlive = $false
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $iar = $tcp.BeginConnect("127.0.0.1", $proxyPort, $null, $null)
    $ok = $iar.AsyncWaitHandle.WaitOne(1000)
    if ($ok -and $tcp.Connected) {
        $proxyAlive = $true
        $tcp.EndConnect($iar)
    }
    $tcp.Close()
} catch {}

Write-Host "Proxy 127.0.0.1:$proxyPort Listening: $proxyAlive"
if (-not $proxyAlive) {
    Write-Warning "Proxy port $proxyPort is not listening! Please start your proxy (e.g. Clash/Mihomo)."
}

# Query Node via Proxy
$proxyNodeInfo = $null
try {
    $proxyNodeInfo = Invoke-RestMethod -Uri "https://ipinfo.io/json" -Proxy "http://127.0.0.1:$proxyPort" -TimeoutSec 5
    Write-Host "Proxy Exit IP: $($proxyNodeInfo.ip)"
    Write-Host "Proxy Exit Location: $($proxyNodeInfo.city), $($proxyNodeInfo.country)"
    Write-Host "Proxy Exit TimeZone: $($proxyNodeInfo.timezone)"
} catch {
    Write-Warning "Failed to probe proxy exit node: $_"
}

# 2. Configure Launcher Settings for Japan/Tokyo node
Write-Host "`n--- [Step 2/6] Configuring Launcher Settings ---"
if (-not (Test-Path $settingsDir)) {
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
}

$targetIana = if ($proxyNodeInfo -and $proxyNodeInfo.timezone) { $proxyNodeInfo.timezone } else { "Asia/Tokyo" }

$settingsJson = @"
{
  "mode": "iana",
  "iana_name": "$targetIana",
  "utc_offset": "UTC+9",
  "graceful_close": true,
  "network_mode": "vpn",
  "proxy_port": $proxyPort,
  "auto_detect_proxy": false,
  "disable_tz": false
}
"@
[System.IO.File]::WriteAllText($settingsFile, $settingsJson, [System.Text.Encoding]::UTF8)
Write-Host "Wrote launcher settings with NetworkMode=vpn, ProxyPort=$proxyPort, IANA=$targetIana"

# 3. Check and close any pre-existing ChatGPT processes
Write-Host "`n--- [Step 3/6] Pre-launch Process Status ---"
$preExisting = Get-Process -Name ChatGPT, Codex -ErrorAction SilentlyContinue
if ($preExisting) {
    Write-Host "Detected $($preExisting.Count) running ChatGPT process(es):"
    $preExisting | Format-Table Id, ProcessName, StartTime -AutoSize | Out-String | Write-Host
} else {
    Write-Host "Zero running ChatGPT/Codex processes detected (clean baseline)."
}

# 4. Invoke Launcher to launch real client
Write-Host "`n--- [Step 4/6] Launching Real ChatGPT Client via Launcher ---"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $launcherExe
$psi.Arguments = "--launch"
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true

$launchStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$launcherProc = [System.Diagnostics.Process]::Start($psi)
$launcherStdout = $launcherProc.StandardOutput.ReadToEnd()
$launcherStderr = $launcherProc.StandardError.ReadToEnd()
$launcherProc.WaitForExit()
$launchStopwatch.Stop()

Write-Host "Launcher ExitCode: $($launcherProc.ExitCode) (Elapsed: $($launchStopwatch.ElapsedMilliseconds)ms)"
Write-Host "Launcher STDOUT:`n$launcherStdout"
if ($launcherStderr) {
    Write-Host "Launcher STDERR:`n$launcherStderr"
}

# 5. Deep Empirical Verification of the Real Client
Write-Host "`n--- [Step 5/6] Deep Process & Network Inspection ---"
# Give ChatGPT 4 seconds to spawn its main and utility processes
Start-Sleep -Seconds 4

$activeProcs = Get-Process -Name ChatGPT, Codex -ErrorAction SilentlyContinue
Write-Host "Active ChatGPT / Codex process count: $($activeProcs.Count)"

$processReport = @()
$allPids = @()

foreach ($p in $activeProcs) {
    $allPids += $p.Id
    Write-Host "`n========================================================"
    Write-Host "Target Process: PID=$($p.Id) Name=$($p.ProcessName)"
    Write-Host "========================================================"
    
    # Read PEB Environment Variables
    $envVars = @{}
    try {
        $pebOut = & $pebReaderExe $p.Id
        $readingEnv = $false
        foreach ($line in $pebOut) {
            Write-Host "  $line"
            if ($line -match "^\s*([A-Za-z0-9_]+)\s*=\s*(.*)$") {
                $envVars[$matches[1]] = $matches[2]
            }
        }
    } catch {
        Write-Warning "  Failed to read PEB for PID $($p.Id): $_"
    }
    
    $processReport += [PSCustomObject]@{
        PID = $p.Id
        Name = $p.ProcessName
        TZ = $envVars["TZ"]
        HTTP_PROXY = $envVars["HTTP_PROXY"]
        HTTPS_PROXY = $envVars["HTTPS_PROXY"]
        ALL_PROXY = $envVars["ALL_PROXY"]
        NO_PROXY = $envVars["NO_PROXY"]
    }
}

# Network connection inspection
Write-Host "`n--- [Network Connection Inspection across ChatGPT Process Tree] ---"
$networkSnapshot = @()
for ($i = 0; $i -lt 5; $i++) {
    $conns = Get-NetTCPConnection -ErrorAction SilentlyContinue | Where-Object { $allPids -contains $_.OwningProcess }
    foreach ($c in $conns) {
        $networkSnapshot += [PSCustomObject]@{
            OwningProcess = $c.OwningProcess
            LocalAddress = $c.LocalAddress
            LocalPort = $c.LocalPort
            RemoteAddress = $c.RemoteAddress
            RemotePort = $c.RemotePort
            State = $c.State
        }
    }
    Start-Sleep -Milliseconds 800
}

$uniqueConns = $networkSnapshot | Sort-Object OwningProcess, LocalPort, RemoteAddress, RemotePort -Unique
if ($uniqueConns) {
    Write-Host "Detected active TCP connections from ChatGPT processes:"
    $uniqueConns | Format-Table OwningProcess, LocalAddress, LocalPort, RemoteAddress, RemotePort, State -AutoSize | Out-String | Write-Host
    
    $proxyConns = $uniqueConns | Where-Object { $_.RemoteAddress -eq "127.0.0.1" -and $_.RemotePort -eq $proxyPort }
    $directConns = $uniqueConns | Where-Object { $_.RemoteAddress -ne "127.0.0.1" }
    
    Write-Host "Connections routed to Proxy (127.0.0.1:$proxyPort): $($proxyConns.Count)"
    Write-Host "Direct external connections: $($directConns.Count)"
} else {
    Write-Host "No active TCP connections detected during monitoring window (Client in idle/cached state)."
}

# 6. Environmental Hygiene & Transaction Audit
Write-Host "`n--- [Step 6/6] Environmental Hygiene & WAL Cleanup Audit ---"
$regKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment")
$residualVars = @{}
foreach ($v in @("TZ", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY")) {
    $val = $regKey.GetValue($v, $null)
    $residualVars[$v] = $val
    Write-Host "Registry HKCU\Environment\$v = $(if ($val -eq $null) { '<NULL/DELETED>' } else { $val })"
}
$regKey.Close()

$walFiles = Get-ChildItem -Path $settingsDir -Filter "*.wal" -ErrorAction SilentlyContinue
Write-Host "WAL files remaining in ${settingsDir}: $($walFiles.Count)"

# Save structured results
$summaryReport = [PSCustomObject]@{
    Timestamp = [DateTime]::UtcNow.ToString("o")
    LocalSystemTz = $localTz.Id
    ProxyPort = $proxyPort
    ProxyAlive = $proxyAlive
    ProxyExitNode = $proxyNodeInfo
    LauncherExitCode = $launcherProc.ExitCode
    LauncherOutput = $launcherStdout
    ActiveProcessCount = $activeProcs.Count
    Processes = $processReport
    NetworkConnections = $uniqueConns
    ResidualRegistryVariables = $residualVars
    RemainingWalFiles = $walFiles.Count
}

$reportPath = Join-Path $root "build_test\real_client_acceptance_evidence.json"
$summaryReport | ConvertTo-Json -Depth 5 | Set-Content -Path $reportPath -Encoding UTF8
Write-Host "`nStructured evidence saved to: $reportPath"
Write-Host "======================================================================"
Write-Host "                      ACCEPTANCE RUN COMPLETE                         "
Write-Host "======================================================================"
