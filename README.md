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
| **Connection Settings** | Save and restore connection settings (CPU type, IP, rack, slot, port, theme, language) to/from `connection_settings.json` |
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

### Project Structure

```
S7Explorer/
├── App.xaml / App.xaml.cs          # Application entry point
├── MainWindow.xaml / .cs           # Main UI window
├── SymbolManagerWindow.xaml / .cs  # Symbol management dialog
├── DbNumberInputDialog.xaml / .cs  # DB number input dialog
├── PlcService.cs                   # PLC connection & read/write logic
├── DbParser.cs                     # SCL/DB file parser → JSON
├── SymbolMapper.cs                 # Symbolic ↔ physical address mapping
├── ConnectionSettings.cs           # Settings persistence
├── LocalizationManager.cs          # Multi-language support
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
| **Bağlantı Ayarları** | CPU tipi, IP, rack, slot, port, tema ve dil bilgilerini `connection_settings.json` dosyasına kaydetme ve geri yükleme |
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

### Proje Yapısı

```
S7Explorer/
├── App.xaml / App.xaml.cs          # Uygulama giriş noktası
├── MainWindow.xaml / .cs           # Ana UI penceresi
├── SymbolManagerWindow.xaml / .cs  # Sembol yönetimi penceresi
├── DbNumberInputDialog.xaml / .cs  # DB numarası giriş diyaloğu
├── PlcService.cs                   # PLC bağlantı & okuma/yazma servisi
├── DbParser.cs                     # SCL/DB dosya ayrıştırıcı → JSON
├── SymbolMapper.cs                 # Sembolik ↔ fiziksel adres eşleme
├── ConnectionSettings.cs           # Ayarların kalıcı hale getirilmesi
├── LocalizationManager.cs          # Çok dilli destek
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

### Bağlantı Ayarları

Bağlantı ayarları ve diğer ayarlar uygulama çıktı dizinindeki `settings.json` dosyasına otomatik olarak kaydedilir ve bir sonraki açılışta geri yüklenir.

---

## License

MIT License — feel free to use, modify and distribute.
