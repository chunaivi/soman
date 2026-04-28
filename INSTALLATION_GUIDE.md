# SoMan — Panduan Instalasi

## Daftar Isi

1. [Kebutuhan Sistem](#1-kebutuhan-sistem)
2. [Instalasi di PC Lokal](#2-instalasi-di-pc-lokal)
3. [Instalasi di Windows VPS](#3-instalasi-di-windows-vps)
4. [Konfigurasi Awal](#4-konfigurasi-awal)
5. [Update Aplikasi](#5-update-aplikasi)
6. [Troubleshooting](#6-troubleshooting)

---

## 1. Kebutuhan Sistem

### Minimum

| Komponen | Spesifikasi |
|---|---|
| OS | Windows 10/11 atau Windows Server 2019+ |
| CPU | 2 core |
| RAM | 4 GB |
| Storage | 500 MB (app) + 1 GB (browser engine) |
| Runtime | .NET 8 Desktop Runtime |
| Koneksi | Internet stabil |

### Rekomendasi (untuk 50+ akun simultan)

| Komponen | Spesifikasi |
|---|---|
| OS | Windows 11 atau Windows Server 2022 |
| CPU | 4-8 core |
| RAM | 8-16 GB |
| Storage | SSD 10 GB+ |
| Koneksi | 50+ Mbps |

### Estimasi Resource per Akun

| Mode | RAM per Context | CPU per Context |
|---|---|---|
| Headless | ~30-60 MB | ~1-2% |
| Headed | ~80-150 MB | ~2-4% |

**Rumus cepat:**  
Akun simultan ≈ (RAM free × 0.8) ÷ RAM per context  
Contoh: 8 GB free → headless → (8000 × 0.8) ÷ 50 ≈ **128 akun simultan**

---

## 2. Instalasi di PC Lokal

### Langkah 1: Install .NET 8 Desktop Runtime

1. Buka https://dotnet.microsoft.com/download/dotnet/8.0
2. Download **.NET Desktop Runtime 8.x** (bukan SDK, kecuali ingin develop)
3. Jalankan installer, ikuti instruksi
4. Verifikasi: Buka PowerShell, ketik:
   ```powershell
   dotnet --list-runtimes
   ```
   Pastikan ada `Microsoft.WindowsDesktop.App 8.x.x`

### Langkah 2: Install SoMan

**Opsi A: Installer (Recommended)**

1. Download `SoMan-Setup-vX.X.X.exe` dari release
2. Jalankan installer
3. Pilih lokasi instalasi (default: `C:\Program Files\SoMan`)
4. Selesai — shortcut akan muncul di Desktop dan Start Menu

**Opsi B: Portable (ZIP)**

1. Download `SoMan-Portable-vX.X.X.zip` dari release
2. Ekstrak ke folder pilihan (misal: `C:\SoMan`)
3. Jalankan `SoMan.exe`

### Langkah 3: Install Playwright Browser

Saat pertama kali dijalankan, SoMan akan otomatis mengunduh browser engine Playwright (Chromium). Proses ini membutuhkan:
- **~300 MB download**
- **~500 MB disk space**
- Koneksi internet

Jika auto-install gagal, jalankan manual via PowerShell:
```powershell
# Masuk ke folder instalasi SoMan
cd "C:\Program Files\SoMan"

# Install browser Playwright
.\playwright.ps1 install chromium
```

### Langkah 4: Jalankan SoMan

1. Buka `SoMan.exe` atau klik shortcut
2. Aplikasi akan membuat database di: `%APPDATA%\SoMan\soman.db`
3. Konfigurasi awal — lihat [Bab 4](#4-konfigurasi-awal)

---

## 3. Instalasi di Windows VPS

### 3.1 Pilih VPS Provider

Provider yang menyediakan Windows VPS:

| Provider | Catatan |
|---|---|
| Contabo | Murah, Windows Server tersedia |
| Hetzner | Performa bagus, perlu upload ISO sendiri |
| DigitalOcean | Droplet Windows tersedia |
| Vultr | Windows Server langsung tersedia |
| AWS Lightsail | Windows instances tersedia |
| Azure | Native Windows support |

**Rekomendasi minimal untuk VPS:**

| Akun Simultan | vCPU | RAM | Storage |
|---|---|---|---|
| 10-30 | 2 | 4 GB | 50 GB SSD |
| 30-100 | 4 | 8 GB | 80 GB SSD |
| 100-300 | 6 | 16 GB | 100 GB SSD |
| 300+ | 8+ | 32 GB+ | 150 GB SSD |

### 3.2 Setup Windows Server

#### A. Akses VPS via RDP

1. Dapatkan IP, username, dan password dari VPS provider
2. Di PC lokal, buka **Remote Desktop Connection** (mstsc.exe)
3. Masukkan IP VPS → Connect
4. Login dengan credentials

#### B. Konfigurasi Windows Server

Setelah masuk via RDP, jalankan PowerShell **sebagai Administrator**:

```powershell
# 1. Set timezone (sesuaikan)
Set-TimeZone -Id "SE Asia Standard Time"

# 2. Disable IE Enhanced Security (agar bisa download)
$AdminKey = "HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}"
$UserKey = "HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\{A509B1A8-37EF-4b3f-8CFC-4F3A74704073}"
Set-ItemProperty -Path $AdminKey -Name "IsInstalled" -Value 0
Set-ItemProperty -Path $UserKey -Name "IsInstalled" -Value 0

# 3. Install .NET 8 Desktop Runtime
Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "dotnet-install.ps1"
# Atau download manual dari browser

# 4. Disable Windows Firewall untuk outbound (opsional, jika proxy digunakan)
# Hati-hati: hanya jika VPS dalam jaringan aman
# Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False

# 5. Enable auto-login (opsional, agar SoMan otomatis jalan setelah restart)
$RegPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $RegPath -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path $RegPath -Name "DefaultUserName" -Value "Administrator"
Set-ItemProperty -Path $RegPath -Name "DefaultPassword" -Value "YOUR_PASSWORD"
```

> **PENTING:** Ganti `YOUR_PASSWORD` dengan password VPS Anda. Simpan password ini dengan aman.

#### C. Install SoMan di VPS

Sama seperti [Bab 2](#2-instalasi-di-pc-lokal), ikuti langkah 1-4.

#### D. Auto-Start SoMan saat Boot

Agar SoMan otomatis berjalan setelah VPS restart:

```powershell
# Buat shortcut di Startup folder
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\SoMan.lnk")
$Shortcut.TargetPath = "C:\Program Files\SoMan\SoMan.exe"
$Shortcut.WorkingDirectory = "C:\Program Files\SoMan"
$Shortcut.Save()
```

Dengan kombinasi **auto-login + auto-start + auto-recovery** (fitur bawaan SoMan), task yang tertunda akan dilanjutkan otomatis setelah VPS restart.

### 3.3 Keamanan VPS

**WAJIB dilakukan:**

1. **Ganti port RDP** dari default 3389:
   ```powershell
   Set-ItemProperty -Path "HKLM:\System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp" -Name "PortNumber" -Value 13389
   # Restart: Restart-Service TermService -Force
   ```

2. **Buat firewall rule** hanya izinkan IP Anda:
   ```powershell
   New-NetFirewallRule -DisplayName "RDP Custom" -Direction Inbound -LocalPort 13389 -Protocol TCP -Action Allow -RemoteAddress "YOUR_IP/32"
   ```

3. **Gunakan password yang kuat** (min 16 karakter, mixed case + angka + simbol)

4. **Aktifkan Windows Update** untuk patch keamanan

5. **Backup database** secara berkala:
   ```powershell
   # Buat scheduled task untuk backup harian
   $Action = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c copy `"$env:APPDATA\SoMan\soman.db`" `"C:\Backup\soman_%date:~-4,4%%date:~-10,2%%date:~-7,2%.db`""
   $Trigger = New-ScheduledTaskTrigger -Daily -At "03:00"
   Register-ScheduledTask -TaskName "SoMan Backup" -Action $Action -Trigger $Trigger -User "Administrator" -Password "YOUR_PASSWORD"
   ```

### 3.4 Monitoring VPS

Tips monitoring SoMan di VPS:

- **Task Manager** via RDP → lihat resource usage
- **SoMan Dashboard** → lihat status semua akun & task
- **Event Viewer** → cek crash/error Windows level
- **Opsional:** Setup notifikasi via email/Telegram jika ada error (fitur akan ditambahkan nanti)

---

## 4. Konfigurasi Awal

Setelah SoMan berhasil dijalankan pertama kali:

### 4.1 Pengaturan Umum

1. Buka **Settings** (ikon gear di sidebar)
2. Sesuaikan:
   - **Theme:** Dark / Light
   - **Browser Mode default:** Headless (recommended untuk VPS)
   - **Browser Engine:** Chromium (default, paling stabil)

### 4.2 Pengaturan Resource

1. Di **Settings → Resource Limits**:
   - Max CPU: 85% (default)
   - Min Free RAM: 20% (default)
   - Biarkan **Max Concurrent: Auto** agar SoMan menghitung sendiri

2. Untuk VPS dengan RAM terbatas, bisa turunkan:
   - Max CPU: 80%
   - Min Free RAM: 25%

### 4.3 Pengaturan Delay

1. Di **Settings → Delay**:
   - Between Actions: 3000-10000ms (default, aman)
   - Between Accounts: 5000-15000ms (default)
   - Human Simulation: ON (recommended)

2. **JANGAN** set delay terlalu rendah (< 2000ms). Risiko detected dan banned tinggi.

### 4.4 Setup Proxy (Opsional tapi Recommended)

1. Di **Settings → Proxy / VPN**
2. Klik **+ Add Proxy**
3. Isi:
   - Name: (nama untuk identifikasi, misal "US-Proxy-1")
   - Type: HTTP / SOCKS5
   - Host: (IP proxy)
   - Port: (port proxy)
   - Username & Password (jika ada)
4. Klik **Test** untuk verifikasi koneksi
5. Klik **Save**
6. Assign proxy ke akun di halaman Account

### 4.5 Tambah Akun Pertama

1. Buka halaman **Accounts**
2. Klik **+ Add Account**
3. Isi data dan paste cookies (lihat [User Guide](USER_GUIDE.md) untuk cara mendapatkan cookies)
4. Klik **Test Login** untuk verifikasi
5. Klik **Save**

---

## 5. Update Aplikasi

### Update Otomatis (Jika tersedia)

SoMan akan memberi notifikasi jika ada versi baru. Klik **Update** dan ikuti instruksi.

### Update Manual

1. Download versi baru dari release
2. Tutup SoMan yang sedang berjalan
3. **Backup database** terlebih dahulu:
   ```powershell
   Copy-Item "$env:APPDATA\SoMan\soman.db" "$env:APPDATA\SoMan\soman_backup.db"
   ```
4. Install versi baru (overwrite / extract ke folder yang sama)
5. Jalankan SoMan — database akan otomatis di-migrate jika ada perubahan schema

---

## 6. Troubleshooting

### Browser Playwright Tidak Bisa Di-install

**Gejala:** Error "Browser not found" atau "Failed to install Chromium"

**Solusi:**
```powershell
# Set environment variable untuk download location
$env:PLAYWRIGHT_BROWSERS_PATH = "C:\SoMan\browsers"

# Install manual
npx playwright install chromium
# atau
pwsh playwright.ps1 install chromium
```

### Aplikasi Tidak Bisa Dibuka

**Gejala:** Double-click tidak terjadi apa-apa, atau error .NET runtime

**Solusi:**
1. Pastikan .NET 8 Desktop Runtime terinstall:
   ```powershell
   dotnet --list-runtimes
   ```
2. Jika belum ada, download dan install dari https://dotnet.microsoft.com/download/dotnet/8.0

### RDP Disconnect = SoMan Berhenti?

**Gejala:** Saat RDP disconnect, SoMan berhenti atau browser freeze

**Solusi:** Ini terjadi karena Windows membatasi session yang disconnect. Fix:

```powershell
# Buat RDP tetap aktif saat disconnect
Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services" -Name "RemoteAppLogoffTimeLimit" -Value 0
Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services" -Name "MaxDisconnectionTime" -Value 0
Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services" -Name "MaxIdleTime" -Value 0
```

Atau gunakan tool seperti **TightVNC** sebagai alternatif RDP — session tidak terganggu saat disconnect.

### Database Corrupt

**Gejala:** Error "database is locked" atau "database disk image is malformed"

**Solusi:**
```powershell
# Backup file yang corrupt terlebih dahulu
Copy-Item "$env:APPDATA\SoMan\soman.db" "$env:APPDATA\SoMan\soman_corrupt.db"

# Coba repair dengan sqlite3
sqlite3 "$env:APPDATA\SoMan\soman.db" ".recover" | sqlite3 "$env:APPDATA\SoMan\soman_repaired.db"

# Ganti database
Move-Item "$env:APPDATA\SoMan\soman_repaired.db" "$env:APPDATA\SoMan\soman.db" -Force
```

### Akun Terdeteksi / Banned

**Gejala:** Akun diminta verifikasi, atau aksi gagal terus

**Solusi:**
1. **Naikkan delay** antar aksi (min 5000ms)
2. **Ganti proxy** — kemungkinan IP sudah di-flag
3. **Kurangi jumlah aksi** per hari per akun
4. **Aktifkan human simulation** di Settings → Delay
5. Untuk akun yang kena restrict, istirahatkan beberapa hari (set status Disabled)

---

*Dokumen ini akan di-update seiring perkembangan fitur SoMan.*
