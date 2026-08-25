# S7Explorer

A WPF desktop application for communicating with **Siemens S7 series PLCs** (S7-200, S7-300, S7-400, S7-1200, S7-1500) over TCP/IP using the [S7.Net](https://github.com/S7NetPlus/s7netplus) library. Built with **.NET 8** and **WPF**.

---

## 🇬🇧 English

### What Does This Application Do?

**S7Explorer** is a modern, user-friendly SCADA/diagnostic tool that allows engineers and automation professionals to connect to Siemens S7 PLCs, read/write data block variables, manage symbolic addresses, and monitor PLC status — all from a Windows desktop interface.

### Key Features

| Feature | Description |
|---|---|
| **PLC Connection** | Connect to any S7-series PLC (S7-200 Smart, S7-300/400, S7-1200/1500) over TCP/IP with configurable IP, rack, slot and port |
| **DB Read / Write** | Read and write variables inside Siemens Data Blocks (DB) using both physical addresses (e.g. `DB1.DBX0.0`) and symbolic names |
| **DB Parser** | Parse Siemens SCL (`.db` / `.scl`) files to automatically extract variable structure, data types, byte offsets, and default values — exported to JSON |
| **Symbol Manager** | Add, edit, delete and persist symbolic address mappings (name → physical address + data type + description) stored as `symbols.json` |
| **Symbolic Addressing** | Select variables by their symbolic name from a dropdown instead of typing raw addresses; the mapper resolves them to physical PLC addresses automatically |
| **Manual Control Pages** | Operator panels for testing a machine by hand, declared as validated JSON files in `pages/` — pilot lamps, hold-to-run buttons, momentary triggers, handshakes and setpoints, with the write permission enforced by the application (see below) |
| **Batched Block Reading** | Cyclic pages read contiguous byte ranges in one PLC request instead of one request per symbol (~95 ms each on a real line), so a 30-symbol page refreshes in a single round trip |
| **Connection Settings** | Save and restore connection settings (CPU type, IP, rack, slot, port, theme, language) to/from `settings.json` |
| **Multi-language UI** | Full localization support via JSON language files (`lang/en-US.json`, `lang/tr-TR.json`, `lang/fr-FR.json`). Language can be changed at runtime |
| **Light / Dark Theme** | Switch between Light and Dark themes from the menu |
| **Left Panel Toggle** | Collapsible left panel for symbol tree navigation — expands/collapses with animation |
| **Status Indicator** | Real-time connection status with animated color indicator (green = connected, red = disconnected) |
| **Error Handling** | Descriptive error messages for connection failures, invalid addresses, type mismatches, and timeouts |

### Architecture & Technologies

- **Framework:** .NET 8 / WPF (Windows Presentation Foundation)
- **PLC Communication:** [S7.Net Plus](https://github.com/S7NetPlus/s7netplus) — open-source S7 protocol library
- **Serialization:** `System.Text.Json`
- **Pattern:** Code-behind with service classes (`PlcService`, `SymbolMapper`, `DbParser`, `ConnectionSettings`, `LocalizationManager`)
- **UI Pattern:** Event-driven, data-binding with `ObservableCollection`

### Manual Control Pages

The **Manual Test** button opens an operator panel used to move a machine by hand during commissioning — jog a cylinder, trigger a camera, start a drive, change a speed setpoint.

A panel is not compiled into the application. It is a **JSON definition file** in the `pages/` folder next to the executable, validated against `symbols.json` before it is allowed to run. Definitions are pure data — nothing in a page file ever executes.

#### Anatomy of a page file

```jsonc
{
  "schemaVersion": 1,
  "pageName": "Manual Control",

  "machine": {
    "db": 1,
    "writablePrefix": "DB_PC.Receive.",       // the ONLY area this page may write
    "lifeOut":     "DB_PC.Receive.Control.Life",
    "manualCmd":   "DB_PC.Receive.Control.Manual",
    "manualState": "DB_PC.Send.State.Manual", // machine's confirmation
    "pollIntervalMs": 500,
    "lifeIntervalMs": 1000,
    "lifeTimeoutMs":  2000
  },

  "controls": [
    { "type": "lamp",      "group": "Piston", "label": "FORWARD",
      "symbol": "DB_PC.Send.IO.Input.RejectFw" },
    { "type": "holdToRun", "group": "Piston", "label": "JOG FWD",
      "symbol": "DB_PC.Receive.Control.ManRejFw",
      "feedback": "DB_PC.Send.IO.Input.RejectFw" },
    { "type": "setpoint",  "group": "Main Conveyor", "label": "Speed",
      "symbol": "DB_PC.Receive.Velocity.Main",
      "feedback": "DB_PC.Send.Velocity.Main",
      "unit": "Hz", "scale": 0.1, "decimals": 1, "min": 0, "max": 50, "step": 0.5 }
  ],

  "alarmGroup": {
    "prefix":  "DB_PC.Send.Alarm.",           // every symbol below becomes an alarm lamp
    "summary": "DB_PC.Send.State.Error"
  }
}
```

#### Control types

| `type` | Direction | Behaviour |
|---|---|---|
| `lamp` | read | Pilot lamp for a BOOL. `color`: `green` (default), `red`, `yellow`, `blue` |
| `badge` | read | State chip beside a device — ACTIVE / PASSIVE |
| `numeric` | read | Large live measurement, formatted with `scale`, `decimals`, `unit` |
| `toggle` | write | On/off command bit; stays where the operator left it |
| `holdToRun` | write | Set only while the button is held; released on mouse-up, focus loss or page close |
| `momentary` | write | Single short pulse — trigger-style commands |
| `handshake` | write | App sets the bit, PLC clears it; progress reported through `busy` / `done`, failure after `timeoutMs` |
| `setpoint` | write | Numeric value with `min` / `max` / `step`; raw PLC value is the entry divided by `scale` |

Common fields: `label`, `group` (controls sharing a group are drawn under one header), `feedback` (read-back symbol — the page flags the control when the machine does not confirm), `icon` (Segoe MDL2 glyph), `confirm` (ask before sending), `requiresManualMode` (default `true`; see the manual-mode gate below).

#### Safety model

Writing to a live machine is gated at four independent points:

1. **Validation.** `ManualPageValidator` rejects the whole page if a command control writes outside `machine.writablePrefix`, targets a symbol missing from `symbols.json`, has a data type that does not match the control, declares a setpoint range the PLC type cannot hold, or lets two controls drive the same command bit. Errors block loading; warnings (no life bit, no manual-mode confirmation, a display reading the command area) are shown but allow it.
2. **Arming.** Monitoring a page never writes anything. The operator must tick **Enable writing** and confirm; the banner turns orange and the page announces that it can now command the PLC.
3. **Manual-mode confirmation.** While armed, commands stay locked until the machine reports `machine.manualState`. Each locked button shows *why* it is locked. Two kinds of command are exempt from this gate: the manual-mode request control itself — otherwise the gate would lock the only key that opens it — and any control declaring `"requiresManualMode": false`. That exemption exists for commands that come *before* manual mode in the operating sequence: recovering from an emergency stop, or the first power-up, both start with a reset that has to run while the machine still refuses to go to manual. Exempt controls still require arming, so they are never one click away.
4. **Runtime allowlist.** Every write is re-checked against the validator's approved symbol set, so a UI bug cannot send a command the page never declared.

Disarming, stopping monitoring, or closing the window clears the command bits first (numeric setpoints are left alone — they are parameters, not commands).

> ⚠️ **Watchdog.** `machine.lifeOut` is a handshake: the app writes 1, the PLC program pulls it back to 0. Reading a 0 proves the PLC is scanning. If the PLC program does **not** implement this handshake, nothing on the PLC side can drop a command bit that stayed set after a network drop or an application crash. Confirm this with the PLC program before arming writing on a machine that can move.

#### Panel size, language and theme

The page header carries the same language and theme selectors as the main window, plus a **panel selector**:

| Profile | Resolution | Class |
|---|---|---|
| 7" | 800 × 480 | KTP700 / TP700 / MTP700 |
| 10" | 1280 × 800 | MTP1000 Unified |

The choice is not cosmetic. Cards, buttons, lamps, steppers and fonts are all sized from the selected profile (`PanelProfile`), the controls are rebuilt, and the panel area is drawn at exactly the target resolution inside a bezel. If the page is taller than the panel, a warning states by how many pixels — an operator at the machine cannot scroll a membrane screen the way a mouse scrolls a window, so anything past the bottom edge is simply unreachable. The selection is remembered in `settings.json` (`panelSize`).

The theme dresses the window around the panel. The panel itself keeps the fixed `HmiStyle` palette in every theme, because on a machine screen red means fault regardless of what the rest of the application looks like.

#### Alarm strip

Every symbol under `alarmGroup.prefix` becomes an alarm, labelled from its description in `symbols.json`. Only *transitions* are recorded, so a standing alarm produces one timestamped line rather than one per cycle. When the condition goes away the line is not deleted — it is marked: the icon greys, the text is struck through and the time it cleared is added. Recent history is what tells an operator what happened while they were looking at the machine instead of the screen.

The **ACK** button in the alarm header removes the lines that have already cleared. Standing alarms are never removed by it, and the SYSTEM STATUS tile reads the live bits rather than the list, so acknowledging can tidy the strip but can never hide a fault that is still there.

#### Cycle behaviour

`ManualPageRunner` polls on a `DispatcherTimer` at `pollIntervalMs`. A cycle that is still running when the next tick arrives is skipped and reported rather than queued. A symbol that fails three times in a row is paused so one bad address cannot dominate every cycle (`ManualPageRunner.RetryPausedSymbols` clears the pause list; it is not yet wired to a button — restarting monitoring is the way to retry). Each cycle publishes a snapshot with cycle time, round-trip count, life state and manual-mode state, all shown live in the window header. `PlcService.PlanRead` reports how many PLC requests a page would cost *before* connecting — if the step count approaches the symbol count, the DB layout is too scattered to batch.

### Project Structure

```
S7Explorer/
├── App.xaml / App.xaml.cs          # Application entry point, themes
├── MainWindow.xaml / .cs           # Main UI window
├── SymbolManagerWindow.xaml / .cs  # Symbol management dialog
├── DbNumberInputDialog.xaml / .cs  # DB number input dialog
├── MessageDialog.xaml / .cs        # Themed message box
├── ManualPageWindow.xaml / .cs     # Manual control panel window
├── PlcService.cs                   # PLC connection, read/write, batched block reading
├── DbParser.cs                     # SCL/DB file parser → JSON
├── SymbolMapper.cs                 # Symbolic ↔ physical address mapping
├── ConnectionSettings.cs           # Settings persistence
├── LocalizationManager.cs          # Multi-language support
├── ManualPages/
│   ├── ManualPageDefinition.cs     # JSON page schema (data model)
│   ├── ManualPageLoader.cs         # Discovers & parses pages/*.json
│   ├── ManualPageValidator.cs      # Enforces write permission, types, ranges
│   ├── ManualPageRunner.cs         # Cyclic read, life bit, command writing
│   ├── ManualControlFactory.cs     # Builds the WPF control per JSON type
│   ├── PanelProfile.cs             # 7" / 10" panel dimensions
│   └── HmiStyle.cs                 # Fixed panel palette (theme-independent)
├── pages/
│   └── qr-hatti.json               # Example page definition (machine-specific)
├── lang/
│   ├── en-US.json                  # English strings
│   ├── tr-TR.json                  # Turkish strings
│   └── fr-FR.json                  # French strings
├── Resources/
│   ├── app_icon.ico
│   └── app_icon.png
└── TestSymbols/
    ├── DB_TypeTest.scl             # Sample SCL file for testing
    └── DB_TypeTest.symbols.json    # Parsed symbol output sample
```

### Requirements

- Windows 10/11
- .NET 8 Runtime (or SDK for development)
- Network access to the Siemens PLC
- Siemens S7-200 Smart / S7-300 / S7-400 / S7-1200 / S7-1500 PLC

### Getting Started

1. Clone the repository
2. Open `S7Explorer.slnx` in Visual Studio 2022+ or Visual Studio 2026
3. Build and run the project (`F5`)
4. Enter the PLC's IP address, select the CPU type, rack and slot
5. Click **Connect**
6. Enter a DB number and address, then click **Read** or **Write**
7. For an operator panel, load the machine's symbols, drop a page definition into `pages/` and click **Manual Test**

### Connection Settings

Connection settings and other settings are automatically saved to `settings.json` in the application output directory and restored on next launch.

---

## 🇹🇷 Türkçe

### Bu Uygulama Ne İşe Yarar?

**S7Explorer**, mühendislerin ve otomasyon profesyonellerinin Siemens S7 serisi PLC'lere bağlanmasını, veri bloğu değişkenlerini okumasını/yazmasını, sembolik adresleri yönetmesini ve PLC durumunu anlık olarak izlemesini sağlayan modern ve kullanıcı dostu bir WPF masaüstü uygulamasıdır.

### Temel Özellikler

| Özellik | Açıklama |
|---|---|
| **PLC Bağlantısı** | TCP/IP üzerinden S7 serisi herhangi bir PLC'ye (S7-200 Smart, S7-300/400, S7-1200/1500) IP, rack, slot ve port bilgisiyle bağlanma |
| **DB Okuma / Yazma** | Siemens Veri Bloklarındaki (DB) değişkenleri hem fiziksel adresle (`DB1.DBX0.0`) hem de sembolik isimle okuyup yazma |
| **DB Parser** | Siemens SCL (`.db` / `.scl`) dosyalarını ayrıştırarak değişken yapısını, veri tiplerini, byte offsetlerini ve varsayılan değerleri otomatik olarak JSON formatına çıkarma |
| **Sembol Yöneticisi** | Sembolik adres eşlemelerini (isim → fiziksel adres + veri tipi + açıklama) ekleme, düzenleme, silme ve `symbols.json` olarak kalıcı hale getirme |
| **Sembolik Adresleme** | Ham adres yazmak yerine açılır listeden değişkeni sembolik adıyla seçme; eşleyici fiziksel PLC adresine otomatik çevirir |
| **Manuel Kontrol Sayfaları** | Makineyi elle test etmek için operatör panelleri; `pages/` klasöründe doğrulanan JSON dosyalarıyla tanımlanır — sinyal lambaları, basılı tut butonları, tetik darbeleri, el sıkışmalar ve set değerleri; yazma izni uygulama tarafından dayatılır (aşağıya bakın) |
| **Toplu Blok Okuma** | Döngüsel sayfalar, sembol başına bir istek (gerçek hatta ~95 ms) yerine bitişik byte aralıklarını tek PLC isteğinde okur; 30 sembollük bir sayfa tek gidiş-dönüşte tazelenir |
| **Bağlantı Ayarları** | CPU tipi, IP, rack, slot, port, tema ve dil bilgilerini `settings.json` dosyasına kaydetme ve geri yükleme |
| **Çok Dilli Arayüz** | JSON dil dosyaları üzerinden tam yerelleştirme desteği (`lang/en-US.json`, `lang/tr-TR.json`, `lang/fr-FR.json`). Dil çalışma anında değiştirilebilir |
| **Açık / Koyu Tema** | Menüden Açık ve Koyu tema arasında geçiş yapma |
| **Sol Panel Aç/Kapat** | Sembol ağacı navigasyonu için animasyonlu genişleyip daralan sol panel |
| **Durum Göstergesi** | Animasyonlu renk göstergesiyle anlık bağlantı durumu (yeşil = bağlı, kırmızı = bağlı değil) |
| **Hata Yönetimi** | Bağlantı hataları, geçersiz adresler, tip uyumsuzlukları ve zaman aşımı için açıklayıcı hata mesajları |

### Mimari & Teknolojiler

- **Framework:** .NET 8 / WPF (Windows Presentation Foundation)
- **PLC Haberleşmesi:** [S7.Net Plus](https://github.com/S7NetPlus/s7netplus) — açık kaynaklı S7 protokol kütüphanesi
- **Serileştirme:** `System.Text.Json`
- **Desen:** `PlcService`, `SymbolMapper`, `DbParser`, `ConnectionSettings`, `LocalizationManager` servis sınıflarıyla Code-behind mimarisi
- **UI Deseni:** Event-driven, `ObservableCollection` ile veri bağlama

### Manuel Kontrol Sayfaları

**Manuel Test** butonu, devreye alma sırasında makineyi elle hareket ettirmek için kullanılan bir operatör panelini açar — pistonu ileri geri sürmek, kamerayı tetiklemek, sürücüyü çalıştırmak, hız set değerini değiştirmek.

Panel uygulamanın içine derlenmez. Çalıştırılabilir dosyanın yanındaki `pages/` klasöründe duran bir **JSON tanım dosyasıdır** ve çalışmasına izin verilmeden önce `symbols.json`'a karşı doğrulanır. Tanımlar saf veridir — sayfa dosyasındaki hiçbir şey kod olarak çalışmaz.

#### Sayfa dosyasının yapısı

```jsonc
{
  "schemaVersion": 1,
  "pageName": "Manuel Kontrol",

  "machine": {
    "db": 1,
    "writablePrefix": "DB_PC.Receive.",       // sayfanın yazabileceği TEK alan
    "lifeOut":     "DB_PC.Receive.Control.Life",
    "manualCmd":   "DB_PC.Receive.Control.Manual",
    "manualState": "DB_PC.Send.State.Manual", // makinenin teyidi
    "pollIntervalMs": 500,
    "lifeIntervalMs": 1000,
    "lifeTimeoutMs":  2000
  },

  "controls": [
    { "type": "lamp",      "group": "Piston", "label": "İLERİDE",
      "symbol": "DB_PC.Send.IO.Input.RejectFw" },
    { "type": "holdToRun", "group": "Piston", "label": "İLERİ",
      "symbol": "DB_PC.Receive.Control.ManRejFw",
      "feedback": "DB_PC.Send.IO.Input.RejectFw" },
    { "type": "setpoint",  "group": "Ana Konveyör", "label": "Hız Set",
      "symbol": "DB_PC.Receive.Velocity.Main",
      "feedback": "DB_PC.Send.Velocity.Main",
      "unit": "Hz", "scale": 0.1, "decimals": 1, "min": 0, "max": 50, "step": 0.5 }
  ],

  "alarmGroup": {
    "prefix":  "DB_PC.Send.Alarm.",           // önek altındaki her sembol alarm lambası olur
    "summary": "DB_PC.Send.State.Error"
  }
}
```

#### Kontrol tipleri

| `type` | Yön | Davranış |
|---|---|---|
| `lamp` | okur | BOOL için sinyal lambası. `color`: `green` (varsayılan), `red`, `yellow`, `blue` |
| `badge` | okur | Cihazın yanındaki durum rozeti — AKTİF / PASİF |
| `numeric` | okur | Büyük puntolu canlı ölçüm; `scale`, `decimals`, `unit` ile biçimlenir |
| `toggle` | yazar | Aç/kapa komut biti; operatör bıraktığı yerde kalır |
| `holdToRun` | yazar | Yalnızca basılı tutulduğu sürece set; bırakma, odak kaybı veya sayfa kapanışında düşer |
| `momentary` | yazar | Tek kısa darbe — tetik tarzı komutlar |
| `handshake` | yazar | Uygulama set eder, PLC temizler; ilerleme `busy` / `done` ile, hata `timeoutMs` sonunda bildirilir |
| `setpoint` | yazar | `min` / `max` / `step` ile sayısal değer; PLC'ye yazılan ham değer, girilenin `scale`'e bölünmüşüdür |

Ortak alanlar: `label`, `group` (aynı gruptaki kontroller tek başlık altında çizilir), `feedback` (geri okuma sembolü — makine teyit etmezse sayfa kontrolü işaretler), `icon` (Segoe MDL2 simgesi), `confirm` (göndermeden önce onay iste), `requiresManualMode` (varsayılan `true`; aşağıdaki manuel mod kapısına bakın).

#### Güvenlik modeli

Çalışan bir makineye yazma, birbirinden bağımsız dört noktada kapatılır:

1. **Doğrulama.** `ManualPageValidator`, bir komut kontrolü `machine.writablePrefix` dışına yazıyorsa, `symbols.json`'da olmayan bir sembolü hedefliyorsa, veri tipi kontrole uymuyorsa, PLC tipinin taşıyamayacağı bir setpoint aralığı bildiriyorsa ya da iki kontrol aynı komut bitini sürüyorsa tüm sayfayı reddeder. Hatalar yüklemeyi engeller; uyarılar (canlılık biti yok, manuel mod teyidi yok, gösterge komut alanını okuyor) gösterilir ama yüklemeye izin verilir.
2. **Yazmayı etkinleştirme.** Bir sayfayı izlemek hiçbir şey yazmaz. Operatörün **Yazmayı etkinleştir** kutusunu işaretleyip onaylaması gerekir; şerit turuncuya döner ve sayfa artık PLC'ye komut gönderebileceğini duyurur.
3. **Manuel mod teyidi.** Yazma açıkken bile komutlar, makine `machine.manualState` bitini bildirene kadar kilitli kalır. Kilitli her buton *neden* kilitli olduğunu üzerinde gösterir. İki tür komut bu kapıdan muaftır: manuel modu talep eden kontrolün kendisi — aksi halde kapı, kendisini açabilecek tek anahtarı da kilitlerdi — ve `"requiresManualMode": false` bildiren her kontrol. Bu muafiyet, işletme sırasında manuel moddan *önce* gelen komutlar içindir: acil stoptan çıkışta ve ilk açılışta önce reset yapılır ve bu, makine henüz manuele geçmeyi reddederken çalışmak zorundadır. Muaf kontroller için de yazmanın açık olması şarttır; yani asla tek tık uzakta değildirler.
4. **Çalışma anı izin listesi.** Her yazma, doğrulayıcının onayladığı sembol kümesine karşı yeniden denetlenir; böylece arayüzdeki bir hata, sayfanın hiç bildirmediği bir komutu gönderemez.

Yazmayı kapatmak, izlemeyi durdurmak veya pencereyi kapatmak önce komut bitlerini temizler (sayısal set değerlerine dokunulmaz — onlar komut değil, parametredir).

> ⚠️ **Watchdog.** `machine.lifeOut` bir el sıkışmadır: uygulama 1 yazar, PLC programı 0'a çeker. 0 okumak PLC'nin tarama yaptığının kanıtıdır. PLC programı bu el sıkışmayı uygulamıyorsa, ağ koptuğunda ya da uygulama çöktüğünde set kalmış bir komut bitini PLC tarafında düşürecek hiçbir şey yoktur. Hareket edebilen bir makinede yazmayı etkinleştirmeden önce bunu PLC programından teyit edin.

#### Pano boyutu, dil ve tema

Sayfa başlığı, ana penceredeki dil ve tema seçicilerinin aynısını taşır; yanına bir de **pano seçici** eklenir:

| Profil | Çözünürlük | Sınıf |
|---|---|---|
| 7" | 800 × 480 | KTP700 / TP700 / MTP700 |
| 10" | 1280 × 800 | MTP1000 Unified |

Seçim görsel bir tercih değildir. Kartlar, butonlar, lambalar, set değeri kutuları ve yazı boyutları seçilen profilden (`PanelProfile`) türetilir, kontroller yeniden kurulur ve pano alanı tam hedef çözünürlükte bir çerçeve içinde çizilir. Sayfa panodan uzunsa kaç piksel taştığı uyarı olarak yazılır — makinenin başındaki operatör, membran ekranı bir pencereyi fareyle kaydırdığı gibi kaydıramaz; alt kenarın altında kalan her şey ona erişilemezdir. Seçim `settings.json` içinde (`panelSize`) hatırlanır.

Tema, panonun çevresindeki pencereyi giydirir. Panonun kendisi her temada sabit `HmiStyle` paletini korur; çünkü makine ekranında kırmızı, uygulamanın geri kalanı nasıl görünürse görünsün arıza demektir.

#### Alarm şeridi

`alarmGroup.prefix` altındaki her sembol bir alarm olur; etiketi `symbols.json` içindeki açıklamasından gelir. Yalnızca *geçişler* kaydedilir, böylece duran bir alarm turda bir değil, tek bir zaman damgalı satır üretir. Şart ortadan kalktığında satır silinmez, işaretlenir: simge grileşir, yazının üstü çizilir ve düştüğü saat eklenir. Yakın geçmiş, operatöre ekrana değil makineye bakarken ne olduğunu söyleyen şeydir.

Alarm başlığındaki **ONAYLA** butonu yalnızca düşmüş satırları listeden kaldırır. Duran alarmları asla kaldırmaz ve SİSTEM DURUMU göstergesi listeyi değil canlı bitleri okur; yani onaylamak şeridi derleyebilir ama hâlâ var olan bir arızayı gizleyemez.

#### Tur davranışı

`ManualPageRunner`, `pollIntervalMs` periyoduyla bir `DispatcherTimer` üzerinde okur. Sonraki tick geldiğinde hâlâ süren bir tur kuyruğa alınmaz, atlanır ve bildirilir. Üst üste üç kez hata veren sembol duraklatılır; böylece tek bir hatalı adres her turu meşgul edemez (`ManualPageRunner.RetryPausedSymbols` duraklatma listesini temizler ama henüz bir butona bağlı değildir — yeniden denemenin yolu izlemeyi durdurup başlatmaktır). Her tur; tur süresi, gidiş-dönüş sayısı, canlılık durumu ve manuel mod durumunu taşıyan bir anlık görüntü yayınlar ve bunlar pencere başlığında canlı gösterilir. `PlcService.PlanRead`, bir sayfanın kaç PLC isteğine mal olacağını **bağlanmadan** hesaplar — adım sayısı sembol sayısına yaklaşıyorsa DB düzeni toplu okumaya fazla dağınıktır.

### Proje Yapısı

```
S7Explorer/
├── App.xaml / App.xaml.cs          # Uygulama giriş noktası, temalar
├── MainWindow.xaml / .cs           # Ana UI penceresi
├── SymbolManagerWindow.xaml / .cs  # Sembol yönetimi penceresi
├── DbNumberInputDialog.xaml / .cs  # DB numarası giriş diyaloğu
├── MessageDialog.xaml / .cs        # Temaya uyumlu mesaj kutusu
├── ManualPageWindow.xaml / .cs     # Manuel kumanda paneli penceresi
├── PlcService.cs                   # PLC bağlantı, okuma/yazma, toplu blok okuma
├── DbParser.cs                     # SCL/DB dosya ayrıştırıcı → JSON
├── SymbolMapper.cs                 # Sembolik ↔ fiziksel adres eşleme
├── ConnectionSettings.cs           # Ayarların kalıcı hale getirilmesi
├── LocalizationManager.cs          # Çok dilli destek
├── ManualPages/
│   ├── ManualPageDefinition.cs     # JSON sayfa şeması (veri modeli)
│   ├── ManualPageLoader.cs         # pages/*.json bulur ve ayrıştırır
│   ├── ManualPageValidator.cs      # Yazma izni, tip ve aralık denetimi
│   ├── ManualPageRunner.cs         # Döngüsel okuma, canlılık biti, komut yazma
│   ├── ManualControlFactory.cs     # JSON tipine göre WPF kontrolünü üretir
│   ├── PanelProfile.cs             # 7" / 10" pano ölçüleri
│   └── HmiStyle.cs                 # Sabit pano paleti (temadan bağımsız)
├── pages/
│   └── qr-hatti.json               # Örnek sayfa tanımı (makineye özel)
├── lang/
│   ├── en-US.json                  # İngilizce metinler
│   ├── tr-TR.json                  # Türkçe metinler
│   └── fr-FR.json                  # Fransızca metinler
├── Resources/
│   ├── app_icon.ico
│   └── app_icon.png
└── TestSymbols/
    ├── DB_TypeTest.scl             # Test için örnek SCL dosyası
    └── DB_TypeTest.symbols.json    # Ayrıştırılmış sembol çıktısı örneği
```

### Gereksinimler

- Windows 10/11
- .NET 8 Runtime (geliştirme için SDK)
- Siemens PLC'ye ağ erişimi
- Siemens S7-200 Smart / S7-300 / S7-400 / S7-1200 / S7-1500 PLC

### Başlarken

1. Depoyu klonlayın
2. `S7Explorer.slnx` dosyasını Visual Studio 2022+ veya Visual Studio 2026'da açın
3. Projeyi derleyip çalıştırın (`F5`)
4. PLC'nin IP adresini girin, CPU tipini, rack ve slot değerlerini seçin
5. **Bağlan** butonuna tıklayın
6. DB numarasını ve adresi girin, ardından **Oku** veya **Yaz** butonuna tıklayın
7. Operatör paneli için makinenin sembollerini yükleyin, sayfa tanımını `pages/` klasörüne koyun ve **Manuel Test** butonuna tıklayın

### Bağlantı Ayarları

Bağlantı ayarları ve diğer ayarlar uygulama çıktı dizinindeki `settings.json` dosyasına otomatik olarak kaydedilir ve bir sonraki açılışta geri yüklenir.

---

## License

MIT License — feel free to use, modify and distribute.
