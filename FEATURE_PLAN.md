# Acuris Desktop — Feature Plan (RFIDStockPro Parity)

Reference: **RFIDStockPro** (Android / Acuris) at `C:\Users\GT\Downloads\Acuris-2026-06-15-1~\Acuris-2026-06-15-1\RFIDStockPro`  
Desktop app: **RfidScanner** (WPF / .NET 4.8 x86) at `E:\rfid\RfidScanner`

Legend: `[x]` Done · `[~]` Partial · `[ ]` Not started

---

## Phase 1 — Foundation & Shell

| # | Feature | Status |
|---|---------|--------|
| 1.1 | WPF MVVM app (.NET 4.8 x86) | [x] |
| 1.2 | Login → Main → Logout → Login session loop | [x] |
| 1.3 | Light/dark theme | [x] |
| 1.4 | App logo / icon | [x] |
| 1.5 | Navigation shell (sidebar menu like Android drawer) | [x] |
| 1.6 | Dashboard home with quick tiles | [x] |
| 1.7 | Splash screen | [ ] |
| 1.8 | Role-based permission enforcement | [ ] |

---

## Phase 2 — Authentication & Account

| # | Feature | Status |
|---|---------|--------|
| 2.1 | Email/password login (Supabase users table) | [x] |
| 2.2 | AES password encryption | [x] |
| 2.3 | Deleted-user check on login | [x] |
| 2.4 | Logout (disconnect reader + return to login) | [x] |
| 2.5 | Sign up / registration | [ ] |
| 2.6 | OTP email verification | [ ] |
| 2.7 | Forgot password | [ ] |
| 2.8 | License key verification | [ ] |
| 2.9 | Device registration / device limits | [ ] |
| 2.10 | Supabase realtime auth/session events | [ ] |

---

## Phase 3 — User Management

| # | Feature | Status |
|---|---------|--------|
| 3.1 | List company users | [x] |
| 3.2 | Profile window (edit username) | [~] |
| 3.3 | Add user | [ ] |
| 3.4 | Edit user (name, role, permissions) | [ ] |
| 3.5 | Delete user (soft delete) | [ ] |
| 3.6 | Change user role | [ ] |
| 3.7 | Search users | [ ] |
| 3.8 | Profile image | [ ] |
| 3.9 | Show deleted user history | [ ] |

---

## Phase 4 — RFID Scanner (Chainway R6 BLE)

| # | Feature | Status |
|---|---------|--------|
| 4.1 | BLE device scan | [x] |
| 4.2 | Connect / disconnect | [x] |
| 4.3 | Inventory scan (EPC + TID) | [x] |
| 4.4 | Transmit power control | [x] |
| 4.5 | Live tag list (EPC, TID, Type, RSSI) | [x] |
| 4.6 | Tag filter (text + min RSSI) | [x] |
| 4.7 | Scan history (SQLite) | [x] |
| 4.8 | CSV export | [x] |
| 4.9 | Simulation mode | [x] |
| 4.10 | Keyboard wedge | [x] |
| 4.11 | Cloud sync (HTTP POST queue) | [~] |
| 4.12 | Inventory EPC-only mode toggle | [ ] |
| 4.13 | EPC+TID+USER mode | [ ] |
| 4.14 | Single tag read | [ ] |
| 4.15 | Write tag (EPC/TID/USER) | [ ] |
| 4.16 | Lock / Kill tag | [ ] |
| 4.17 | Tag locate (proximity) | [ ] |
| 4.18 | Barcode scan module | [ ] |
| 4.19 | UHF firmware version display | [ ] |
| 4.20 | Firmware update (DFU) | [ ] |
| 4.21 | Reader hardware button callback | [ ] |
| 4.22 | Battery level indicator | [ ] |
| 4.23 | UART mode (integrated handheld) | [ ] |

---

## Phase 5 — Product Management

| # | Feature | Status |
|---|---------|--------|
| 5.1 | Dynamic Supabase product table (`{company}_product`) | [x] |
| 5.2 | Product list / stock view | [x] |
| 5.3 | Product search & filters | [x] |
| 5.4 | Product details view | [ ] |
| 5.5 | Add product (bind RFID tag) | [ ] |
| 5.6 | Edit product | [ ] |
| 5.7 | Delete product | [ ] |
| 5.8 | Stock status (In Stock / Out of Stock) | [ ] |
| 5.9 | Mark as Sold / Return to stock | [ ] |
| 5.10 | Product images / video (Supabase Storage) | [ ] |
| 5.11 | Labels & locations management | [ ] |
| 5.12 | Locate product by RFID | [ ] |
| 5.13 | Update RFID tag on product | [ ] |
| 5.14 | Offline product cache | [ ] |
| 5.15 | Inventory reconciliation scan | [ ] |

---

## Phase 6 — Sell Workflow

| # | Feature | Status |
|---|---------|--------|
| 6.1 | Sell scan (RFID + barcode) | [ ] |
| 6.2 | Customer name / company / comment | [ ] |
| 6.3 | Sales history list | [ ] |
| 6.4 | Sales details | [ ] |
| 6.5 | Bulk sell preview | [ ] |
| 6.6 | Mark items sold (single/bulk) | [ ] |
| 6.7 | Export sell (PDF/CSV/Excel) | [ ] |
| 6.8 | Google Sheet / Excel bulk mark-as-sold | [ ] |

---

## Phase 7 — Return Workflow

| # | Feature | Status |
|---|---------|--------|
| 7.1 | Return scan | [ ] |
| 7.2 | Return history | [ ] |
| 7.3 | Return details | [ ] |
| 7.4 | Bulk return preview | [ ] |
| 7.5 | Mark items returned | [ ] |
| 7.6 | Export return details | [ ] |

---

## Phase 8 — Memo (Consignment)

| # | Feature | Status |
|---|---------|--------|
| 8.1 | Memo management list | [ ] |
| 8.2 | Memo scanner | [ ] |
| 8.3 | Memo detail (sell/return from memo) | [ ] |
| 8.4 | Bulk memo preview | [ ] |
| 8.5 | Memo filters (In Stock / Sold / Memo / Unmatched) | [ ] |
| 8.6 | Export memo (PDF/CSV/Excel/email) | [ ] |
| 8.7 | Import Excel for memo | [ ] |

---

## Phase 9 — In/Out Tracker (Collections)

| # | Feature | Status |
|---|---------|--------|
| 9.1 | In/Out tracker home | [ ] |
| 9.2 | Create collection | [ ] |
| 9.3 | Add products to collection | [ ] |
| 9.4 | Track collection (RFID scan) | [ ] |
| 9.5 | List products in collection | [ ] |
| 9.6 | Delete collection | [ ] |

---

## Phase 10 — Bulk Upload

| # | Feature | Status |
|---|---------|--------|
| 10.1 | Excel bulk upload (.xls/.xlsx) | [ ] |
| 10.2 | Google Sheet import | [ ] |
| 10.3 | Column header mapping | [ ] |
| 10.4 | Review / preview before upload | [ ] |
| 10.5 | Duplicate SKU detection | [ ] |
| 10.6 | Bulk RFID label print | [ ] |

---

## Phase 11 — Reports & Analytics

| # | Feature | Status |
|---|---------|--------|
| 11.1 | Dashboard charts (weekly/monthly/yearly) | [ ] |
| 11.2 | Scan inventory reports | [ ] |
| 11.3 | Scan details drill-down | [ ] |
| 11.4 | Date range & user filters | [ ] |
| 11.5 | Stock analysis screen | [ ] |
| 11.6 | Activity timeline | [ ] |
| 11.7 | Export reports (PDF/CSV/Excel) | [ ] |

---

## Phase 12 — Settings & Configuration

| # | Feature | Status |
|---|---------|--------|
| 12.1 | Settings screen | [ ] |
| 12.2 | Manage labels | [ ] |
| 12.3 | Manage locations | [ ] |
| 12.4 | RFID device mode (BLE settings) | [~] |
| 12.5 | Printer configuration | [ ] |

---

## Phase 13 — Backend & Sync

| # | Feature | Status |
|---|---------|--------|
| 13.1 | Supabase users CRUD | [~] |
| 13.2 | Supabase products CRUD | [~] |
| 13.3 | Supabase sales / returns / memo tables | [ ] |
| 13.4 | Supabase reports table | [ ] |
| 13.5 | Supabase Storage (media) | [ ] |
| 13.6 | Supabase realtime (products/users) | [ ] |
| 13.7 | Offline sync / cache refresh | [ ] |
| 13.8 | AWS DynamoDB legacy support | [ ] |
| 13.9 | AWS → Supabase migration utility | [ ] |

---

## Progress Summary

| Phase | Done | Partial | Not Started |
|-------|------|---------|-------------|
| 1 Foundation | 6 | 0 | 2 |
| 2 Auth | 4 | 0 | 6 |
| 3 Users | 1 | 1 | 7 |
| 4 RFID Scanner | 10 | 1 | 12 |
| 5 Products | 3 | 0 | 12 |
| 6 Sell | 0 | 0 | 8 |
| 7 Return | 0 | 0 | 6 |
| 8 Memo | 0 | 0 | 7 |
| 9 Collections | 0 | 0 | 6 |
| 10 Bulk Upload | 0 | 0 | 6 |
| 11 Reports | 0 | 0 | 7 |
| 12 Settings | 0 | 1 | 4 |
| 13 Backend | 0 | 2 | 7 |

**Overall:** ~22 done · ~6 partial · ~90 not started (of ~118 tracked items)

---

## Implementation Order (Recommended)

1. **Phase 1** — Complete shell navigation + dashboard tiles  
2. **Phase 5** — Product list, details, add/edit (core inventory)  
3. **Phase 6–8** — Sell, Return, Memo workflows  
4. **Phase 4** — Advanced RFID (write, lock, locate)  
5. **Phase 10–11** — Bulk upload + reports  
6. **Phase 2–3** — Full auth + user admin parity  
7. **Phase 9, 12–13** — Collections, settings, full sync  

---

## Build & Run

```powershell
cd E:\rfid\RfidScanner
$env:DOTNET_SKIP_WORKLOAD_INTEGRATION='1'
& "C:\Program Files\dotnet\dotnet.exe" build
& "C:\Program Files\dotnet\dotnet.exe" run
```

Output: `E:\rfid\RfidScanner\bin\Debug\net48\RfidScanner.exe`

---

*Last updated: June 2026 — update checkboxes as features are completed.*
