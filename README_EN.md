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

## 📖 Overview

A native helper launcher for the official Windows ChatGPT desktop client (Microsoft Store `OpenAI.Codex` and Standalone). The client is built on Chromium/Electron but offers no proxy settings in its UI, so restrictive networks cause `Reconnecting...` loops and timeouts — and the usual fix (global VPN / TUN) hijacks the whole machine. This project uses **process-level environment injection** + **sub-second WAL rollback** + **authoritative HTTPS egress verification**, giving ChatGPT a dedicated proxy while leaving the **system timezone and global proxy 100% untouched**.

---

## 🚀 Quick Start

1. Download `ChatGPTAntiBanLauncher.exe` from [Releases](../../releases) (portable, ~50-90 KB);
2. Launch, pick **Local Proxy mode**, and confirm your port (Clash `7897` / v2rayN `7890`);
3. Click **Detect Egress Node**, then **Match This Timezone** to align the timezone;
4. Click **Save & Launch** to start the client; *(optional)* create a **desktop shortcut**.

**Headless**: `ChatGPTAntiBanLauncher.exe --launch` (launch with saved config) & `--help`.

---

## ✨ Key Features

- **🟢 Dual-mode proxy**: injects `HTTP_PROXY` etc. in Local Proxy mode; strips inherited dirty proxy vars in Direct mode. Port presets (7897/7890/10808/1080) with socket probes and strict 1~65535 validation.
- **🕒 Timezone config**: IANA cities & UTC offsets; auto-converts to POSIX `Etc/GMT` to bypass Chromium's V8 `UTC-X` defect; rejects unreliable DST-shifting offsets; optional "disable spoofing".
- **🔍 Egress detection**: HTTPS-only GeoIP with strict TLS 1.2+, objectively aligns selected vs. actual egress timezone with 1-click apply.
- **⚡ WAL & Compare-and-Restore**: whitelisted 5 env vars only; user-SID global mutex prevents concurrent races; preserves external modifications (amber warning); sub-second cleanup with zero registry residue.
- **🛡️ Process safety**: canonical path/boundary checks; prefers `CloseMainWindow()`, refuses force-kill without explicit consent.
- **🪶 Lightweight**: pure C# WinForms, single ~50-90 KB `.exe`, no install; atomic config save never deletes the original.
- **🎨 Modern UI**: light/dark theme toggle with persistent follow-system (`theme_mode`); rounded cards + icon set + status animations; collapsible log bar; high-DPI adaptive button sizing.

---

## 📊 Solution Comparison

| Dimension | System-Wide TUN / TAP | Manual Batch Script (.bat) | This Launcher |
| :--- | :---: | :---: | :---: |
| **Proxy Scope** | ❌ Hijacks all traffic | ⚠️ Child process only, error-prone | 🟢 **Scoped to ChatGPT only** |
| **System Pollution** | ❌ Modifies routing/adapters | ❌ Leftover dirty vars | 🟢 **Zero-pollution (WAL rollback)** |
| **Conflict Protection** | ➖ None | ❌ Concurrent runs corrupt state | 🟢 **User SID global mutex** |
| **Egress Sensing** | ❌ None | ❌ None | 🟢 **HTTPS probe + 1-click alignment** |
| **Timezone Engine** | ❌ Changes system clock | ⚠️ Naive offsets break V8 | 🟢 **POSIX + Non-DST safeguards** |
| **Dependencies** | Requires TAP driver | None | 🟢 **Pure C#, zero deps** |

---

## 🏗️ Architecture

```mermaid
sequenceDiagram
    autonumber
    actor User as User
    participant UI as Launcher GUI
    participant Lock as Global SID Mutex
    participant WAL as Disk WAL File
    participant Shell as Windows Host Env (Registry / COM)
    participant App as ChatGPT Client

    User->>UI: Click [Save & Launch]
    UI->>Lock: Acquire Global\ChatGPTLauncher_EnvLock_<SID>
    Note over Lock: Cross-session mutex prevents concurrent races
    Lock-->>UI: Lock acquired
    UI->>WAL: Write snapshot & Flush(true)
    UI->>Shell: Inject whitelisted vars (TZ / HTTP_PROXY)
    UI->>App: Launch client (Standalone isolated process or Store COM)
    Note over App: Process tree adopts proxy
    UI->>Shell: Compare-and-Restore rollback
    Note over Shell: Restores originals; preserves external edits
    UI->>WAL: Clean up transaction log
    UI->>Lock: Release mutex
    UI-->>User: Launch success (system 100% clean)
```

---

## 🖥️ Requirements

Windows 10/11 (64-bit), built-in .NET Framework 4.5+ (**no runtime install needed**). Supports Store `OpenAI.Codex` and Standalone desktop editions.

---

## 🛠️ Building & Testing

Uses the built-in `csc.exe` — no Node/Visual Studio/.NET SDK required.

- **Build only**: `compile.bat` → `ChatGPTAntiBanLauncher.exe`; `build.bat` builds and launches.
- **Full tests**: `run_tests.bat` — 3 isolated suites, **212 assertions 100% passed** (mutex, WAL recovery, atomic save, POSIX validation; never touches the real registry).

---

## 🔬 Technical Reports

- 📄 [L4 Real Client Acceptance Report](docs/reports/REAL_CLIENT_ACCEPTANCE_REPORT.md) — PEB evidence proving 100% of outbound traffic routes through the proxy (0 direct connections).
- 📄 [v0.5 Validation Report](docs/reports/VALIDATION_REPORT_V0_5.md) — methodology & coverage of the 212 assertions.
- 📄 [Project Memory](docs/dev/PROJECT_MEMORY.md) — high-DPI scaling, batch multi-byte drift, namespace isolation lessons.

---

## ❓ FAQ

<details><summary><b>Q1: Will antivirus / Windows Defender flag this?</b></summary>

It's open-source, pure C#, compiled with the built-in `csc.exe`. As an unsigned lightweight launcher it may occasionally trigger heuristic false positives. Review the code and compile locally with `compile.bat` for full confidence.
</details>

<details><summary><b>Q2: Why does the Store-app chat page still show local time?</b></summary>

For sandbox safety, Chromium sets `clear_environment = true` on renderer processes and prefers native clock APIs over POSIX `TZ`. **This does not affect the network core**: `NetworkService` and `codex.exe` adopt the proxy/timezone and route 100% of traffic through the proxy.
</details>

<details><summary><b>Q3: Does closing the launcher change my system timezone/proxy?</b></summary>

**No.** Zero system pollution is the core principle — env vars are rolled back to their exact original values within milliseconds of launch.
</details>

---

## 🔒 Disclaimer

An independent third-party utility, **not affiliated with or endorsed by OpenAI / Microsoft**; uses no memory hooks, injection, or reverse-engineering; contains no telemetry or analytics. Users are responsible for complying with local laws and OpenAI's ToS.

---

## 📄 License

[MIT License](LICENSE)