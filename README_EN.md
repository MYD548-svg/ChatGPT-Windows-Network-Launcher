# ChatGPT Windows Network Launcher

<p align="center">
  <a href="README.md"><b>简体中文</b></a> | <a href="README_EN.md"><b>English</b></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET%20Framework-4.5%2B%20(Built--in)-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt="Framework" />
  <img src="https://img.shields.io/badge/Build-Zero--Dependency%20(csc.exe)-2ea44f?style=flat-square" alt="Build" />
  <img src="https://img.shields.io/badge/Tests-212%20Assertions%20Passed-success?style=flat-square" alt="Tests" />
  <img src="https://img.shields.io/badge/License-MIT-orange?style=flat-square" alt="License" />
  <img src="https://img.shields.io/badge/PRs-Welcome-brightgreen.svg?style=flat-square" alt="PRs Welcome" />
</p>

---

## 📖 Overview & Problem Statement

**ChatGPT Windows Network Launcher** is a dedicated native Windows helper application for the official ChatGPT desktop client (fully compatible with both Microsoft Store `OpenAI.Codex` and Standalone desktop editions).

### Background & Pain Points
The official Windows ChatGPT desktop client is built upon Chromium/Electron. However, it **does not provide any native proxy or connection settings in its user interface**:
- **Frequent Reconnecting & Timeouts**: In restrictive network environments, the client often gets trapped in infinite `Reconnecting...` loops, connection drops, or blank screens;
- **Severe Side Effects of Global VPNs**: The typical workaround requires switching the entire system to a global VPN or TUN/TAP virtual network adapter, which hijacks **all** traffic on your machine, slowing down local applications and disrupting LAN services;
- **Timezone vs Egress IP Discrepancies**: Severe mismatches between the local Windows system timezone and the proxy egress location (e.g., node located in Tokyo UTC+9 while the client reports UTC+8) can trigger multi-factor client anomaly detection.

### Our Solution
This project delivers **process-level environment variable injection**, **sub-second Write-Ahead Logging (WAL) transaction rollback**, and **authoritative HTTPS egress timezone verification**, enabling the ChatGPT client to utilize a dedicated local proxy channel while keeping the Windows system clock, global proxies, and other running applications **100% untouched and clean**.

---

## 📊 Solution Comparison

| Dimension | System-Wide TUN / TAP Mode | Manual Batch Script (.bat) | This Launcher |
| :--- | :---: | :---: | :---: |
| **Proxy Scope** | ❌ Hijacks all system traffic | ⚠️ Child process only, error-prone | 🟢 **Scoped strictly to ChatGPT process** |
| **System Pollution** | ❌ Modifies global routing / adapters | ❌ Prone to leftover dirty variables | 🟢 **100% Zero-pollution (WAL rollback)** |
| **Concurrency Protection** | ➖ None | ❌ Concurrent runs corrupt state | 🟢 **User SID-based Global Mutex** |
| **External Modification** | ➖ None | ❌ Overwrites external configs | 🟢 **Compare-and-Restore preservation** |
| **Egress IP & Timezone** | ❌ None | ❌ None | 🟢 **HTTPS probe & 1-click alignment** |
| **Timezone POSIX Engine** | ❌ Requires changing system clock | ⚠️ Naive offset causes V8 crash | 🟢 **Strict POSIX & Non-DST safeguards** |
| **Runtime Dependencies** | Requires TAP driver installation | None | 🟢 **Pure C# — zero dependencies** |

---

## ✨ Key Features

### 1. 🟢 Dual-Mode Proxy Injection
- **Local Proxy Mode**: Injects `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, and `NO_PROXY` environment variables into the target process;
- **Explicit Direct Mode**: Actively strips inherited proxy variables to guarantee a pure direct connection;
- **One-Click Presets**: Preconfigured with common local proxy ports (`7897 (Clash Verge / Mihomo)`, `7890 (v2rayN)`, `10808 (Xray)`, `1080 (Shadowsocks)`), equipped with live socket listening probes;
- **Strict Input Validation**: Port inputs are validated strictly within 1~65535, preventing silent fallback.

### 2. 🕒 Target Timezone & Chromium V8 POSIX Compatibility
- Supports standard IANA timezone names (e.g., `America/Los_Angeles`, `Asia/Tokyo`) and explicit UTC offsets;
- **V8 Engine POSIX Mapping**: Converts UTC offsets to POSIX-compliant `Etc/GMT` formats, bypassing a known V8/Chromium defect on Windows that mistakes non-standard `UTC-X` for `Etc/Unknown`;
- **Strict Non-DST Enforcement**: Only permits offsets that map to permanent non-DST timezones (such as Kathmandu +5:45, Kolkata +5:30, Darwin +9:30). Offsets subject to seasonal DST shifts without static alternatives (e.g. Newfoundland -3:30) are rejected with clear errors, strictly forbidding unsafe silent fallbacks;
- **Bypass Option**: Offers a "Disable Timezone Spoofing" toggle to inject only network proxies without touching timezone variables.

### 3. 🔍 Authoritative HTTPS Egress Probe & Security Alignment
- Entirely relies on secure HTTPS GeoIP endpoints—zero plaintext HTTP;
- **Strict TLS Certificate Chain Validation**: Enforces TLS 1.2+ handshake and eliminates all insecure certificate bypasses;
- Accurately classifies certificate errors, proxy unreachable, network timeouts, and HTTP errors;
- Objectively compares the configured timezone with the actual egress location, offering a **【⚡ Match This Timezone】** button.

### 4. ⚡ Write-Ahead Logging (WAL) & Compare-and-Restore
- **Strict Variable Whitelist**: Confined strictly to 5 trusted keys (`TZ`, `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NO_PROXY`); critical system variables like `PATH` are untouchable;
- **Cross-Session Mutex Guarantee**: Employs a global mutex bound to the current user SID (`Global\ChatGPTLauncher_EnvLock_<UserSID>`), with zero silent fallback to local locks;
- **Compare-and-Restore Conflict Protection**: If an external application modifies environment variables during the launch window, the launcher preserves the external value, outputs an amber warning in the UI, and redacts sensitive credentials from logs;
- **Sub-Second Transient Rollback**: Environment variables are restored to their original state within milliseconds of client launch.

### 5. 🛡️ Precise Process Identification & Graceful Exit
- Matches exact canonical path boundaries, preventing accidental kills of processes in similar prefix directories (`ChatGPT-other`);
- Sends `CloseMainWindow()` to prompt graceful exit before launch; refuses to force-kill without explicit user authorization callback, preventing loss of unsaved chat drafts.

### 6. 🪶 Lightweight Native Build & Atomic Config
- Built purely with native C# Windows Forms, compiling into a tiny standalone executable (~50-90 KB);
- Uses Safe Atomic Replace for settings persistence: if the target file is locked, existing settings are 100% preserved without deleting the file;
- Supports creating a 1-click desktop shortcut.

---

## 🏗️ Architecture & Workflow

```mermaid
sequenceDiagram
    autonumber
    actor User as User
    participant UI as Launcher GUI
    participant Lock as Global SID Mutex
    participant WAL as Disk WAL File
    participant Shell as Windows Host Env (Registry / COM)
    participant App as ChatGPT Client (ChatGPT.exe)

    User->>UI: Click [Save & Launch]
    UI->>Lock: Acquire Global\ChatGPTLauncher_EnvLock_<SID>
    Note over Lock: Cross-session mutex prevents concurrent race conditions
    Lock-->>UI: Lock acquired
    UI->>WAL: Write initial state snapshot and Flush(true) to disk
    UI->>Shell: Temporarily inject whitelisted vars (TZ / HTTP_PROXY)
    UI->>App: Launch client (Standalone isolated process or Store COM activation)
    Note over App: Process tree (Main, NetworkService, codex.exe) adopts proxy
    UI->>Shell: Execute Compare-and-Restore rollback
    Note over Shell: Restores original values; preserves external modifications
    UI->>WAL: Validate and cleanly delete WAL file
    UI->>Lock: Release Global Mutex
    UI-->>User: Launch success notification (System state 100% clean)
```

---

## 🖥️ System Requirements

- **Operating System**: Windows 10 / Windows 11 (64-bit)
- **Runtime**: .NET Framework 4.5 or higher (**Built into Windows 10 and 11 by default; no additional runtimes or SDKs required**)
- **Client Editions Supported**:
  - Microsoft Store Edition (`OpenAI.Codex`)
  - Standalone Desktop Installer Edition

---

## 🚀 Quick Start

### Method 1: Pre-built Executable
1. Go to the [Releases page](../../releases) and download the latest `ChatGPTAntiBanLauncher.exe`;
2. Run the application;
3. Select Local Proxy mode and specify your local port (e.g. `7897` for Clash, `7890` for v2ray);
4. Click **【🔍 Detect Current Node】**, then click **【⚡ Match This Timezone】**;
5. Click **【🚀 Save & Launch】**;
6. *(Optional)* Click **【📌 Create Desktop Shortcut】** for quick access.

### Method 2: Headless Command Line Mode
The launcher supports command-line invocation for scripts and automation:
```cmd
REM Launch client immediately using saved configuration
ChatGPTAntiBanLauncher.exe --launch

REM View command line help
ChatGPTAntiBanLauncher.exe --help
```

---

## 🛠️ Building & Testing

No Node.js, Python, or heavy .NET SDK installations are needed. You can compile directly using Windows' built-in `csc.exe`:

### 1. Build Only
Run `compile.bat` or execute in command prompt:
```cmd
compile.bat
```
Or with:
```cmd
build.bat /build-only
```
Upon completion, `ChatGPTAntiBanLauncher.exe` will be generated in the root directory.

### 2. Build and Run
Execute `build.bat` to compile and launch the GUI immediately.

### 3. Run Automated Isolated Test Suite
Run all 3 suites of unit and defensive tests in an isolated sandbox environment:
```cmd
run_tests.bat
```
> **Results**: 162 defense tests + 45 component tests + 5 transaction tests = **212 automated assertions passed with 100% success**.

---

## 🔬 In-Depth Engineering Reports

For complete technical transparency and verifiable evidence, review our audit reports:

- 📄 **[L4 Real Client Acceptance Report (REAL_CLIENT_ACCEPTANCE_REPORT.md)](docs/reports/REAL_CLIENT_ACCEPTANCE_REPORT.md)**:
  - Direct Win32 PEB extraction evidence proving `HTTP_PROXY` and `TZ` injection across `ChatGPT.exe`, `NetworkService`, and `codex.exe`;
  - Dynamic socket polling proving **100% of outbound connections route through the local proxy** (0 direct public IP connections);
  - Explains the Chromium Renderer sandbox environment clearing behavior.
- 📄 **[v0.5 Engineering Validation Report (VALIDATION_REPORT_V0_5.md)](docs/reports/VALIDATION_REPORT_V0_5.md)**:
  - Details the methodology and results for all 212 automated assertions.
- 📄 **[Project Memory & Engineering Retrospective (PROJECT_MEMORY.md)](docs/dev/PROJECT_MEMORY.md)**:
  - Engineering insights on High-DPI scaling, Windows batch multi-byte drift prevention, and namespace isolation.

---

## ❓ FAQ & Troubleshooting

<details>
<summary><b>Q1: Will Windows Defender or Antivirus flag this application?</b></summary>
<br>
This is an open-source, pure C# project compiled via Windows' native <code>csc.exe</code>. Because it is an independent open-source project without a costly commercial code-signing certificate, heuristic detection in some antivirus software may occasionally raise a false positive.
<br><br>
<b>Resolution</b>: All source code is completely open and auditable in this repository. You can review the code and compile it locally using <code>compile.bat</code> with complete confidence.
</details>

<details>
<summary><b>Q2: Why does the Store App chat page still display my local computer time?</b></summary>
<br>
As verified in our <a href="docs/reports/REAL_CLIENT_ACCEPTANCE_REPORT.md">L4 Acceptance Report</a>:
<ol>
  <li>For sandbox security, Chromium explicitly sets <code>clear_environment = true</code> when spawning <code>--type=renderer</code> processes, stripping non-essential environment variables;</li>
  <li>On Windows, Chromium's DOM prefers native Win32 clock APIs over POSIX <code>TZ</code> variables;</li>
  <li><b>Network routing is unaffected</b>: All external network requests are handled by <code>NetworkService</code> and <code>codex.exe</code>, which successfully adopt the proxy and timezone variables. 100% of outbound network traffic is properly routed through the proxy.</li>
</ol>
</details>

<details>
<summary><b>Q3: Does closing the launcher alter my system's global timezone or proxy?</b></summary>
<br>
<b>Never.</b> Zero system pollution is a core design principle. The launcher rolls back environment variables to their exact original values via WAL rollback within milliseconds of launching the client.
</details>

---

## 🔒 Security & Disclaimer

1. **Non-Affiliation**: This project is an independent third-party utility and is **not affiliated with, endorsed by, sponsored by, or associated with OpenAI, ChatGPT, or Microsoft**. "ChatGPT" is a trademark of OpenAI;
2. **Non-Invasive Architecture**: This software relies solely on documented operating system APIs. It contains **no DLL injection, no memory hooks, and does not modify any client binary files**;
3. **Privacy Assurance**: No telemetry, analytics, or credential harvesting code is present;
4. **Disclaimer**: Users are responsible for complying with applicable local laws and OpenAI's Terms of Service (ToS). The authors and contributors assume no liability for misuse.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
