# SoMan v1 — Social Media Account Manager (Threads Edition)

## Dokumen Rencana Pengembangan

**Versi Dokumen:** 1.0  
**Tanggal:** 15 April 2026  
**Status:** Draft  

---

## Daftar Isi

1. [Ringkasan Proyek](#1-ringkasan-proyek)
2. [Teknologi & Tools](#2-teknologi--tools)
3. [Arsitektur Aplikasi](#3-arsitektur-aplikasi)
4. [Struktur Folder Proyek](#4-struktur-folder-proyek)
5. [Database Schema](#5-database-schema)
6. [Spesifikasi Modul](#6-spesifikasi-modul)
7. [Desain UI](#7-desain-ui)
8. [Fase Pengembangan](#8-fase-pengembangan)
9. [Catatan Pengembangan Platform Selanjutnya](#9-catatan-pengembangan-platform-selanjutnya)

---

## 1. Ringkasan Proyek

### Apa itu SoMan?

SoMan (Social Manager) adalah aplikasi desktop Windows untuk mengelola dan mengotomasi banyak akun media sosial secara bersamaan. Versi pertama (v1) difokuskan pada **Threads** (threads.net).

### Fitur Utama

- Manajemen akun unlimited (login via cookies)
- Browser automation cerdas (auto-detect resource CPU/RAM)
- Mode headed & headless (hybrid, bisa dipilih per akun/task)
- Interaksi otomatis: like, comment, follow, unfollow, scroll, create post, repost, search, view profile
- Proxy per akun (HTTP/SOCKS5) + VPN optional
- Template aksi (reusable action sequences)
- Kategori/pengelompokan akun
- Pengaturan delay (random, human-like)
- Penautkan antar akun
- Scheduler (jadwal otomatis) + manual trigger
- Activity logging per akun
- Dashboard monitoring real-time (CPU, RAM, status akun)
- Enkripsi cookies (AES-256) di database
- Auto-recovery: lanjutkan task tertunda saat app restart

### Target Platform

- **OS:** Windows 10/11 (termasuk Windows Server untuk VPS)
- **Deployment:** Lokal atau Windows VPS (akses via RDP)
- **Runtime:** .NET 8
- **Min Spec:** 4GB RAM, dual-core CPU (semakin besar semakin banyak browser simultan)
- **Recommended VPS:** 8-16GB RAM, 4 vCPU, Windows Server 2022

---

## 2. Teknologi & Tools

| Komponen | Teknologi | Versi | Alasan |
|---|---|---|---|
| Framework UI | WPF (Windows Presentation Foundation) | .NET 8 | Native Windows, performa tinggi, UI kaya |
| UI Toolkit | Material Design in XAML Toolkit | 5.x | Modern look, dark/light theme |
| Browser Automation | Microsoft.Playwright | 1.49+ | BrowserContext = hemat RAM, multi-browser support |
| Database | SQLite | via EF Core 8 | Lokal, ringan, zero config |
| ORM | Entity Framework Core | 8.x | Type-safe queries, migrations |
| DI Container | Microsoft.Extensions.DependencyInjection | 8.x | Standard .NET DI |
| Logging | Serilog | 4.x | Structured logging, file sink |
| Scheduling | Quartz.NET | 3.x | Cron-like scheduler, persistent jobs |
| JSON | System.Text.Json | Built-in | Serialisasi konfigurasi |
| Resource Monitor | System.Diagnostics / WMI | Built-in | CPU & RAM monitoring |
| MVVM | CommunityToolkit.Mvvm | 8.x | Source generators, ObservableObject |

### NuGet Packages

```
Microsoft.Playwright
Microsoft.EntityFrameworkCore.Sqlite
MaterialDesignThemes
MaterialDesignColors
CommunityToolkit.Mvvm
Quartz
Serilog
Serilog.Sinks.File
Serilog.Sinks.Console
```

---

## 3. Arsitektur Aplikasi

### Pola Arsitektur: Modular Monolith + MVVM

```
┌─────────────────────────────────────────────────────────┐
│                     WPF UI (Views)                       │
│  Dashboard │ Accounts │ Tasks │ Templates │ Settings     │
├─────────────────────────────────────────────────────────┤
│                   ViewModels (MVVM)                       │
│  Binding, Commands, Navigation, State Management         │
├─────────────────────────────────────────────────────────┤
│                     Services                              │
│  ┌──────────┐ ┌──────────┐ ┌───────────┐ ┌───────────┐  │
│  │ Browser  │ │ Account  │ │ Template  │ │ Scheduler │  │
│  │ Manager  │ │ Service  │ │ Service   │ │ Service   │  │
│  ├──────────┤ ├──────────┤ ├───────────┤ ├───────────┤  │
│  │ Proxy    │ │ Category │ │ Delay     │ │ Logger    │  │
│  │ Manager  │ │ Service  │ │ Service   │ │ Service   │  │
│  ├──────────┤ ├──────────┤ ├───────────┤ ├───────────┤  │
│  │ Resource │ │ Account  │ │ Config    │ │           │  │
│  │ Monitor  │ │ Linker   │ │ Service   │ │           │  │
│  └──────────┘ └──────────┘ └───────────┘ └───────────┘  │
├─────────────────────────────────────────────────────────┤
│              Threads Platform Module                      │
│  ThreadsAutomation: Login, Like, Comment, Follow, Post   │
├─────────────────────────────────────────────────────────┤
│                    Data Layer                              │
│  DbContext │ Repositories │ Models │ Migrations           │
├─────────────────────────────────────────────────────────┤
│                    Infrastructure                          │
│  SQLite │ Playwright │ File System │ OS APIs              │
└─────────────────────────────────────────────────────────┘
```

### Alur Kerja Utama

```
User membuat Template Aksi
    → "Like 5 post random, delay 10-30 detik antar aksi"
    → "Scroll feed 2 menit"
    → "Comment 3 post dengan teks random dari daftar"

User memilih akun (atau kategori)
    → Pilih akun: Akun1, Akun2, ... Akun50

User klik "Run" atau "Schedule"
    → Scheduler/manual trigger

TaskEngine:
    → Cek ResourceMonitor: berapa slot tersedia?
    → Masukkan task ke queue berdasarkan prioritas
    → BrowserManager buka context untuk tiap akun
    → Jalankan aksi dari template secara sequential per akun
    → DelayService inject random delay antar aksi
    → Logger catat setiap aksi + hasil
    → Selesai → tutup context → ambil task berikutnya dari queue
```

---

## 4. Struktur Folder Proyek

```
SoMan/
├── SoMan.sln                          # Solution file
├── DEVELOPMENT_PLAN.md                 # Dokumen ini
│
├── src/
│   └── SoMan/                          # Main WPF Project
│       ├── App.xaml                    # Application entry
│       ├── App.xaml.cs
│       ├── MainWindow.xaml             # Shell window
│       ├── MainWindow.xaml.cs
│       │
│       ├── Models/                     # Data models / entities
│       │   ├── Account.cs
│       │   ├── AccountCategory.cs
│       │   ├── AccountLink.cs
│       │   ├── ProxyConfig.cs
│       │   ├── ActionTemplate.cs
│       │   ├── ActionStep.cs
│       │   ├── ScheduledTask.cs
│       │   ├── TaskExecution.cs
│       │   ├── ActivityLog.cs
│       │   └── AppSettings.cs
│       │
│       ├── Data/                       # Database layer
│       │   ├── SoManDbContext.cs
│       │   ├── Migrations/
│       │   └── Repositories/
│       │       ├── IAccountRepository.cs
│       │       ├── AccountRepository.cs
│       │       ├── ITemplateRepository.cs
│       │       ├── TemplateRepository.cs
│       │       └── ...
│       │
│       ├── Services/                   # Business logic
│       │   ├── Browser/
│       │   │   ├── IBrowserManager.cs
│       │   │   ├── BrowserManager.cs
│       │   │   ├── BrowserContextPool.cs
│       │   │   └── ResourceMonitor.cs
│       │   │
│       │   ├── Account/
│       │   │   ├── IAccountService.cs
│       │   │   ├── AccountService.cs
│       │   │   ├── ICategoryService.cs
│       │   │   ├── CategoryService.cs
│       │   │   ├── IAccountLinkerService.cs
│       │   │   └── AccountLinkerService.cs
│       │   │
│       │   ├── Proxy/
│       │   │   ├── IProxyManager.cs
│       │   │   └── ProxyManager.cs
│       │   │
│       │   ├── Execution/
│       │   │   ├── ITaskEngine.cs
│       │   │   └── TaskEngine.cs
│       │   │
│       │   ├── Scheduler/
│       │   │   ├── ISchedulerService.cs
│       │   │   └── SchedulerService.cs
│       │   │
│       │   ├── Template/
│       │   │   ├── ITemplateService.cs
│       │   │   └── TemplateService.cs
│       │   │
│       │   ├── Delay/
│       │   │   ├── IDelayService.cs
│       │   │   └── DelayService.cs
│       │   │
│       │   ├── Logging/
│       │   │   ├── IActivityLogger.cs
│       │   │   └── ActivityLogger.cs
│       │   │
│       │   ├── Config/
│       │   │   ├── IConfigService.cs
│       │   │   └── ConfigService.cs
│       │   │
│       │   ├── Security/
│       │   │   ├── IEncryptionService.cs
│       │   │   └── EncryptionService.cs     # AES-256 encrypt/decrypt cookies
│       │   │
│       │   └── Recovery/
│       │       ├── IRecoveryService.cs
│       │       └── RecoveryService.cs        # Auto-resume tasks after restart
│       │
│       ├── Platforms/                  # Platform-specific automation
│       │   └── Threads/
│       │       ├── ThreadsAutomation.cs       # Main orchestrator
│       │       ├── ThreadsActions.cs          # Implementasi aksi
│       │       ├── ThreadsSelectors.cs        # CSS/XPath selectors
│       │       ├── ThreadsConstants.cs        # URL, rate limits
│       │       └── ThreadsHelpers.cs          # Utility functions
│       │
│       ├── ViewModels/                 # MVVM ViewModels
│       │   ├── MainViewModel.cs
│       │   ├── DashboardViewModel.cs
│       │   ├── AccountListViewModel.cs
│       │   ├── AccountDetailViewModel.cs
│       │   ├── TaskListViewModel.cs
│       │   ├── TemplateEditorViewModel.cs
│       │   ├── SchedulerViewModel.cs
│       │   ├── LogViewModel.cs
│       │   └── SettingsViewModel.cs
│       │
│       ├── Views/                      # WPF Views (XAML)
│       │   ├── DashboardView.xaml
│       │   ├── AccountListView.xaml
│       │   ├── AccountDetailView.xaml
│       │   ├── AddAccountDialog.xaml
│       │   ├── TaskListView.xaml
│       │   ├── TemplateEditorView.xaml
│       │   ├── SchedulerView.xaml
│       │   ├── LogView.xaml
│       │   ├── SettingsView.xaml
│       │   └── Components/
│       │       ├── AccountCard.xaml
│       │       ├── StatusBadge.xaml
│       │       ├── ResourceGauge.xaml
│       │       └── ActivityFeed.xaml
│       │
│       ├── Converters/                 # WPF Value Converters
│       │   ├── BoolToVisibilityConverter.cs
│       │   ├── StatusToColorConverter.cs
│       │   └── ...
│       │
│       ├── Themes/                     # Material Design themes
│       │   ├── DarkTheme.xaml
│       │   ├── LightTheme.xaml
│       │   └── SharedResources.xaml
│       │
│       └── Assets/                     # Icons, images
│           ├── Icons/
│           └── Images/
│
└── tests/
    └── SoMan.Tests/                    # Unit & Integration tests
        ├── Services/
        ├── Platforms/
        └── ViewModels/
```

---

## 5. Database Schema

### ERD (Entity Relationship)

```
┌─────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Account    │────<│ AccountCategoryMap│>────│ AccountCategory  │
├─────────────┤     └──────────────────┘     ├─────────────────┤
│ Id (PK)     │                               │ Id (PK)         │
│ Name        │     ┌──────────────────┐     │ Name            │
│ Platform    │────<│  AccountLink     │     │ Color           │
│ CookiesJson │     ├──────────────────┤     │ Description     │
│ ProxyId(FK) │     │ Id (PK)          │     │ CreatedAt       │
│ Status      │     │ AccountId1 (FK)  │     └─────────────────┘
│ Notes       │     │ AccountId2 (FK)  │
│ IsHeadless  │     │ LinkType         │
│ LastActive  │     │ Notes            │
│ CreatedAt   │     └──────────────────┘
│ UpdatedAt   │
└──────┬──────┘     ┌──────────────────┐
       │            │  ProxyConfig     │
       │            ├──────────────────┤
       └───────────>│ Id (PK)          │
                    │ Name             │
                    │ Type             │  ← HTTP, SOCKS5, VPN
                    │ Host             │
                    │ Port             │
                    │ Username         │
                    │ Password         │
                    │ IsActive         │
                    └──────────────────┘

┌──────────────────┐     ┌──────────────────┐
│ ActionTemplate   │────<│   ActionStep     │
├──────────────────┤     ├──────────────────┤
│ Id (PK)          │     │ Id (PK)          │
│ Name             │     │ TemplateId (FK)  │
│ Description      │     │ Order            │
│ Platform         │     │ ActionType       │  ← Like, Comment, Follow, dll
│ IsActive         │     │ Parameters (JSON)│  ← {target, text, count, dll}
│ CreatedAt        │     │ DelayMinMs       │
│ UpdatedAt        │     │ DelayMaxMs       │
└──────────────────┘     └──────────────────┘

┌──────────────────┐     ┌──────────────────┐
│ ScheduledTask    │     │  TaskExecution   │
├──────────────────┤     ├──────────────────┤
│ Id (PK)          │     │ Id (PK)          │
│ TemplateId (FK)  │     │ ScheduledTaskId  │
│ Name             │     │ AccountId (FK)   │
│ CronExpression   │     │ Status           │  ← Queued, Running, Done, Failed
│ AccountIds (JSON)│     │ StartedAt        │
│ CategoryIds(JSON)│     │ CompletedAt      │
│ IsEnabled        │     │ ErrorMessage     │
│ LastRunAt        │     │ Progress (%)     │
│ NextRunAt        │     └──────────────────┘
│ CreatedAt        │
└──────────────────┘

┌──────────────────┐
│  ActivityLog     │
├──────────────────┤
│ Id (PK)          │
│ AccountId (FK)   │
│ ActionType       │
│ Target           │  ← URL / username target
│ Result           │  ← Success, Failed, Skipped
│ Details          │  ← Detail tambahan (text comment, dll)
│ ScreenshotPath   │  ← Optional
│ ExecutedAt       │
└──────────────────┘
```

### Model C# Detail

#### Account.cs
```csharp
public class Account
{
    public int Id { get; set; }
    public string Name { get; set; }                    // Nama display
    public string Platform { get; set; }                // "Threads", "Facebook", dll
    public string Username { get; set; }                // Username di platform
    public string CookiesJson { get; set; }             // Cookies dalam format JSON
    public int? ProxyConfigId { get; set; }             // FK ke ProxyConfig (nullable)
    public AccountStatus Status { get; set; }           // Active, Suspended, NeedVerify, Error
    public string? Notes { get; set; }
    public bool IsHeadless { get; set; }                // Default mode untuk akun ini
    public DateTime? LastActiveAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ProxyConfig? ProxyConfig { get; set; }
    public ICollection<AccountCategoryMap> Categories { get; set; }
    public ICollection<ActivityLog> ActivityLogs { get; set; }
}

public enum AccountStatus
{
    Active,
    Suspended,
    NeedVerification,
    CookiesExpired,
    Error,
    Disabled
}
```

#### ActionStep.cs — Parameter JSON Examples
```json
// ActionType: Like
{ "count": 5, "source": "feed" }

// ActionType: Comment
{ "count": 3, "source": "feed", "texts": ["Nice!", "Great post!", "🔥🔥🔥"] }

// ActionType: Follow
{ "count": 10, "source": "suggested" }

// ActionType: Scroll
{ "durationSeconds": 120, "speed": "medium" }

// ActionType: CreatePost
{ "text": "Hello world!", "mediaPath": null }

// ActionType: Search
{ "keyword": "technology", "interactWithResults": true }

// ActionType: ViewProfile
{ "username": "targetuser" }

// ActionType: Repost
{ "count": 2, "source": "feed" }
```

---

## 6. Spesifikasi Modul

### 6.1 BrowserManager

**Tanggung jawab:** Mengelola lifecycle browser instance dan BrowserContext.

```
BrowserManager
├── InitializeAsync()              → Install/launch Playwright browser
├── CreateContextAsync(account)    → Buat BrowserContext baru untuk akun
├── CloseContextAsync(accountId)   → Tutup context akun tertentu
├── CloseAllAsync()                → Tutup semua browser & context
├── GetActiveContextCount()        → Jumlah context yang sedang aktif
├── GetMaxAvailableSlots()         → Hitung slot tersedia berdasarkan resource
└── IsContextAlive(accountId)      → Cek apakah context masih hidup
```

**Logika Smart Allocation:**
```
Method: GetMaxAvailableSlots()

1. totalRAM = SystemInfo.GetTotalPhysicalMemory()
2. usedRAM = SystemInfo.GetUsedPhysicalMemory()
3. freeRAM = totalRAM - usedRAM
4. cpuUsage = SystemInfo.GetCpuUsagePercent()

5. safetyBuffer = totalRAM * 0.20          // Sisakan 20% RAM untuk OS
6. availableRAM = freeRAM - safetyBuffer

7. IF cpuUsage > 85%:
       return 0  // CPU terlalu sibuk, jangan buka browser baru

8. memPerContext = isHeadless ? 50MB : 120MB  // Estimasi per context
9. maxByRAM = availableRAM / memPerContext

10. maxByCPU = (100 - cpuUsage) / 2          // ~2% CPU per context

11. return MIN(maxByRAM, maxByCPU)
```

**BrowserContext Configuration per akun:**
```csharp
var contextOptions = new BrowserNewContextOptions
{
    UserAgent = RandomUserAgent(),     // Randomisasi (jika anti-detection aktif)
    ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
    Locale = "en-US",
    TimezoneId = "Asia/Jakarta",
    Proxy = account.ProxyConfig != null ? new Proxy
    {
        Server = $"{account.ProxyConfig.Type}://{account.ProxyConfig.Host}:{account.ProxyConfig.Port}",
        Username = account.ProxyConfig.Username,
        Password = account.ProxyConfig.Password
    } : null
};

// Inject cookies setelah context dibuat
var context = await browser.NewContextAsync(contextOptions);
var cookies = JsonSerializer.Deserialize<List<Cookie>>(account.CookiesJson);
await context.AddCookiesAsync(cookies);
```

### 6.2 AccountService

**Tanggung jawab:** CRUD akun, import cookies, status management.

```
AccountService
├── AddAccountAsync(name, platform, cookies, proxy?)
├── UpdateAccountAsync(account)
├── DeleteAccountAsync(accountId)
├── GetAllAccountsAsync(platform?)
├── GetAccountsByCategory(categoryId)
├── ImportCookiesAsync(accountId, cookiesJson)
├── ValidateCookiesAsync(accountId)          → Buka browser, cek login valid
├── UpdateStatusAsync(accountId, status)
├── GetAccountHealthAsync(accountId)         → Cek apakah akun masih aktif
└── ExportAccountsAsync(format)              → Export ke JSON
```

**Format Cookies yang Diterima:**
```json
[
    {
        "name": "sessionid",
        "value": "xxx",
        "domain": ".threads.net",
        "path": "/",
        "expires": 1720000000,
        "httpOnly": true,
        "secure": true,
        "sameSite": "None"
    },
    ...
]
```

User bisa import cookies dari:
- File JSON (format array of cookies)
- Paste langsung dari browser extension (EditThisCookie, Cookie-Editor)
- Netscape/HTTP cookie file format (auto-convert)

### 6.3 ProxyManager

**Tanggung jawab:** Kelola proxy configuration.

```
ProxyManager
├── AddProxyAsync(name, type, host, port, user?, pass?)
├── UpdateProxyAsync(proxy)
├── DeleteProxyAsync(proxyId)
├── GetAllProxiesAsync()
├── TestProxyAsync(proxyId)              → Cek koneksi + dapatkan IP
├── AssignProxyToAccount(proxyId, accountId)
├── RemoveProxyFromAccount(accountId)
├── GetProxyForAccount(accountId)
└── ImportProxiesAsync(text)             → Bulk import: host:port:user:pass per line
```

**Supported Proxy Types:**
- HTTP/HTTPS
- SOCKS5
- VPN Config (file .ovpn — diteruskan ke OpenVPN client)

### 6.4 TaskEngine

**Tanggung jawab:** Eksekusi template aksi pada akun-akun terpilih.

```
TaskEngine
├── ExecuteTemplateAsync(templateId, accountIds[], mode)
│   mode: Manual | Scheduled
├── StopExecutionAsync(executionId)
├── StopAllAsync()
├── PauseAsync(executionId)
├── ResumeAsync(executionId)
├── GetRunningTasks()
├── GetQueuedTasks()
└── GetExecutionStatus(executionId)
```

**Alur Eksekusi:**
```
ExecuteTemplateAsync(templateId, [akun1, akun2, ..., akun50])

1. Load template + action steps
2. Untuk setiap akun:
   a. Cek ResourceMonitor → ada slot?
      - Ya → lanjut
      - Tidak → masukkan ke queue, tunggu slot kosong
   b. BrowserManager.CreateContextAsync(akun)
   c. Untuk setiap ActionStep dalam template (berurutan):
      i.   ThreadsAutomation.ExecuteAction(context, step)
      ii.  DelayService.WaitRandomAsync(step.DelayMinMs, step.DelayMaxMs)
      iii. ActivityLogger.LogAsync(akun, step, result)
      iv.  Cek apakah ada error/block → handle gracefully
   d. BrowserManager.CloseContextAsync(akun)
   e. Emit progress event → UI update

3. Semua akun selesai → TaskExecution.Status = Completed
```

**Concurrency:**
- Gunakan `SemaphoreSlim` untuk membatasi concurrent contexts
- Jumlah max concurrent = `ResourceMonitor.GetMaxAvailableSlots()`
- Dinamis: jika resource berkurang, jangan buka context baru sampai slot tersedia

### 6.5 SchedulerService

**Tanggung jawab:** Jadwalkan eksekusi template pada waktu tertentu.

```
SchedulerService
├── CreateScheduleAsync(name, templateId, accountIds, cronExpression)
├── UpdateScheduleAsync(schedule)
├── DeleteScheduleAsync(scheduleId)
├── EnableScheduleAsync(scheduleId)
├── DisableScheduleAsync(scheduleId)
├── GetAllSchedulesAsync()
├── GetUpcomingRunsAsync(count)
├── TriggerNowAsync(scheduleId)          → Manual trigger untuk testing
└── GetScheduleHistoryAsync(scheduleId)
```

**Cron Expression Examples:**
```
"0 0 8 * * ?"      → Setiap hari jam 08:00
"0 0 */2 * * ?"    → Setiap 2 jam
"0 30 9 ? * MON-FRI"→ Senin-Jumat jam 09:30
"0 0 8,12,18 * * ?" → Jam 08:00, 12:00, 18:00
```

Menggunakan **Quartz.NET** sebagai scheduler engine. Jobs di-persist ke SQLite agar survive app restart.

### 6.6 TemplateService

**Tanggung jawab:** CRUD template aksi.

```
TemplateService
├── CreateTemplateAsync(name, platform, description)
├── UpdateTemplateAsync(template)
├── DeleteTemplateAsync(templateId)
├── DuplicateTemplateAsync(templateId)
├── GetAllTemplatesAsync(platform?)
├── AddStepAsync(templateId, actionType, parameters, delayMin, delayMax)
├── UpdateStepAsync(step)
├── RemoveStepAsync(stepId)
├── ReorderStepsAsync(templateId, newOrder[])
└── ExportTemplateAsync(templateId) → JSON
```

**Contoh Template:**
```
Template: "Morning Engagement Routine"
Platform: Threads
Steps:
  1. Scroll Feed      | duration: 60s      | delay: 5-10s
  2. Like             | count: 5, random   | delay: 15-45s
  3. Comment          | count: 2, random   | delay: 30-60s
  4. View Profile     | count: 3, random   | delay: 10-20s
  5. Follow           | count: 2, suggested| delay: 20-40s
  6. Scroll Feed      | duration: 30s      | delay: 5-10s
```

### 6.7 DelayService

**Tanggung jawab:** Inject delay yang human-like antara aksi.

```
DelayService
├── WaitAsync(minMs, maxMs)              → Random delay antara min-max
├── WaitBetweenAccountsAsync()           → Delay antar switch akun
├── GetRandomDelay(minMs, maxMs)         → Hitung delay tanpa wait
└── ApplyJitter(baseDelayMs, jitterPct)  → Tambah variasi ±jitterPct%
```

**Global Delay Settings (di AppSettings):**
```json
{
    "delay": {
        "globalMinMs": 3000,
        "globalMaxMs": 10000,
        "betweenAccountsMinMs": 5000,
        "betweenAccountsMaxMs": 15000,
        "jitterPercent": 20,
        "enableHumanSimulation": true
    }
}
```

Jika `enableHumanSimulation = true`:
- Delay mengikuti distribusi normal (bukan uniform random)
- Sesekali ada "pause panjang" (simulasi user distracted)
- Kecepatan aksi bervariasi (tidak mekanis)

### 6.8 CategoryService

**Tanggung jawab:** Pengelompokan akun.

```
CategoryService
├── CreateCategoryAsync(name, color, description)
├── UpdateCategoryAsync(category)
├── DeleteCategoryAsync(categoryId)
├── GetAllCategoriesAsync()
├── AssignAccountToCategory(accountId, categoryId)
├── RemoveAccountFromCategory(accountId, categoryId)
├── GetAccountsByCategory(categoryId)
└── GetCategoriesForAccount(accountId)
```

**Satu akun bisa masuk banyak kategori** (many-to-many). Contoh:
- Kategori: "Niche Tech", "New Accounts", "High Engagement"
- Akun "john_tech01" → [Niche Tech, New Accounts]

### 6.9 AccountLinkerService

**Tanggung jawab:** Tautkan relasi antar akun.

```
AccountLinkerService
├── LinkAccountsAsync(accountId1, accountId2, linkType, notes?)
├── UnlinkAccountsAsync(linkId)
├── GetLinkedAccounts(accountId)
├── GetAllLinksAsync()
└── GetLinksByType(linkType)
```

**Link Types:**
```csharp
public enum LinkType
{
    SamePerson,          // Akun berbeda platform milik orang yang sama
    SameGroup,           // Akun dalam 1 kelompok operasional
    InteractWith,        // Akun A harus berinteraksi dengan akun B
    DoNotInteract,       // Akun A JANGAN berinteraksi dengan akun B
    MasterSlave          // Akun utama + akun pendukung
}
```

### 6.10 ActivityLogger

**Tanggung jawab:** Catat semua aksi yang dilakukan.

```
ActivityLogger
├── LogAsync(accountId, actionType, target, result, details?)
├── GetLogsForAccount(accountId, dateRange?, actionType?)
├── GetRecentLogs(count)
├── GetDailySummary(date)
├── GetAccountStats(accountId)            → Total likes, comments, dll
├── ExportLogsAsync(dateRange, format)    → CSV/JSON
└── CleanupOldLogs(olderThanDays)
```

### 6.11 ResourceMonitor

**Tanggung jawab:** Monitoring real-time resource sistem.

```
ResourceMonitor
├── GetCpuUsageAsync()           → % CPU usage
├── GetMemoryInfoAsync()         → Total, Used, Free, Percentage
├── GetAvailableSlots()          → Berapa browser context bisa dibuka
├── StartMonitoring(intervalMs)  → Mulai polling berkala
├── StopMonitoring()
├── OnResourceCritical           → Event: resource hampir habis
└── OnResourceRecovered          → Event: resource kembali normal
```

**Threshold:**
- CPU > 85% → Jangan buka context baru
- RAM free < 20% → Jangan buka context baru
- CPU > 95% atau RAM free < 10% → **Pause** semua task yang bukan essential

---

## 7. Desain UI

### 7.1 Layout Utama

```
┌──────────────────────────────────────────────────────────────┐
│  ≡ SoMan                          ☀/🌙  _  □  ✕            │
├────────┬─────────────────────────────────────────────────────┤
│        │                                                      │
│  📊    │   [Konten berubah sesuai menu yang dipilih]          │
│  Dash  │                                                      │
│        │                                                      │
│  👥    │                                                      │
│  Akun  │                                                      │
│        │                                                      │
│  ▶️    │                                                      │
│  Task  │                                                      │
│        │                                                      │
│  📋    │                                                      │
│  Tmpl  │                                                      │
│        │                                                      │
│  📅    │                                                      │
│  Jadwal│                                                      │
│        │                                                      │
│  📜    │                                                      │
│  Log   │                                                      │
│        │                                                      │
│  ⚙️    │                                                      │
│  Sett  │                                                      │
│        │                                                      │
├────────┴─────────────────────────────────────────────────────┤
│  Status Bar: Running: 12/50 │ CPU: 45% │ RAM: 6.2/16GB      │
└──────────────────────────────────────────────────────────────┘
```

### 7.2 Dashboard View

```
┌──────────────────────────────────────────────────────────┐
│  Dashboard                                                │
├──────────────────────────────────────────────────────────┤
│                                                           │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐    │
│  │ Total    │ │ Active   │ │ Running  │ │ Errors   │    │
│  │ Accounts │ │ Accounts │ │ Tasks    │ │ Today    │    │
│  │   124    │ │    98    │ │   12/50  │ │    3     │    │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘    │
│                                                           │
│  ┌─── Resource Monitor ───────────────────────────────┐  │
│  │  CPU  [████████░░░░░░░░░] 45%                      │  │
│  │  RAM  [██████████░░░░░░░] 62% (10.1/16.0 GB)      │  │
│  │  Slots Available: 38                                │  │
│  └────────────────────────────────────────────────────┘  │
│                                                           │
│  ┌─── Running Tasks ─────────────────────────────────┐   │
│  │  Account          Action         Progress  Status │   │
│  │  @user_001        Morning Eng..  ████░ 75%  🔄    │   │
│  │  @user_002        Like & Scrl..  ██░░░ 40%  🔄    │   │
│  │  @user_003        Comment..      █░░░░ 20%  🔄    │   │
│  │  ... 9 more running                               │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  ┌─── Recent Activity ───────────────────────────────┐   │
│  │  10:30  @user_001  Liked post by @target_user     │   │
│  │  10:31  @user_002  Commented "Great post!"        │   │
│  │  10:32  @user_001  Followed @another_user         │   │
│  │  10:33  @user_003  ❌ Failed to like (timeout)    │   │
│  │  ...                                              │   │
│  └───────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

### 7.3 Account List View

```
┌──────────────────────────────────────────────────────────┐
│  Accounts                    [+ Add Account] [Import]     │
├──────────────────────────────────────────────────────────┤
│  Filter: [All Platforms ▼] [All Categories ▼] [Search 🔍]│
│                                                           │
│  ┌────────────────────────────────────────────────────┐  │
│  │ ☑ │ @user_001    │ Threads │ 🟢 Active │ Niche-Tech│  │
│  │ ☑ │ @user_002    │ Threads │ 🟢 Active │ New-Accts │  │
│  │ ☑ │ @user_003    │ Threads │ 🟡 Verify │ Niche-Tech│  │
│  │ ☑ │ @user_004    │ Threads │ 🔴 Error  │ -         │  │
│  │   │ ...                                             │  │
│  └────────────────────────────────────────────────────┘  │
│                                                           │
│  Selected: 3  [▶ Run Template] [🏷 Set Category] [🗑 Del]│
│                                                           │
│  ┌─── Account Detail (klik untuk expand) ────────────┐   │
│  │  Username: @user_001                               │   │
│  │  Platform: Threads                                 │   │
│  │  Proxy: socks5://proxy1.example.com:1080           │   │
│  │  Categories: Niche-Tech, High-Engagement           │   │
│  │  Last Active: 10 minutes ago                       │   │
│  │  Today: 15 likes, 5 comments, 2 follows            │   │
│  │  Linked: @user_001_fb (Facebook, SamePerson)       │   │
│  │                                                     │   │
│  │  [Edit] [Test Login] [View Logs] [Open Browser]    │   │
│  └───────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

### 7.4 Add Account Dialog

```
┌─────────────────────────────────────────┐
│  Add New Account                    ✕   │
├─────────────────────────────────────────┤
│                                         │
│  Account Name:  [                    ]  │
│  Platform:      [Threads           ▼]  │
│  Username:      [                    ]  │
│                                         │
│  Cookies:                               │
│  ┌─────────────────────────────────┐   │
│  │  Paste JSON cookies here...      │   │
│  │                                  │   │
│  │                                  │   │
│  └─────────────────────────────────┘   │
│  [📁 Import from File]                  │
│                                         │
│  Proxy (optional):                      │
│  [None                             ▼]  │
│  [+ Add New Proxy]                      │
│                                         │
│  Mode:  ○ Headed  ● Headless           │
│                                         │
│  Category (optional):                   │
│  [☑ Niche-Tech] [☐ New-Accts] [+ New]  │
│                                         │
│  Notes:                                 │
│  [                                   ]  │
│                                         │
│        [Cancel]  [Test Login]  [Save]   │
└─────────────────────────────────────────┘
```

### 7.5 Template Editor View

```
┌──────────────────────────────────────────────────────────┐
│  Template Editor                                          │
├──────────────────────────────────────────────────────────┤
│                                                           │
│  Name: [Morning Engagement Routine              ]        │
│  Platform: [Threads ▼]   Status: [Active ▼]              │
│  Description: [Routine like, comment, follow pagi hari]  │
│                                                           │
│  ┌─── Action Steps ──────────────────────────────────┐   │
│  │                                                    │   │
│  │  ① Scroll Feed                                     │   │
│  │     Duration: 60s │ Speed: Medium                  │   │
│  │     Delay after: 5000-10000ms                      │   │
│  │     [Edit] [Delete] [↑] [↓]                        │   │
│  │  ─────────────────────────────────────────────     │   │
│  │  ② Like Posts                                      │   │
│  │     Count: 5 │ Source: Feed (random)               │   │
│  │     Delay after: 15000-45000ms                     │   │
│  │     [Edit] [Delete] [↑] [↓]                        │   │
│  │  ─────────────────────────────────────────────     │   │
│  │  ③ Comment on Posts                                │   │
│  │     Count: 2 │ Source: Feed (random)               │   │
│  │     Texts: ["Nice!", "Great post!", "🔥🔥"]       │   │
│  │     Delay after: 30000-60000ms                     │   │
│  │     [Edit] [Delete] [↑] [↓]                        │   │
│  │  ─────────────────────────────────────────────     │   │
│  │  ④ Follow Users                                    │   │
│  │     Count: 2 │ Source: Suggested                   │   │
│  │     Delay after: 20000-40000ms                     │   │
│  │     [Edit] [Delete] [↑] [↓]                        │   │
│  │                                                    │   │
│  │  [+ Add Step]                                      │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  [Cancel]  [Duplicate]  [Save]  [▶ Run Now]              │
└──────────────────────────────────────────────────────────┘
```

### 7.6 Scheduler View

```
┌──────────────────────────────────────────────────────────┐
│  Scheduler                              [+ New Schedule]  │
├──────────────────────────────────────────────────────────┤
│                                                           │
│  ┌────────────────────────────────────────────────────┐  │
│  │ Name              Template         Schedule   ON/OFF│  │
│  ├────────────────────────────────────────────────────┤  │
│  │ Morning Run       Morning Eng..    08:00 daily  🟢 │  │
│  │ Midday Engage     Like & Scroll    12:00 daily  🟢 │  │
│  │ Evening Post      Auto Post        18:00 M-F   🟢 │  │
│  │ Weekend Boost     Full Engage      10:00 S-S   🔴 │  │
│  └────────────────────────────────────────────────────┘  │
│                                                           │
│  ┌─── Schedule Detail ───────────────────────────────┐   │
│  │  Name: Morning Run                                 │   │
│  │  Template: Morning Engagement Routine              │   │
│  │  Schedule: Every day at 08:00                      │   │
│  │  Accounts: All in category "Niche-Tech" (45 akun)  │   │
│  │  Last Run: Today 08:00 (Success: 43, Failed: 2)    │   │
│  │  Next Run: Tomorrow 08:00                          │   │
│  │                                                     │   │
│  │  [Edit] [Delete] [▶ Run Now] [View History]        │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  ┌─── Upcoming Runs ─────────────────────────────────┐   │
│  │  12:00  Midday Engage     → 30 accounts            │   │
│  │  18:00  Evening Post      → 45 accounts            │   │
│  │  Tomorrow 08:00  Morning  → 45 accounts            │   │
│  └───────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

### 7.7 Settings View

```
┌──────────────────────────────────────────────────────────┐
│  Settings                                                 │
├──────────────────────────────────────────────────────────┤
│                                                           │
│  ┌─── General ───────────────────────────────────────┐   │
│  │  Theme:          [Dark ▼]                          │   │
│  │  Language:       [Indonesia ▼]                     │   │
│  │  Start with OS:  [☐]                               │   │
│  │  Minimize to tray: [☑]                             │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  ┌─── Browser ───────────────────────────────────────┐   │
│  │  Default Mode:       [Headless ▼]                  │   │
│  │  Browser Engine:     [Chromium ▼]                  │   │
│  │  Max Concurrent:     [Auto (based on resource) ▼]  │   │
│  │  Manual Override:    [   ] contexts                │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  ┌─── Delay ─────────────────────────────────────────┐   │
│  │  Between Actions:    Min [3000]ms  Max [10000]ms   │   │
│  │  Between Accounts:   Min [5000]ms  Max [15000]ms   │   │
│  │  Jitter:             [20]%                         │   │
│  │  Human Simulation:   [☑]                           │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  ┌─── Resource Limits ───────────────────────────────┐   │
│  │  Max CPU Usage:      [85]%                         │   │
│  │  Min Free RAM:       [20]%                         │   │
│  │  Critical CPU:       [95]%                         │   │
│  │  Critical RAM:       [10]%                         │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  ┌─── Proxy / VPN ───────────────────────────────────┐   │
│  │  ┌────────────────────────────────────────────┐   │   │
│  │  │ Name        Type     Host          Status  │   │   │
│  │  │ Proxy-US1   SOCKS5   us1.proxy..   🟢     │   │   │
│  │  │ Proxy-UK1   HTTP     uk1.proxy..   🟢     │   │   │
│  │  │ VPN-SG      VPN      sg.vpn...     🔴     │   │   │
│  │  └────────────────────────────────────────────┘   │   │
│  │  [+ Add Proxy] [Import Bulk] [Test All]            │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│  ┌─── Data ──────────────────────────────────────────┐   │
│  │  Database Location: C:\Users\...\SoMan\soman.db   │   │
│  │  Log Retention: [30] days                          │   │
│  │  [Export All Data] [Import Data] [Clean Old Logs]  │   │
│  └───────────────────────────────────────────────────┘   │
│                                                           │
│                              [Reset to Default]  [Save]   │
└──────────────────────────────────────────────────────────┘
```

---

## 8. Fase Pengembangan

> **Terakhir diupdate:** 16 April 2026

### Fase 1: Foundation (Core + Data Layer) ✅ SELESAI
**Status: Selesai**

- [x] Setup project (.NET 8 WPF, NuGet packages)
- [x] Setup Material Design theme (dark/light)
- [x] Buat Models & DbContext (EF Core + SQLite)
- [x] Initial migration (EnsureCreated)
- [x] MainWindow shell + sidebar navigation (7 menu)
- [x] Basic MVVM infrastructure (DI, navigation service, CommunityToolkit.Mvvm)
- [x] Settings View + ConfigService (global settings, 17 default values)
- [x] EncryptionService (AES-256 untuk cookies)
- [x] ResourceMonitor (CPU via PerformanceCounter, RAM via P/Invoke)
- [x] BrowserManager (Playwright context management)
- [x] DelayService (human-like delay, Box-Muller distribution)
- [x] ActivityLogger (log ke database)
- [x] RecoveryService (resume pending tasks)
- [x] Dashboard View (summary cards, resource monitor, recent activity)
- [x] Global exception handler (MessageBox, tidak force close)

### Fase 2: Account Management ✅ SELESAI
**Status: Selesai**

- [x] AccountService (CRUD, enkripsi cookies, status management)
- [x] Account List View (DataGrid, filter platform/category/status/search)
- [x] Add/Edit Account Dialog (form + cookie import, proxy dropdown, headless toggle, **category picker**)
- [x] **Kolom Category di tabel akun** — menampilkan dot warna + nama kategori per akun
- [ ] Account Detail View (panel detail saat klik akun — info lengkap, linked accounts)
- [x] CategoryService + Category management UI (di Settings, **dengan color picker visual**)
- [x] AccountLinkerService (backend selesai)
- [ ] Account linking UI (belum ada UI untuk link antar akun)
- [x] ProxyManager + Proxy management di Settings (add, delete, bulk import)
- [ ] Import/export akun (bulk import dari file)

**Bug fixes yang sudah diterapkan:**
- Fixed: XAML crash (MathConverter, BooleanToVisibilityConverter, LeadingIcon)
- Fixed: DbContext stale state (semua service pakai fresh DbContext per operasi)
- Fixed: Resource Monitor tidak update saat startup (DispatcherTimer 3 detik)
- Fixed: Namespace conflict Account/Proxy di BrowserManager

### Fase 3: Browser Engine ✅ SELESAI
**Status: Semua komponen selesai dan sudah diuji runtime. Browser berhasil membuka Threads dengan cookies.**

- [x] Playwright installation & setup (NuGet + Chromium v1148 untuk .NET Playwright SDK)
- [x] BrowserManager (create/close context) — dual browser (headed + headless), anti-detection
- [x] ResourceMonitor (CPU, RAM monitoring) — auto-update + ResourceCritical/Recovered events
- [x] **Smart allocation logic** — CanLaunchMore() + GetAvailableSlots() berdasarkan CPU & RAM, **min 1 browser selalu diizinkan**
- [x] **Cookie injection & session validation** — flexible parser (support Cookie-Editor, EditThisCookie, dll)
- [x] **Proxy integration per context** — HTTP & SOCKS5 per akun, password decrypted
- [x] Dashboard View - resource monitoring gauge — sudah ada
- [x] Browser action bar di Account List (Open/Close/Validate/CloseAll buttons)
- [x] **Checkbox multi-select** — centang akun di DataGrid, Select All header, bulk open/close/validate
- [x] **Mobile viewport** — browser ukuran HP (390x844 iPhone 14) agar berjejer saat banyak akun
- [x] **Auto-position windows** — window otomatis berjejer dari kiri ke kanan via CDP
- [x] **SessionValidator v2** — cek login popup (desktop mode) + cek authenticated UI elements
- [x] **Browser running indicator** — kolom "Browser" di DataGrid, dot hijau On / abu-abu Off per akun
- [x] **Status message fix** — pesan resource-blocked yang jelas, tidak lagi stale cookie count

**Bug fixes sesi ini:**
- Fixed: Cookie parsing error `'%' is an invalid end of a number` — diganti ke flexible JsonNode parser yang support semua format export browser extension (expirationDate, sameSite string, dll)
- Fixed: Tombol browser tidak responsif (silent return) — ditambah feedback status untuk semua kasus
- Fixed: Playwright .NET butuh chromium-1148 (bukan chromium-1217 dari Node.js) — diinstall via playwright.ps1
- Fixed: SessionValidator false positive — versi mobile Threads tampilkan login banner walau sudah login; validator sekarang pakai `forceDesktop: true` (viewport 1280x720 + desktop UA)
- Fixed: Browser commands support multi-select — centang beberapa akun lalu Open Browser sekaligus

### Fase 4: Threads Automation ✅ SELESAI
**Status: Core automation engine selesai, semua action type sudah diimplementasi.**

- [x] ThreadsAutomation class structure (orchestrator + step executor)
- [x] ThreadsSelectors (CSS selectors untuk semua elemen Threads UI)
- [x] ThreadsConstants (URLs, rate limits, timeouts, scroll settings)
- [x] ThreadsActions (low-level browser actions)
- [x] Login via cookies + session check (`IsLoggedInAsync`)
- [x] Scroll feed (`ScrollFeedAsync` — durasi-based, human-like delay)
- [x] Like post (`LikeFeedPostsAsync` — skip already liked, scroll-load more)
- [x] Comment on post (`CommentOnFeedPostsAsync` — random text selection, error recovery)
- [x] Follow user (`FollowUserAsync` + `FollowFromSuggestedAsync`)
- [x] Unfollow user (`UnfollowUserAsync` — with confirm dialog)
- [x] Create post (`CreatePostAsync` — text-only)
- [x] Repost (`RepostFeedPostsAsync` — via repost popup menu)
- [x] View profile (`ViewProfileAsync` — navigate + scroll to simulate reading)
- [x] Search keyword (`SearchAndInteractAsync` — search + optional result interaction)
- [x] Error handling (TimeoutException, PlaywrightException, activity logging per action)
- [x] IBrowserManager.GetPage() — public method to retrieve active page for automation
- [x] DI registration (ThreadsAutomation as transient service)

**File structure:**
```
Platforms/Threads/
├── ThreadsAutomation.cs   — Orchestrator, ExecuteStepAsync dispatcher
├── ThreadsActions.cs      — Low-level browser actions (scroll, like, comment, etc.)
├── ThreadsSelectors.cs    — CSS selectors (easy to update)
└── ThreadsConstants.cs    — URLs, rate limits, timeouts
```

### Fase 5: Template & Task Engine 🔄 DALAM PENGERJAAN
**Status: Backend selesai, UI selesai, sedang testing & bug fixing**

- [x] TemplateService (CRUD — create, update, delete, duplicate, add/edit/delete/reorder steps)
- [x] Template Editor View (template list panel + detail panel + step DataGrid)
- [x] ActionStep parameter forms per action type (dynamic form: count, duration, texts, username, keyword, interact toggle)
- [x] Template/Step dialog (Grid overlay approach — bukan DialogHost, karena DialogHost breaks DataContext di WPF)
- [x] TaskEngine (execution, pause/resume/stop, progress events, DB logging)
- [x] DelayService (random delay, human simulation) — backend sudah ada dari Fase 1
- [x] Task List View (template/account picker, Run/RunAll/StopAll, running tasks grid, execution history)
- [x] Manual trigger (Run Now button) — tested, scroll automation berjalan
- [x] Pause/Resume/Stop task buttons — tombol sudah diperbaiki (toggle Pause/Play + visual feedback)
- [x] DI Registration (ITemplateService, ITaskEngine)
- [x] Converters (NullToVisibility, InverseNullToVisibility, CountToVisibility)

**Bug fixes Fase 5:**
- Fixed: CommunityToolkit.Mvvm `[RelayCommand]` strips "Async" suffix — `SaveTemplateAsync()` generates `SaveTemplateCommand`, NOT `SaveTemplateAsyncCommand`. All 9+ XAML command bindings were wrong.
- Fixed: DialogHost.DialogContent renders in separate visual tree → commands lose DataContext. Solution: Grid overlay with dark backdrop Border.
- Fixed: Duplicate Style attribute (MC3024) — TextBox had both `Style=` attribute and `<TextBox.Style>` element.
- Fixed: `SoMan.Services.Task` namespace conflicted with `System.Threading.Tasks.Task` → renamed to `Services/Execution`.
- Fixed: Action buttons in DataGrid used `AncestorType=DataGrid` (unreliable) → changed to `AncestorType=UserControl`.
- Fixed: Pause/Play buttons both visible simultaneously → now toggle via DataTrigger on `IsPaused` property.
- Fixed: No visual feedback on action buttons → changed from `MaterialDesignIconForegroundButton` to `MaterialDesignIconButton` with colored icons (orange Pause, green Play, red Stop).

**⚠ BELUM DITES (lanjut besok):**
- [ ] Test Pause/Resume/Stop buttons berfungsi saat task running
- [ ] Test template CRUD (create, edit, delete, duplicate)
- [ ] Test step CRUD (add, edit, delete, reorder)
- [ ] Hapus debug code (Debug.WriteLine di ViewModel, Click handler di code-behind) setelah semua berfungsi

### Fase 6: Scheduler & Logging ⬜ BELUM DIMULAI
**Status: Belum dimulai**

- [ ] SchedulerService (Quartz.NET integration)
- [ ] Scheduler View (CRUD schedules)
- [ ] Cron expression builder UI
- [ ] ActivityLogger — backend sudah ada
- [ ] Log View (filter, search, export)
- [ ] Dashboard View - activity feed
- [ ] Dashboard View - statistics cards

### Fase 7: Polish & Stability ⬜ BELUM DIMULAI
**Status: Belum dimulai**

- [x] Error handling global — sudah diterapkan
- [ ] Retry logic untuk aksi yang gagal
- [x] Graceful shutdown (tutup semua browser saat app ditutup) — sudah ada di App.OnExit
- [ ] System tray integration
- [ ] Startup with Windows option
- [ ] Export/import all data
- [ ] Performance optimization
- [ ] UI polish & edge cases
- [ ] Testing (unit + integration)

---

## 9. Catatan Pengembangan Platform Selanjutnya

> **Catatan untuk development platform baru (Facebook, Instagram, Pinterest, dll):**
>
> Saat ingin menambah platform baru, langkah-langkah:
>
> 1. **Baca dokumen ini** untuk memahami arsitektur keseluruhan
> 2. **Review kode Threads** di folder `Platforms/Threads/` sebagai referensi
> 3. **Buat folder baru** di `Platforms/{PlatformName}/`
> 4. **Implement class-class** yang setara:
>    - `{Platform}Automation.cs` — Orchestrator
>    - `{Platform}Actions.cs` — Implementasi aksi
>    - `{Platform}Selectors.cs` — CSS/XPath selectors
>    - `{Platform}Constants.cs` — URL, rate limits
> 5. **Tambah enum value** di `Platform` dan `ActionType` yang relevan
> 6. **Refactor jika perlu**: Jika ada pola yang sama antara 2+ platform,
>    ekstrak ke shared base class / interface di folder `Platforms/Shared/`
>
> **Yang TIDAK perlu dibuat ulang:**
> - BrowserManager, ResourceMonitor (sudah platform-agnostic)
> - AccountService, CategoryService, AccountLinkerService
> - TaskEngine, SchedulerService, TemplateService
> - DelayService, ActivityLogger
> - Semua UI kecuali platform-specific action forms
>
> Arsitektur sengaja dibuat modular agar penambahan platform baru
> hanya menyentuh folder `Platforms/` dan sedikit modifikasi UI.

---

## Lampiran

### A. Threads Selectors Reference (Contoh Awal)

> **PENTING:** Selectors Threads.net bisa berubah kapan saja karena update platform.
> File `ThreadsSelectors.cs` harus mudah di-update tanpa mengubah logic.

```csharp
// Contoh struktur (akan di-research detail saat development)
public static class ThreadsSelectors
{
    // Feed
    public const string FeedContainer = "main[role='main']";
    public const string PostItem = "article";
    
    // Actions
    public const string LikeButton = "[aria-label='Like']";
    public const string CommentInput = "[aria-label='Reply']";
    public const string FollowButton = "text=Follow";
    public const string RepostButton = "[aria-label='Repost']";
    
    // Navigation
    public const string SearchInput = "[aria-label='Search']";
    public const string ProfileLink = "[aria-label='Profile']";
    
    // Post Creation
    public const string NewPostButton = "[aria-label='Create']";
    public const string PostTextArea = "[role='textbox']";
    public const string PostSubmitButton = "text=Post";
}
```

### B. Konfigurasi Default Aplikasi

```json
{
    "app": {
        "theme": "Dark",
        "language": "id",
        "startWithWindows": false,
        "minimizeToTray": true
    },
    "browser": {
        "defaultMode": "Headless",
        "engine": "Chromium",
        "maxConcurrent": "auto",
        "manualMaxOverride": null
    },
    "delay": {
        "betweenActionsMinMs": 3000,
        "betweenActionsMaxMs": 10000,
        "betweenAccountsMinMs": 5000,
        "betweenAccountsMaxMs": 15000,
        "jitterPercent": 20,
        "enableHumanSimulation": true
    },
    "resource": {
        "maxCpuPercent": 85,
        "minFreeRamPercent": 20,
        "criticalCpuPercent": 95,
        "criticalFreeRamPercent": 10
    },
    "logging": {
        "retentionDays": 30,
        "enableScreenshots": false
    }
}
```

---

*Dokumen ini akan di-update seiring perkembangan proyek.*
