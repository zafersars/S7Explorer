# CLAUDE.md

Bu dosya, depoda çalışan geliştiriciler ve Claude Code için proje rehberidir.
Kullanıcıya dönük tanıtım ve JSON sayfa şeması `README.md` içindedir; burada tekrar edilmez.

## Proje

.NET 8 / WPF masaüstü uygulaması. Siemens S7 PLC'lere S7.Net Plus ile TCP/IP üzerinden bağlanır;
DB okur/yazar, SCL dosyalarını ayrıştırır ve JSON tanımlı **manuel kumanda panelleri** çalıştırır.

Çözüm: `S7Explorer.slnx` → tek proje `S7Explorer/S7Explorer.csproj`
(`net8.0-windows`, `UseWPF`, `Nullable=enable`, self-contained win-x64 tek dosya yayın).

```powershell
dotnet build S7Explorer/S7Explorer.csproj
dotnet run   --project S7Explorer/S7Explorer.csproj
dotnet publish S7Explorer/S7Explorer.csproj -c Release   # tek dosya EXE
```

Test projesi yoktur. Doğrulama, gerçek PLC ile elle yapılır — bu yüzden değişiklikleri
"çalışıyor" diye bildirmeden önce nasıl doğrulandığını açıkça söyleyin.

## Kod düzeni

Kalıp: MVVM değil, **code-behind + servis sınıfları**. Yeni bir MVVM katmanı getirmeyin.

| Katman | Dosya | Sorumluluk |
|---|---|---|
| PLC erişimi | `PlcService.cs` | Bağlantı, tekil okuma/yazma, `ReadManyAsync` (toplu blok okuma), `PlanRead` |
| Adresleme | `SymbolMapper.cs` | Sembolik ad ↔ fiziksel adres + veri tipi; `symbols.json` |
| Ayrıştırma | `DbParser.cs` | Siemens SCL/DB → JSON sembol tablosu |
| Ayarlar | `ConnectionSettings.cs` | `settings.json` (EXE'nin yanında) |
| Dil | `LocalizationManager.cs` | `lang/*.json`, çalışma anında değişir |
| Manuel sayfalar | `ManualPages/` | Tanım, yükleyici, doğrulayıcı, koşucu, kontrol fabrikası, palet |

`ManualPages/` içindeki akış tek yönlüdür ve bu sıra korunmalıdır:

```
pages/*.json → ManualPageLoader → ManualPageValidator → ManualPageRunner → ManualControlFactory
                                        ↑ izin listesi burada üretilir ↑
```

## Değişmezler (bunları bozmayın)

1. **Yazma izni yalnızca doğrulayıcıdan gelir.** `ManualPageRunner`, `ManualPageValidator`'ın
   ürettiği `WrittenSymbols` kümesi dışına yazmaz ve her yazmada bunu yeniden denetler.
   Bu kümeyi çağıran taraftan doldurmayın, kontrolü gevşetmeyin.
2. **İzlemek yazmak değildir.** Sayfa açmak ve okumak PLC'ye hiçbir şey yazmaz.
   `IsWriteEnabled` false başlar; yalnızca operatörün açık onayıyla açılır.
3. **Manuel mod kapısının muafiyeti bildirimseldir.** Kapıyı atlayan tek şey
   `ControlDefinition.RequiresManualMode` (varsayılan `true`) ve manuel modu talep eden kontrolün
   kendisidir; ikisi de `ManualControl.BypassesManualModeGate` üzerinden okunur. Muafiyeti kontrol
   tipine (ör. "handshake'ler serbest") göre kodlamayın — hangi komutun manuel moddan önce geldiği
   makineye özgüdür, sayfa bildirir. Muafiyet yazma iznini kapsamaz: arm'lanmadan hiçbir şey gitmez.
4. **Kapanış temizler.** Yazmayı kapatma, durdurma ve pencere kapanışı komut bitlerini
   sıfırlar (`ShutdownAsync` / `ResetAllCommandsAsync`). Sayısal setpoint'lere dokunulmaz.
5. **Sıra mantığı ve kilitlemeler PLC'de kalır.** Uygulama komut biti yazar, çıkışa yazmaz,
   emniyet mantığını taklit etmez.
6. **Ölçüler `HmiStyle.Metrics`'ten (`PanelProfile`) gelir.** Manuel panelde yeni bir kontrol
   çizerken piksel sabiti yazmayın — kart, buton, lamba ve yazı boyutları seçilen panodan
   (7" 800×480 / 10" 1280×800) türetilir. Profil değişince sayfa yeniden kurulur; kurulum
   alarm geçmişini korumak zorundadır.
7. **`HmiStyle` paleti temadan bağımsızdır.** Panoda kırmızı arızadır; kullanıcı temasıyla kaymaz.
   Manuel panel renklerini `App.xaml` tema kaynaklarına bağlamayın.
8. **Tur birikmez.** `OnTick` süren bir turun üstüne binmez; atlar ve `Notice` ile bildirir.
   Blok okuma yerine sembol sembol okumaya dönmeyin — sahada ölçülen ~95 ms/istek,
   500 ms periyodu imkânsız kılar.
9. **Şema sürümü tektir.** `ManualPageDefinition.SupportedSchemaVersion` yalnızca bir göç
   yoluyla birlikte artırılır; eski sayfalar sessizce yanlış yorumlanmamalıdır.

## Yazım kuralları

- Kod içi XML doc'lar **çift dilli**: `EN:` satırı, ardından `TR:` satırı. Mevcut kalıba uyun.
- Satır içi `//` yorumlar Türkçedir ve *neden*i anlatır, ne yaptığını değil.
- Kullanıcıya görünen her metin `LocalizationManager` üzerinden gelir; üç dil dosyası da
  (`en-US`, `tr-TR`, `fr-FR`) aynı anahtar kümesini taşımalıdır. Yeni anahtar eklerken üçünü de güncelleyin.
- `ObservableCollection`'a bağlı bir satır sınıfı yerinde değiştirilecekse `INotifyPropertyChanged`
  uygulamalıdır (`AlarmRow` örnek). Aynı nesneyi aynı indekse yeniden atamak (`list[i] = item`)
  bağlamayı tazelemez — alarm satırları bu yüzden düştükten sonra kırmızı kalıyordu.
- Doğrulama mesajları mühendise hitap eder: neyin yanlış olduğunu ve neden önemli olduğunu söyler.

## Yayınlama

```powershell
dotnet publish S7Explorer/S7Explorer.csproj -c Release -o publish
Compress-Archive -Path publish\S7Explorer.exe,publish\lang,publish\pages,publish\db `
                 -DestinationPath publish\S7Explorer-vX.Y.Z-win-x64.zip -CompressionLevel Optimal
gh release create vX.Y.Z publish\S7Explorer-vX.Y.Z-win-x64.zip --title "S7Explorer vX.Y.Z" --notes "..."
```

**Release asset'i zip'tir, çıplak EXE değildir.** İki sebep, ikisi de sahada acıtır:

1. `IncludeNativeLibrariesForSelfExtract` olmadan WPF'in native DLL'leri (`wpfgfx_cor3`,
   `PresentationNative_cor3`, `vcruntime140_cor3`) tek dosyanın dışında kalır. Bu ayar
   `csproj`'da açıktır — kapatmayın. Kapalıyken uygulama başlar, ilk pencereyi oluştururken
   `DllNotFoundException` ile çöker; pencere hiç görünmediği için hata sessiz görünür.
   v1.0.0 bu yüzden çalışmıyordu.
2. `lang/`, `pages/` ve `db/` EXE'nin yanında olmak zorundadır. `lang/` yoksa uygulama
   açılır ama arayüz ham çeviri anahtarları gösterir (`Win_Main` gibi) — çökmediği için
   fark edilmesi zordur.

Doğrulama, publish klasöründe çalıştırmak değildir: zip'i **boş bir klasöre açıp** oradan
çalıştırın ve görünür pencere başlığının "Siemens PLC Control" olduğunu (ham anahtar değil)
görün. Publish klasöründe kalan dosyalar hatayı gizler.

`db/` altındaki `DB_PC.db` makineye özeldir; `pages/qr-hatti.json` ile aynı açık soruya tabidir
(depoda mı kalacak, saha başına mı dağıtılacak). Genel dağıtımdan çıkarılmasına karar verilirse
tek değişiklik noktası zip'i oluşturan `-Path` listesidir.

## Çalışma dosyaları

`settings.json`, `symbols.json` ve `pages/*.json` EXE'nin yanında durur
(`AppDomain.CurrentDomain.BaseDirectory`). Program Files altında bu konum yazılabilir değildir;
`%AppData%`'ya taşıma planlı ama yapılmamış bir iştir — taşınırsa `ManualPageLoader.PagesFolder`
ve `ConnectionSettings` tek değişiklik noktalarıdır.

`pages/qr-hatti.json` belirli bir makineye (QR etiketleme hattı, `DB_PC`) özeldir; genel bir
örnek değildir. Depoda mı kalacağı yoksa saha başına mı dağıtılacağı henüz kararlaştırılmadı.

## Açık riskler

- **Watchdog yok.** `Receive.Control.Life` el sıkışmasının PLC tarafı, çalışmayan ayrı bir PC
  yazılımına aittir. Bu haliyle ağ koparsa ya da uygulama çökerse set kalmış bir komut bitini
  PLC düşüremez. Hareket edebilen bir makineye bağlanmadan önce bu konu yeniden ele alınmalıdır.
- Yazma yolu kodda mevcut ve arm'lanabilir; gerçek makine üzerinde ilk kez denenmeden önce
  ayrıca teyit alınmalıdır.
