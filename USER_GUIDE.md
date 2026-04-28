# SoMan — Panduan Penggunaan

## Daftar Isi

1. [Memulai Aplikasi](#1-memulai-aplikasi)
2. [Dashboard](#2-dashboard)
3. [Manajemen Akun](#3-manajemen-akun)
4. [Template Aksi](#4-template-aksi)
5. [Menjalankan Task](#5-menjalankan-task)
6. [Scheduler (Penjadwalan)](#6-scheduler-penjadwalan)
7. [Proxy & VPN](#7-proxy--vpn)
8. [Kategori & Pengelompokan](#8-kategori--pengelompokan)
9. [Penautkan Akun](#9-penautkan-akun)
10. [Activity Log](#10-activity-log)
11. [Pengaturan](#11-pengaturan)
12. [Tips & Best Practices](#12-tips--best-practices)

---

## 1. Memulai Aplikasi

### Tampilan Utama

Setelah membuka SoMan, Anda akan melihat:

```
┌──────────────────────────────────────────────┐
│  ≡ SoMan                          ☀/🌙  _ □ ✕│
├────────┬─────────────────────────────────────┤
│        │                                      │
│  📊 Dashboard                                │
│  👥 Accounts                                 │
│  ▶️ Tasks                                     │
│  📋 Templates                                │
│  📅 Scheduler                                │
│  📜 Logs                                     │
│  ⚙️ Settings                                 │
│        │                                      │
├────────┴─────────────────────────────────────┤
│  Status Bar                                   │
└──────────────────────────────────────────────┘
```

- **Sidebar kiri:** Navigasi antar halaman
- **Area utama:** Konten sesuai halaman yang dipilih
- **Status bar bawah:** Running tasks, CPU, RAM real-time
- **Tombol ☀/🌙:** Toggle tema terang/gelap

---

## 2. Dashboard

Dashboard adalah halaman utama yang menampilkan ringkasan status SoMan.

### Informasi yang Ditampilkan

| Bagian | Keterangan |
|---|---|
| **Summary Cards** | Total akun, akun aktif, task berjalan, error hari ini |
| **Resource Monitor** | CPU usage, RAM usage, slot browser tersedia |
| **Running Tasks** | Daftar task yang sedang berjalan dengan progress |
| **Recent Activity** | Log aktivitas terbaru secara real-time |

### Cara Membaca Resource Monitor

- **CPU < 60%:** Aman, bisa tambah task
- **CPU 60-85%:** Normal, SoMan mengelola sendiri
- **CPU > 85%:** SoMan akan pause task baru sampai CPU turun
- **RAM:** Sama seperti CPU, SoMan auto-throttle jika mendekati batas

---

## 3. Manajemen Akun

### 3.1 Cara Mendapatkan Cookies

Sebelum menambah akun, Anda perlu mengambil cookies dari browser:

#### Metode 1: Extension Browser (Recommended)

1. Install extension **Cookie-Editor** atau **EditThisCookie** di Chrome/Firefox
2. Login ke **threads.net** di browser
3. Buka extension → klik **Export** → pilih format **JSON**
4. Copy hasil export

#### Metode 2: Browser DevTools

1. Login ke **threads.net** di browser
2. Tekan `F12` → buka tab **Application** (Chrome) atau **Storage** (Firefox)
3. Di sidebar kiri, klik **Cookies** → `https://www.threads.net`
4. Catat cookies penting:
   - `sessionid`
   - `csrftoken`
   - `ds_user_id`
   - `ig_did`
   - `mid`
5. Format menjadi JSON:
   ```json
   [
       {
           "name": "sessionid",
           "value": "NILAI_SESSION_ID",
           "domain": ".threads.net",
           "path": "/",
           "httpOnly": true,
           "secure": true,
           "sameSite": "None"
       },
       {
           "name": "csrftoken",
           "value": "NILAI_CSRF",
           "domain": ".threads.net",
           "path": "/",
           "secure": true,
           "sameSite": "None"
       }
   ]
   ```

> **Catatan:** Cookies akan dienkripsi (AES-256) saat disimpan ke database.  
> Cookies dapat expired. Jika akun menunjukkan status "Cookies Expired", Anda perlu mengambil cookies baru.

### 3.2 Menambah Akun

1. Buka halaman **Accounts**
2. Klik tombol **+ Add Account** di pojok kanan atas
3. Isi form:

   | Field | Keterangan | Wajib? |
   |---|---|---|
   | Account Name | Nama untuk identifikasi (bebas) | Ya |
   | Platform | Pilih "Threads" | Ya |
   | Username | Username di Threads (tanpa @) | Ya |
   | Cookies | Paste JSON cookies | Ya |
   | Proxy | Pilih proxy (jika ada) | Tidak |
   | Mode | Headed / Headless | Ya (default: Headless) |
   | Category | Pilih kategori | Tidak |
   | Notes | Catatan tambahan | Tidak |

4. Klik **Test Login** — SoMan akan membuka browser, inject cookies, dan cek apakah login berhasil
5. Jika berhasil (status 🟢), klik **Save**

### 3.3 Status Akun

| Status | Warna | Keterangan |
|---|---|---|
| Active | 🟢 Hijau | Akun siap digunakan |
| Cookies Expired | 🟡 Kuning | Perlu update cookies |
| Need Verification | 🟡 Kuning | Platform minta verifikasi |
| Suspended | 🔴 Merah | Akun di-suspend oleh platform |
| Error | 🔴 Merah | Error tidak diketahui |
| Disabled | ⚫ Abu | Dinonaktifkan oleh user |

### 3.4 Mengedit Akun

1. Klik akun di daftar → panel detail muncul di bagian bawah
2. Klik **Edit**
3. Ubah data yang diperlukan
4. Klik **Save**

### 3.5 Menghapus Akun

1. Pilih akun (bisa multi-select dengan checkbox)
2. Klik **Delete** (ikon tempat sampah)
3. Konfirmasi penghapusan

> **Perhatian:** Menghapus akun juga menghapus semua activity log akun tersebut.

### 3.6 Import & Export Akun

**Import (Bulk):**
1. Klik **Import** di halaman Accounts
2. Pilih file JSON dengan format:
   ```json
   [
       {
           "name": "Akun 1",
           "platform": "Threads",
           "username": "user_001",
           "cookies": [{ "name": "sessionid", "value": "..." }],
           "proxy": null,
           "isHeadless": true,
           "categories": ["Niche-Tech"],
           "notes": ""
       }
   ]
   ```
3. SoMan akan memproses dan menambahkan semua akun

**Export:**
1. Pilih akun yang ingin di-export (atau Select All)
2. Klik **Export**
3. Pilih lokasi file → Save as JSON

---

## 4. Template Aksi

Template adalah urutan aksi yang bisa dijalankan berulang kali pada banyak akun.

### 4.1 Membuat Template Baru

1. Buka halaman **Templates**
2. Klik **+ New Template**
3. Isi:
   - **Name:** Nama template (misal: "Morning Engagement")
   - **Platform:** Threads
   - **Description:** Deskripsi singkat

### 4.2 Menambah Action Step

1. Di Template Editor, klik **+ Add Step**
2. Pilih jenis aksi:

| Action | Parameter | Keterangan |
|---|---|---|
| **Scroll Feed** | Duration (detik), Speed (slow/medium/fast) | Scroll halaman beranda |
| **Like** | Count (jumlah), Source (feed/profile/search) | Like post |
| **Comment** | Count, Source, Texts (daftar teks komentar) | Komentar pada post |
| **Follow** | Count, Source (suggested/search/profile) | Follow user |
| **Unfollow** | Count, Source (following list) | Unfollow user |
| **Create Post** | Text, Media path (optional) | Buat post baru |
| **Repost** | Count, Source (feed) | Repost/share post |
| **View Profile** | Username (atau random from feed) | Kunjungi profil user |
| **Search** | Keyword, Interact with results (yes/no) | Pencarian keyword |

3. Set **delay** sesudah aksi ini:
   - **Min delay:** Waktu minimum (ms) sebelum aksi berikutnya
   - **Max delay:** Waktu maksimum (ms) — SoMan akan random antara min-max
4. Klik **Add**

### 4.3 Mengatur Urutan Step

- Gunakan tombol **↑** dan **↓** untuk mengubah urutan
- Aksi dijalankan secara berurutan dari atas ke bawah

### 4.4 Contoh Template

#### Template: "Engagement Ringan"
```
1. Scroll Feed     | 60 detik, speed: medium    | delay 5-10s
2. Like            | 3 post dari feed           | delay 15-30s
3. View Profile    | 2 random dari feed         | delay 10-20s
```

#### Template: "Engagement Aktif"
```
1. Scroll Feed     | 120 detik, speed: slow     | delay 5-10s
2. Like            | 10 post dari feed          | delay 10-25s
3. Comment         | 3 post, teks random        | delay 30-60s
4. Follow          | 5 user dari suggested      | delay 20-40s
5. Scroll Feed     | 60 detik                   | delay 5-10s
6. Repost          | 2 post dari feed           | delay 15-30s
```

#### Template: "Auto Post"
```
1. Create Post     | teks: "Good morning! ☀️"  | delay 5-10s
2. Scroll Feed     | 30 detik                   | delay 5s
3. Like            | 5 post                     | delay 10-20s
```

### 4.5 Duplikasi Template

1. Buka template yang ingin diduplikasi
2. Klik **Duplicate**
3. Template baru akan dibuat dengan nama "[Original] (Copy)"
4. Edit sesuai kebutuhan

---

## 5. Menjalankan Task

### 5.1 Manual Run (Langsung Jalankan)

**Dari halaman Templates:**
1. Buka template yang ingin dijalankan
2. Klik **▶ Run Now**
3. Dialog muncul — pilih akun yang akan menjalankan template:
   - **All Accounts:** Semua akun aktif
   - **By Category:** Pilih kategori
   - **Select Manually:** Pilih akun satu per satu
4. Klik **Start**

**Dari halaman Accounts:**
1. Pilih akun (multi-select dengan checkbox)
2. Klik **▶ Run Template**
3. Pilih template dari daftar
4. Klik **Start**

### 5.2 Melihat Task yang Berjalan

Halaman **Tasks** menampilkan:

| Kolom | Keterangan |
|---|---|
| Account | Akun yang menjalankan task |
| Template | Template yang sedang dijalankan |
| Current Step | Aksi yang sedang dilakukan |
| Progress | Persentase selesai (progress bar) |
| Status | 🔄 Running, ⏳ Queued, ✅ Done, ❌ Failed |
| Started | Waktu mulai |

### 5.3 Mengontrol Task

| Tombol | Fungsi |
|---|---|
| **⏸ Pause** | Pause task tertentu (bisa resume) |
| **▶ Resume** | Lanjutkan task yang di-pause |
| **⏹ Stop** | Hentikan task tertentu |
| **⏹ Stop All** | Hentikan semua task yang berjalan |

### 5.4 Antrian (Queue)

Jika resource tidak cukup untuk menjalankan semua akun sekaligus, akun selebihnya masuk ke **antrian**:

```
Contoh: 50 akun dipilih, resource hanya cukup untuk 20 simultan

Running:  [Akun1 🔄] [Akun2 🔄] ... [Akun20 🔄]
Queued:   [Akun21 ⏳] [Akun22 ⏳] ... [Akun50 ⏳]

Saat Akun1 selesai → Akun21 otomatis mulai
Saat Akun2 selesai → Akun22 otomatis mulai
... dan seterusnya
```

SoMan mengelola antrian ini secara otomatis berdasarkan resource yang tersedia.

### 5.5 Auto-Recovery

Jika aplikasi crash atau VPS restart saat ada task yang sedang berjalan:

1. Saat SoMan dibuka kembali, dialog akan muncul:
   ```
   "Ditemukan 15 task yang belum selesai dari sesi sebelumnya.
    Apakah ingin melanjutkan?"
   
   [Lanjutkan Semua]  [Pilih]  [Abaikan]
   ```
2. **Lanjutkan Semua:** Semua task tertunda akan di-resume
3. **Pilih:** Pilih manual task mana yang ingin dilanjutkan
4. **Abaikan:** Hapus semua task tertunda

---

## 6. Scheduler (Penjadwalan)

### 6.1 Membuat Jadwal Baru

1. Buka halaman **Scheduler**
2. Klik **+ New Schedule**
3. Isi form:

   | Field | Keterangan |
   |---|---|
   | Name | Nama jadwal (misal: "Morning Routine") |
   | Template | Pilih template aksi yang akan dijalankan |
   | Accounts | Pilih akun: All / By Category / Manual Select |
   | Schedule | Waktu eksekusi |

4. Pilih pola jadwal:

   | Pola | Contoh |
   |---|---|
   | **Setiap hari** | Jam 08:00 setiap hari |
   | **Hari tertentu** | Senin-Jumat jam 09:00 |
   | **Interval** | Setiap 2 jam |
   | **Custom cron** | `0 0 8,12,18 * * ?` (3x sehari) |

5. Klik **Save**

### 6.2 Mengelola Jadwal

| Aksi | Cara |
|---|---|
| **Enable/Disable** | Toggle switch ON/OFF di daftar |
| **Edit** | Klik jadwal → Edit |
| **Delete** | Klik jadwal → Delete |
| **Run Now** | Klik **▶ Run Now** untuk trigger manual (testing) |
| **View History** | Lihat riwayat eksekusi jadwal |

### 6.3 Melihat Upcoming Runs

Di bagian bawah halaman Scheduler, ditampilkan jadwal yang akan dijalankan berikutnya beserta estimasi waktunya.

---

## 7. Proxy & VPN

### 7.1 Mengapa Perlu Proxy?

Jika Anda menjalankan banyak akun dari 1 IP yang sama, platform akan mendeteksi aktivitas mencurigakan. Proxy memberikan **IP unik per akun** sehingga terlihat seperti pengguna berbeda dari lokasi berbeda.

### 7.2 Menambah Proxy

1. Buka **Settings → Proxy / VPN**
2. Klik **+ Add Proxy**
3. Isi:

   | Field | Keterangan |
   |---|---|
   | Name | Nama identifikasi (misal: "US-Proxy-1") |
   | Type | HTTP / SOCKS5 |
   | Host | IP atau hostname proxy |
   | Port | Port proxy |
   | Username | (Opsional) Username auth |
   | Password | (Opsional) Password auth |

4. Klik **Test** — SoMan akan verifikasi koneksi dan menampilkan IP proxy
5. Klik **Save**

### 7.3 Import Proxy Bulk

1. Klik **Import Bulk**
2. Paste daftar proxy, satu per baris, format:
   ```
   host:port
   host:port:username:password
   socks5://host:port:username:password
   ```
3. SoMan akan menambahkan semua proxy sekaligus

### 7.4 Mengassign Proxy ke Akun

1. Buka halaman **Accounts**
2. Edit akun → pilih proxy dari dropdown
3. Atau: Multi-select akun → klik **Set Proxy** → pilih proxy

### 7.5 VPN (Opsional)

Jika menggunakan VPN bukan proxy:
1. Di **Settings → Proxy / VPN**, klik **+ Add Proxy**
2. Type: VPN
3. Upload file `.ovpn`
4. SoMan akan menjalankan OpenVPN client di background

> **Catatan:** VPN = 1 IP untuk semua akun. Proxy per akun lebih aman untuk multi-akun.

---

## 8. Kategori & Pengelompokan

### 8.1 Membuat Kategori

1. Di halaman **Accounts**, klik **Manage Categories** (atau di Settings)
2. Klik **+ New Category**
3. Isi:
   - **Name:** Nama kategori (misal: "Niche-Tech", "New Accounts")
   - **Color:** Pilih warna label
   - **Description:** Deskripsi opsional

### 8.2 Mengassign Akun ke Kategori

- **Saat tambah akun:** Pilih kategori di form Add Account
- **Akun existing:** Edit akun → pilih kategori
- **Bulk assign:** Multi-select akun → klik **🏷 Set Category** → pilih kategori

### 8.3 Filter Berdasarkan Kategori

Di halaman Accounts:
1. Gunakan dropdown **Filter by Category**
2. Pilih kategori — hanya akun dalam kategori itu yang ditampilkan
3. Bisa dikombinasikan dengan search

### 8.4 Jalankan Template per Kategori

Saat menjalankan template aksi:
1. Pilih **By Category** pada pemilihan akun
2. Pilih satu atau beberapa kategori
3. Semua akun aktif dalam kategori tersebut akan menjalankan template

---

## 9. Penautkan Akun

### 9.1 Mengapa Tautkan Akun?

Penautkan berguna untuk:
- Menandai akun yang **milik orang yang sama** di platform berbeda
- Menandai akun yang **harus berinteraksi satu sama lain** (saling like, comment)
- Menandai akun yang **TIDAK BOLEH berinteraksi** (mencegah terdeteksi)
- Mengelompokkan akun utama + akun pendukung

### 9.2 Jenis Tautan

| Jenis | Keterangan |
|---|---|
| **Same Person** | Akun berbeda milik orang yang sama |
| **Same Group** | Akun dalam 1 kelompok operasional |
| **Interact With** | Akun A harus berinteraksi dengan akun B |
| **Do Not Interact** | Akun A TIDAK BOLEH berinteraksi dengan akun B |
| **Master-Slave** | Akun utama + akun pendukung |

### 9.3 Membuat Tautan

1. Buka detail akun → klik **Link Account**
2. Pilih akun tujuan dari daftar
3. Pilih jenis tautan
4. Tambah catatan (opsional)
5. Klik **Save**

### 9.4 Bagaimana Tautan Mempengaruhi Automation?

- **Interact With:** Saat akun A menjalankan template "Like", akun A akan memprioritaskan post dari akun B
- **Do Not Interact:** Saat akun A menjalankan template, akun A akan **melewatkan** post dari akun B
- **Master-Slave:** Akun-akun slave akan otomatis engage dengan post dari akun master

---

## 10. Activity Log

### 10.1 Melihat Log

1. Buka halaman **Logs**
2. Log menampilkan semua aksi yang dilakukan:

   | Kolom | Keterangan |
   |---|---|
   | Timestamp | Waktu aksi dilakukan |
   | Account | Akun yang melakukan |
   | Action | Jenis aksi (Like, Comment, dll) |
   | Target | URL/user target |
   | Result | ✅ Success / ❌ Failed / ⏭ Skipped |
   | Details | Detail tambahan (teks comment, error message, dll) |

### 10.2 Filter & Search

- **Filter by Account:** Lihat log khusus akun tertentu
- **Filter by Action:** Hanya tampilkan aksi tertentu (misal: hanya Comment)
- **Filter by Date:** Rentang tanggal
- **Filter by Result:** Hanya Success / hanya Failed
- **Search:** Cari keyword dalam detail

### 10.3 Export Log

1. Set filter sesuai kebutuhan
2. Klik **Export**
3. Pilih format: **CSV** atau **JSON**
4. Simpan file

### 10.4 Statistik Akun

Di detail akun → klik **View Logs**, ditampilkan:
- Total like hari ini / minggu ini / bulan ini
- Total comment, follow, unfollow, post
- Grafik aktivitas harian
- Rasio sukses/gagal

---

## 11. Pengaturan

### 11.1 Umum

| Setting | Keterangan | Default |
|---|---|---|
| Theme | Dark / Light | Dark |
| Start with Windows | Jalankan saat Windows boot | OFF |
| Minimize to tray | Minimize ke system tray, bukan close | ON |

### 11.2 Browser

| Setting | Keterangan | Default |
|---|---|---|
| Default Mode | Headed / Headless | Headless |
| Browser Engine | Chromium / Firefox / WebKit | Chromium |
| Max Concurrent | Auto / Manual override | Auto |

### 11.3 Delay

| Setting | Keterangan | Default |
|---|---|---|
| Between Actions | Min-Max delay antar aksi (ms) | 3000-10000 |
| Between Accounts | Min-Max delay antar akun (ms) | 5000-15000 |
| Jitter | Variasi tambahan (%) | 20% |
| Human Simulation | Delay mengikuti pola manusia | ON |

### 11.4 Resource Limits

| Setting | Keterangan | Default |
|---|---|---|
| Max CPU | Batas CPU sebelum pause task baru | 85% |
| Min Free RAM | Minimum RAM free | 20% |
| Critical CPU | CPU kritis, pause semua task | 95% |
| Critical RAM Free | RAM kritis, pause semua | 10% |

### 11.5 Data

| Setting | Keterangan | Default |
|---|---|---|
| Log Retention | Hapus log lebih lama dari X hari | 30 hari |
| Export All Data | Export seluruh database ke JSON | — |
| Import Data | Import dari file export | — |
| Clean Old Logs | Hapus log lama secara manual | — |

---

## 12. Tips & Best Practices

### Keamanan Akun

1. **Gunakan proxy berbeda** untuk setiap akun (atau minimal per kelompok kecil)
2. **Jangan set delay terlalu rendah** — minimum 3 detik antar aksi
3. **Aktifkan Human Simulation** — membuat pola aktivitas lebih natural
4. **Batasi jumlah aksi per hari:**
   - Like: maks 50-100 per akun per hari
   - Comment: maks 20-30 per akun per hari
   - Follow: maks 30-50 per akun per hari
   - Post: maks 5-10 per akun per hari
5. **Rotasi waktu aktif** — jangan semua akun aktif di jam yang sama
6. **Istirahatkan akun** yang menunjukkan tanda-tanda restrict

### Performa

1. **Gunakan headless** untuk sebagian besar akun — hemat RAM signifikan
2. **Biarkan Max Concurrent di Auto** — SoMan akan mengoptimalkan sendiri
3. **Jangan jalankan aplikasi berat lain** bersamaan jika di VPS
4. **Monitor dashboard** secara berkala untuk memastikan tidak ada error bertumpuk

### Organisasi

1. **Kategorikan akun** berdasarkan niche/tujuan
2. **Buat template terpisah** untuk engagement dan posting
3. **Gunakan jadwal** yang berbeda untuk waktu pagi, siang, malam
4. **Tautkan akun** yang saling berinteraksi — SoMan akan membantu engagement silang
5. **Review log secara berkala** — deteksi pattern error sebelum menjadi masalah

### Backup

1. **Backup database** secara rutin (terutama sebelum update app)
2. **Export akun** ke file JSON sebagai backup terpisah
3. **Simpan cookies mentah** di tempat aman — jika database corrupt, tinggal import ulang

---

*Panduan ini akan di-update seiring penambahan fitur baru pada SoMan.*
