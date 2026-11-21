# 3. Depo haritası

[Dizin](../README.md#türkçe) · Önceki: [2. Başlarken](02-baslarken.md) · Sonraki: [4. Modüller ve olaylar](04-moduller.md)

Şeylerin nerede olduğu. Bir şeyi değiştirmeniz gerekince önce satırını bulun.

## Üst düzey

| Yol | Nedir |
|---|---|
| `Vyxum.slnx` | Çözüm dosyası (yeni XML çözüm biçimi). Kökte `dotnet build` bunu derler. |
| `global.json` | .NET SDK'yı sabitler (`10.0.100`, daha yeni özellik bantları serbest) ve test çalıştırıcısını seçer. |
| `Directory.Build.props` | Her proje için ayarlar: `net10.0`, nullable, örtük using, uyarılar hata ve ürün sürümünün tek kaynağı olan **`<Version>`**. |
| `Directory.Packages.props` | Tüm NuGet paket sürümleri (merkezi paket yönetimi). |
| `.config/dotnet-tools.json` | Depoya yerel `dotnet-ef` aracı (bir kez `dotnet tool restore`). |
| `.editorconfig` | Kod stili. |
| `.github/workflows/ci.yml` | CI: .NET derleme+test (SQL Server ile), web kontrolleri, kurulum dosyası derlemesi. |
| `CLAUDE.md` | Projenin pazarlıksız kuralları. Okuyun. |
| `README.md`, `README.tr.md` | Genel bakış, yapılandırma anahtarları ve modüllerin HTTP API'si. |
| `CONTRIBUTING.md` | Katkı kuralları, geçiş komutları. |
| `docs/` | Son kullanıcı için `INSTALL.md` / `KURULUM.md` ve bu geliştirici rehberi. |
| `installer/` | Inno Setup betiği ve derleme betiği ([8. bölüm](08-surum.md)). |

## `src/Platform`

| Proje | Rol |
|---|---|
| `Vyxum.Platform.Abstractions` | Modüllerin kullanabildiği şeyler. Arayüzler (`IModule`, `ICapabilityRegistry`, `IIntegrationEventHandler<T>`, `ILiveWeightSource`, `IDeviceCommands`), `ModuleDbContext`, outbox/inbox varlıkları, `UiStringSource`. Küçük tutun: buraya eklenen her şey tüm modüller için sözleşme olur. |
| `Vyxum.Platform` | Çalışma zamanı. Klasörler: `Modules/` (keşif, doğrulama, katalog), `Events/` (outbox dağıtıcı), `Data/` (veritabanı sağlayıcısı, başlatıcı, yedekler, platform tabloları), `Devices/` (agent hub, canlı ağırlık deposu, cihaz ayarları, keşif işleri), `Setup/` (kurulum sihirbazı API'si, yeniden başlatma), `Configuration/` (`vyxum.json` okuyucu/yazıcı), `Ui/` (`/api/ui/strings`, `/api/ui/modules`). |
| `Vyxum.Platform.Testing` | Test yardımcıları: `TestDatabase`, `TestModuleHost`, `TestClock`. |

## `src/Host/Vyxum.Host`

`Program.cs` kısadır ve baştan sona okunmaya değer: `vyxum.json`'u yükler, platformu `HostModules` modülleriyle kaydeder, agent hub'ı, istek yerelleştirmesini (varsayılan Türkçe, İngilizce), statik dosyaları, `/health` uç noktasını ekler ve API gruplarını eşler. `HostModules.cs` yerleşik modülleri listeler. `Resources/Strings*.resx` kabuk metinlerini tutar. `.csproj`, web arayüzünü `wwwroot/` içine derleyen MSBuild hedeflerini içerir.

## `src/Modules`

`Products/Vyxum.Modules.Products` ve `Weighing/Vyxum.Modules.Weighing`. Her modülün biçimi aynıdır:

```
<Ad>Module.cs          IModule: manifest + servis kaydı + uç nokta eşleme
<Ad>DbContext.cs       modülün tek DbContext'i (ModuleDbContext'ten türer)
<Ad>DesignTimeFactory  tek satır, `dotnet ef`in bağlamı oluşturmasını sağlar
<Ad>Endpoints.cs       HTTP uç noktaları
<Ad>Service.cs         iş mantığı
<Ad>Strings.cs         metinler için ResourceManager
<Olay>.cs              modülün yayınladığı ya da kopyaladığı entegrasyon olayları
Resources/Strings.resx, Strings.tr.resx
Migrations/Sqlite, Migrations/SqlServer
```

## `src/Devices` (`Vyxum.Devices` dışında hiçbir Vyxum derlemesine başvurmaz)

| Proje | Rol |
|---|---|
| `Vyxum.Devices` | `ITransport`, `IScaleProtocol`, `ISupportsZero` sözleşmeleri; `FrameReader` (bayt akışını çerçevelere böler); `DeviceRunner` (bağlan, oku, artan beklemeyle yeniden bağlan); `WeightReading`, `DeviceStatus`. |
| `Vyxum.Devices.Transports` | `SerialTransport`, `TcpTransport`. |
| `Vyxum.Devices.MtSics` | METTLER TOLEDO MT-SICS protokolü. |
| `Vyxum.Devices.ToledoContinuous` | Toledo sürekli çıkış (yalnızca dinler). |
| `Vyxum.Devices.GenericAscii` | Diğer ASCII çıkışları için düzenli ifade tabanlı protokol. |
| `Vyxum.Devices.Simulator` | Bellek ya da TCP üzerinden sahte teraziler ve kayıtlı satırların tekrarı (`Recordings/`). |
| `Vyxum.Devices.Discovery` | Terazileri salt okunur arama (`PortScanner`, `WriteGuardTransport`). |

## `src/Agent/Vyxum.Agent`

`Program.cs` / `AgentHost.cs` başlatır; `HubWorker.cs` sunucuya SignalR bağlantısını sürdürür; `DeviceFleet.cs` ayarlar değişince cihaz çalıştırıcılarını başlatır, durdurur, yeniden başlatır; `Configuration/` ayarları çözer ve doğrular (protokol ve taşıma **tür adları** da burada kaydedilir: `AgentConfiguration`); `Contract/AgentHubContract.cs` kablo sözleşmesinin agent tarafındaki kopyasıdır; `Discovery/` terazi aramasını yürütür.

## `src/Desktop/Vyxum.Desktop`

WinForms + WebView2 penceresi, `net10.0-windows`. Her işletim sisteminde test edilebilen mantık `Logic/` içindedir.

## `src/Web/vyxum-web`

React + TypeScript + Vite ([7. bölüm](07-web-arayuzu.md)). Çıktı `src/Host/Vyxum.Host/wwwroot` klasörüne gider (commit edilmez).

## `tests`

| Proje | Testler |
|---|---|
| `Vyxum.Platform.Tests` | Platform birim testleri (modüller, olaylar, veri, kurulum, arayüz metinleri). |
| `Vyxum.Modules.Tests` | Products ve Weighing, HTTP API üzerinden; ayrıca `MigrationTests`. |
| `Vyxum.IntegrationTests` | Tüm sunucu uçtan uca: olay akışı, agent hub, cihaz ayarları, keşif, kurulum API'si, statik ön yüz. İki veritabanında çalışır. |
| `Vyxum.Devices.Tests` | Protokol ayrıştırıcıları (ham satır kayıtlarıyla), taşımalar, simülatör, keşif. |
| `Vyxum.Desktop.Tests` | Masaüstü penceresinin adres ve sağlık denetimi mantığı. |
| `Vyxum.ArchitectureTests` | Bağımlılık kuralları (NetArchTest) ve kaynak dosyası kuralları (her `Strings.resx`'in aynı anahtarlı, boş değersiz bir `Strings.tr.resx`'i olmalı). |
| `SampleModules/` | Modüllerin olaylarla konuşabildiğini kanıtlamak için **yalnızca testlerde** kullanılan iki küçük modül (Orders, Notifications). Asla dağıtılmaz. |
