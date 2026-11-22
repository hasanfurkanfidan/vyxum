# Vyxum

[English](README.md) | **Türkçe**

Vyxum, seri port ya da ağ üzerinden bir bilgisayara veya sunucuya bağlanan terazilerle çalışan modüler bir tartım yazılımıdır. Kurumun kendi bilgisayarına ya da sunucusuna kurulur ve internet bağlantısı olmadan çalışır.

Bu depo Vyxum'un açık çekirdeğidir ve Apache Lisansı 2.0 ile lisanslanmıştır. Ek modüller ayrıca geliştirilmektedir.

> **Durum: erken geliştirme.** Depoda platform (modül yükleme ve doğrulama, outbox ve inbox ile entegrasyon olayları, veritabanı sağlayıcısı seçimi), Products ve Weighing modülleri, web arayüzü ve cihaz sürücüleri bulunur. F2 fazı sürmektedir.

## Kurulum / Installation

Windows 10 ya da 11 (64 bit). İnternet bağlantısı, veritabanı sunucusu ya da ek bir program gerekmez. `VyxumSetup-<sürüm>.exe` dosyasını çalıştırın ve adım adım kılavuzu izleyin: [Kurulum kılavuzu](docs/KURULUM.md) (English: [docs/INSTALL.md](docs/INSTALL.md)).

## Lisans dosyası / License file

Tartım, Ürünler ve Kayıtlar lisans istemez. Ücretli modüller `.vxlic` uzantılı bir lisans dosyasıyla açılır; dosyayı Ayarlar, Modüller ve lisans bölümünden yüklersiniz. Bkz. [lisans kılavuzu](docs/LISANS.md) (English: [docs/LICENSING.md](docs/LICENSING.md)).

## Depo yapısı

| Yol | İçerik |
|---|---|
| `src/Platform/Vyxum.Platform.Abstractions` | Modüllerin başvurabileceği tek Vyxum derlemesi: modül sözleşmeleri, entegrasyon olayları, modül `DbContext` temel sınıfı |
| `src/Platform/Vyxum.Platform` | Çalışma zamanı: modül bulma ve doğrulama, yetenek kaydı, outbox dağıtıcısı, inbox, veritabanı sağlayıcısı seçimi |
| `src/Platform/Vyxum.Platform.Testing` | Test yardımcıları: bellek içi modül sunucusu ve SQLite ile SQL Server için veritabanı hazırlayıcıları |
| `src/Host/Vyxum.Host` | ASP.NET Core uygulaması (`/health`, `/api/hello`) |
| `src/Modules/Products` | Products modülü: ürün kataloğu |
| `src/Modules/Weighing` | Weighing modülü: kayıtlar, dara, tutar, iptal, dışa aktarma |
| `src/Web/vyxum-web` | React web arayüzü; sunucunun `wwwroot` klasörüne derlenir |
| `tests/Vyxum.Platform.Tests` | Birim testleri |
| `tests/Vyxum.IntegrationTests` | İki veritabanı sağlayıcısında uçtan uca testler, sunucu başlatma dahil |
| `tests/Vyxum.ArchitectureTests` | NetArchTest.Rules ile kontrol edilen mimari kurallar |
| `tests/SampleModules` | Yalnızca testlerde kullanılan iki örnek modül. Dağıtılmazlar. |
| `.github/workflows/ci.yml` | CI: Ubuntu üzerinde SQL Server 2022 servis konteyneri ile derleme ve test |

## Gereksinimler

- [.NET 10 SDK](https://dotnet.microsoft.com/download). `global.json` SDK sürümünü `10.0.100` olarak sabitler ve daha yeni özellik bantlarına izin verir.
- Docker, isteğe bağlı: SQL Server testlerini yerelde çalıştırmak için.

## Derleme ve test

```bash
dotnet build -warnaserror
dotnet test
```

SQL Server testleri `VYXUM_TEST_SQLSERVER` ortam değişkenini gerektirir. Değer, veritabanı adı içermeyen bir bağlantı dizesidir. Her test benzersiz adlı kendi veritabanını oluşturur ve işi bitince siler. Değişken tanımlı değilse SQL Server senaryoları nedeniyle atlanır ve atlanma gerekçesi raporlanır; SQLite senaryoları her zaman çalışır.

SQL Server 2022'yi Docker ile yerelde çalıştırmak için:

```bash
docker run -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Vyxum_Test_123!' \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

export VYXUM_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=Vyxum_Test_123!;TrustServerCertificate=True'
dotnet test
```

Windows PowerShell'de `export` yerine `$env:VYXUM_TEST_SQLSERVER = '...'` kullanın. Yukarıdaki parola yalnızca yerel test içindir; başka bir ortamda kendi parolanızı kullanın.

## Sunucuyu çalıştırma

```bash
dotnet run --project src/Host/Vyxum.Host
```

Yapılandırma gerekmez. Sunucu SQLite kullanır ve ilk açılışta veri klasöründe `vyxum.db` dosyasını oluşturur. Veri klasörü Windows'ta `%ProgramData%\Vyxum`, diğer sistemlerde çalışma klasöründeki `data/` klasörüdür.

Adresi seçmek için `--` sonrasına `--urls http://127.0.0.1:5199` ekleyin.

Uç noktalar:

```bash
curl http://127.0.0.1:5199/health
# {"status":"ok","modules":[],"database":"Sqlite"}

curl -H "Accept-Language: en" http://127.0.0.1:5199/api/hello
# {"message":"Hello from Vyxum","culture":"en"}
```

Sunucu `tr` (varsayılan) ve `en` dillerini destekler.

## Cihazlar

Bir terazi iki parçada okunur. **Taşıma** (transport) baytları seri port ya da TCP üzerinden taşır. **Protokol** ise terazinin gönderdiği baytları yorumlar. Her taşıma her protokolle çalışır.

| Proje | İçerik |
|---|---|
| `src/Devices/Vyxum.Devices` | Sözleşmeler (`ITransport`, `IScaleProtocol`), çerçeve okuyucu ve cihaz çalıştırıcı |
| `src/Devices/Vyxum.Devices.Transports` | Seri port ve TCP taşımaları |
| `src/Devices/Vyxum.Devices.MtSics` | MT-SICS protokolü |
| `src/Devices/Vyxum.Devices.ToledoContinuous` | Toledo sürekli çıkış protokolü |
| `src/Devices/Vyxum.Devices.GenericAscii` | Diğer ASCII çıkışlar için kurala dayalı protokol |
| `src/Devices/Vyxum.Devices.Simulator` | Bellek ya da TCP üzerinden simüle edilmiş teraziler ve kayıtlı satırların yeniden oynatılması |
| `src/Devices/Vyxum.Devices.Discovery` | Seri portlarda ya da tek bir ağ adresinde teraziyi yalnızca okuyarak arama |

Desteklenen taşımalar seri port ve TCP'dir. Desteklenen protokoller MT-SICS, Toledo Continuous ve genel ASCII'dir.

Sürücüler ağırlığı okur. MT-SICS sürücüsü yalnızca `S`, `SI` ve `SIR` gönderir; bir kullanıcı teraziyi sıfırlamayı açıkça istediğinde bir kez de `Z` gönderir. Dara, açılış durumuna dönüş ve ayar komutları asla gönderilmez. Toledo Continuous yalnızca dinler. Genel ASCII, yalnızca sizin yapılandırdığınız istek komutunu gönderir; bu komutun yalnızca ağırlığı okuyan bir komut olması gerekir.

[Vyxum Agent](src/Agent/Vyxum.Agent/README.tr.md), terazinin bağlı olduğu bilgisayarda çalışır. Cihazları okur ve canlı ağırlığı web uygulamasına gönderir.

Sunucuyu ve bir agent'ı simülatörle aynı bilgisayarda çalıştırmak için iki terminal kullanın:

```bash
dotnet run --project src/Host/Vyxum.Host --urls http://localhost:5000
dotnet run --project src/Agent/Vyxum.Agent
```

Ardından http://localhost:5000/api/devices adresini açın. `dotnet run` ile Agent Development ortamında başlar; bu ortamın `appsettings.Development.json` dosyası simüle edilmiş bir MT-SICS terazisi (572 g) ekler, donanım gerekmez.

Cihazlar web uygulamasında (Ayarlar → Cihazlar) ayarlanır ve `plt_Devices` tablosunda saklanır; Agent bağlanınca onları alır ve her değişikliği yeniden başlamadan uygular. `GET`, `POST /api/devices/config` ve `GET`, `PUT`, `DELETE /api/devices/config/{id}` cihaz ayarlarını okur ve değiştirir (`transport` ve `protocol` Agent ayarlarıyla aynı alanlara sahiptir; `capacity` ve `division` ondalık metindir). Agent'ın kendi ayar dosyasındaki `Devices` listesi geliştirme içindir: boş değilse Agent web uygulamasındaki cihazları yok sayar. Bkz. [Agent README](src/Agent/Vyxum.Agent/README.tr.md). `/api/devices` içindeki her cihaz ayarlarından gelen `name`, `capacity`, `division` ve `unit` alanlarını da taşır; Agent'ın kullanamadığı ayarlara sahip bir cihazın durumu `Error` olur.

http://localhost:5000/api/devices/{id}/live bir cihazı Server-Sent Events olarak yayınlar: önce o anki durumu, ardından durumu ya da ağırlığı her değiştiğinde yeni bir olay. Agent'ın henüz bildirmediği bir cihaz kimliği hata değildir; yayın `Disconnected` durumunda başlar ve agent bağlanınca sürer.

`POST /api/devices/{id}/zero` teraziden sıfırlamasını ister, her istekte bir kez. Her zaman 200 ile `{ "result": "Done" | "NotSupported" | "Rejected" | "Unavailable", "detail": "..." }` döner; `detail` günlükler için İngilizce metindir ve olmayabilir. Her cihaz desteklediği komutları `supportedCommands` içinde listeler (MT-SICS için `["Zero"]`).

`POST /api/devices/discover` Agent'tan terazi aramasını ister (gövde isteğe bağlı: `{ "agentName"?: "...", "tcp"?: { "host": "...", "port": 4001 } }`). 202 ile `{ "id" }` döner; Agent zaten arıyorsa 409 ile `{ "code": "devices.DiscoveryRunning" }`, bağlı Agent yoksa 503 ile `{ "code": "devices.NoAgent" }` döner. `GET /api/devices/discover/{id}` işi döndürür: `status` (`Running`, `Completed`, ya da `code` `devices.AgentTimeout` veya `devices.DiscoveryFailed` ile `Failed`), durumlarıyla `ports` (`Pending`, `Scanning`, `Found`, `NotFound`, `InUse`, `Error`) ve `candidates`; adayların `transport` ve `protocol` nesneleri değiştirilmeden `/api/devices/config`'e gönderilebilir. İşler 10 dakika bellekte tutulur. Arama yalnızca MT-SICS `SI` ve `I4` komutlarını gönderir; bkz. [Cihaz arama](src/Agent/Vyxum.Agent/README.tr.md#cihaz-arama).

## Modüller

Products ve Weighing modülleri sunucuya dahildir. Sunucu bunları her zaman yükler ve ikisi de birbirine bağlı değildir: yalnızca entegrasyon olaylarıyla konuşurlar. Her modül kendi tablolarını aynı veritabanında tutar.

| Modül | Proje | Ne yapar |
|---|---|---|
| Products | `src/Modules/Products/Vyxum.Modules.Products` | Ürün kataloğu: kod, ad, birim fiyat, ambalaj darası, raf ömrü, barkod öneki, içindekiler, hedef ağırlık ile alt ve üst tolerans (kg) |
| Weighing | `src/Modules/Weighing/Vyxum.Modules.Weighing` | Tartım kayıtları: cihazdan canlı ağırlık, yazılımsal dara, net, tutar, parti, neden belirterek iptal, liste, toplamlar ve Excel dışa aktarma |

Products uç noktaları (`/api/products`):

- `GET /api/products?search=&includeInactive=` ürünleri listeler; `GET /api/products/{id}` tek bir ürünü döndürür.
- `POST /api/products` ürün ekler; `PUT /api/products/{id}` günceller.
- `POST /api/products/{id}/deactivate` ve `POST /api/products/{id}/activate` ürünün tartımda kullanılıp kullanılamayacağını belirler.

Weighing uç noktaları (`/api/weighing`):

- `GET /settings` ekranın kullandığı kuralları döndürür; `GET /products` ekran için ürün arar.
- `GET /records` kayıtları `from`, `to`, `productId`, `lot` ve `includeCancelled` ile süzerek, `skip` ve `take` ile sayfalayarak listeler. `GET /records/recent` son kayıtları, `GET /records/{id}` tek bir kaydı döndürür.
- `GET /records/export` süzülmüş kayıtları Excel (.xlsx) dosyası olarak indirir.
- `POST /records` bir tartım kaydeder. Her istek bir `requestId` taşır; aynı kimlikle yapılan yeniden deneme ikinci kez kayıt oluşturmaz.
- `POST /records/{id}/cancel` bir kaydı iptal eder. `RequireCancelReason` true iken neden zorunludur.

Olaylar (sürüm 1):

- `products.product-saved`: bir ürün kaydedildiğinde yayınlanır. Weighing ürünün kendi kopyasını tutar; bu yüzden Products'ı hiç çağırmaz.
- `weighing.recorded`: bir kayıt kaydedildiğinde yayınlanır.
- `weighing.cancelled`: bir kayıt iptal edildiğinde yayınlanır.

Tartım kuralları yapılandırmanın `Vyxum:Weighing` bölümünden okunur:

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| `Vyxum:Weighing:RequireStable` | `true` | Yalnızca kararlı ağırlık kaydedilebilir |
| `Vyxum:Weighing:MinimumNet` | `0` | Kaydedilebilecek en küçük net ağırlık (kg). Net her zaman 0'dan büyük olmalıdır; 0'dan büyük bir değer ayrıca alt sınır koyar. |
| `Vyxum:Weighing:MaxReadingAge` | `00:00:03` | Kaydedilebilecek en eski terazi okuması. Daha eski bir okuma agent'ın ya da ağının durduğunu gösterir; kayıt reddedilir. `00:00:00` kontrolü kapatır. |
| `Vyxum:Weighing:MaxSaveDelay` | `00:00:10` | Operatörün gördüğü ağırlık ile kaydın sunucuya ulaşması arasındaki en uzun süre. Daha geç gelen kayıt (ör. ağ hatasından sonra yeniden denenen) reddedilir; böylece bir sonraki yük kaydedilemez. `00:00:00` kontrolü kapatır. |
| `Vyxum:Weighing:RequireCancelReason` | `true` | İptal için neden gerekir |
| `Vyxum:Weighing:Currency` | `TRY` | Tutarların para birimi; her kayıtta saklanır |
| `Vyxum:Weighing:AmountDecimals` | `2` | Tutarların yuvarlanacağı ondalık basamak sayısı (0 ile 4 arası) |
| `Vyxum:Weighing:AmountRounding` | `AwayFromZero` | Yuvarlama yöntemi; .NET `MidpointRounding` adıyla |

## Web arayüzü

Ekranlar `src/Web/vyxum-web` klasöründeki React, TypeScript ve Vite uygulamasıdır. Vite bunu `src/Host/Vyxum.Host/wwwroot` klasörüne derler ve sunucu bu dosyaları sunar. Müşterinin başka bir şey kurması gerekmez: Node.js yalnızca geliştiricinin bilgisayarında gereklidir.

- **Derleme:** `wwwroot` yoksa `dotnet build` arayüzü de derler. Bunun için Node.js 22 gerekir. Atlamak için `-p:SkipUiBuild=true` verin; bu durumda sunucu arayüzü yalnızca `wwwroot` daha önce derlenmişse sunar, aksi halde kök adres 404 döner.
- **Geliştirme:** bir kez `npm ci`, ardından `src/Web/vyxum-web` klasöründe `npm run dev` çalıştırın. Vite arayüzü canlı yenileme ile sunar (genellikle http://localhost:5173) ve `/api` ile `/hubs` isteklerini http://localhost:5000 adresine iletir; bu yüzden sunucuyu önce 5000 portunda başlatın.
- **Metinler:** arayüzde düz metin yoktur. Bütün metinler `Strings.resx` ve `Strings.tr.resx` dosyalarındadır ve `/api/ui/strings` üzerinden arayüze gelir. Arayüz Türkçe ve İngilizce kullanılabilir.
- **Çevrimdışı:** yazı tipleri `@fontsource` ile uygulamayla birlikte gelir. Arayüz internetten hiçbir şey yüklemez.

## Gerçek bir terazi ile deneme

Bu deneme, seri porta bağlı bir METTLER TOLEDO IND231 ile yapılır:

1. Sunucuyu başlatın:

   ```bash
   dotnet run --project src/Host/Vyxum.Host --urls http://localhost:5000
   ```

2. İkinci bir terminalde agent'ı IND231 örnek ayarlarıyla başlatın:

   ```bash
   dotnet run --project src/Agent/Vyxum.Agent -- --settings appsettings.Ind231.example.json
   ```

   Örnek ayar `COM3` portunu, 9600 8N1 biçimini ve MT-SICS okuma komutu `SI` kullanır. Terazi başka bir portta ise `PortName` değerini değiştirin. `--settings` yolu, `dotnet run --project`'in başladığı agent proje klasörüne göredir.

3. Tarayıcıda http://localhost:5000 adresini açın.

Ekranın terazi ile çalışma biçimi:

- Dara Vyxum tarafından yazılımsal olarak tutulur. **Dara al** o anki brüt ağırlığı dara olarak kaydeder. Teraziye kendi dara komutu asla gönderilmez.
- **Sıfırla** düğmesi her basışta teraziye bir kez `Z` gönderir. Başka hiçbir komut gönderilmez.

Veritabanı şeması ilk açılışta oluşturulur. Varsayılan SQLite dosyası veri klasöründeki `vyxum.db` dosyasıdır: çalışma klasöründeki `data/` (yukarıdaki komutlarla başlatırsanız `src/Host/Vyxum.Host/data/`) ya da Windows'ta `%ProgramData%\Vyxum`. Her açılışta platform ve her modül bekleyen EF Core şema geçişlerini (migration) uygular; böylece var olan veritabanı güncellenir ve kayıtlarını korur. Bir SQLite veritabanı değişmeden önce veri klasöründeki `backupsvyxum-<tarih>-<saat>-migration.db` dosyasına kopyalanır; en yeni 10 kopya tutulur. SQL Server veritabanı kopyalanmaz: güncellemeden önce yedeğini kendiniz alın. Geçiş eklemek için [CONTRIBUTING.md](CONTRIBUTING.md#veritabanı-geçişleri) dosyasına bakın.

## Yapılandırma

Anahtarlar sırasıyla programın yanındaki `appsettings.json`, veri klasöründeki ortak ayar dosyası `vyxum.json`, ortam değişkenleri (örneğin `Vyxum__Database__Provider`) ve komut satırı argümanlarından okunur; sonraki kaynak öncekini geçersiz kılar.

`vyxum.json` dosyasını Web, Agent ve masaüstü penceresi birlikte kullanır; program klasöründeki `appsettings.json` güncellemede değişir, bu dosya değişmez. Dosyayı kurulum yazar, sihirbaz da başka bir veritabanı seçildiğinde günceller; müşteri elle düzenlemez. Yeri `Vyxum:DataDirectory` ayarını izler: veri klasörü ortam değişkeni ya da komut satırıyla verilirse dosya da oraya taşınır. Dosya açılışta bir kez okunur; veritabanı değişince Web, Windows servisi olarak çalışıyorsa kendini yeniden başlatır, değilse elle yeniden başlatılması gerektiğini yazar.

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| `Vyxum:Database:Provider` | `Sqlite` | `Sqlite` ya da `SqlServer` |
| `Vyxum:Database:ConnectionString` | Veri klasöründeki `vyxum.db` SQLite dosyası | Sağlayıcı `SqlServer` ise zorunludur |
| `Vyxum:DataDirectory` | Windows'ta `%ProgramData%\Vyxum`, diğerlerinde `data/` | Varsayılan SQLite dosyasının klasörü |
| `Vyxum:Modules` | boş | Modüllerin yükleneceği derleme adlarının listesi, örneğin `Vyxum:Modules:0` |
| `Vyxum:Outbox:PollingInterval` | `00:00:00.5` | Outbox'ların ne sıklıkla kontrol edileceği |
| `Vyxum:Outbox:BatchSize` | `100` | Her kontrolde bir outbox'tan okunacak en çok ileti sayısı |
| `Vyxum:Outbox:RetryBaseDelay` | `00:00:01` | İlk yeniden denemeden önceki bekleme; her hatada iki katına çıkar |
| `Vyxum:Agents:Key` | kurulum yazar | Her agent'ın göndermesi gereken anahtar. Kurulum, web uygulaması ve aynı bilgisayardaki agent için rastgele bir anahtar yazar; başka bilgisayardaki bir agent aynı değere `Vyxum:Agent:Key` olarak ihtiyaç duyar. Anahtar yoksa yalnızca bu bilgisayardan gelen bağlantılar kabul edilir. Web tarayıcısından gelen bağlantılar her zaman reddedilir. |
| `Vyxum:AllowedHosts` | boş | Web uygulamasına ulaşılan ek ana bilgisayar adları, `;` ile ayrılır. IP adresleri, `localhost` ve bu bilgisayarın adı her zaman çalışır; diğer adlar reddedilir (DNS rebinding koruması). |

Süre değerleri .NET `TimeSpan` biçimindedir (`hh:mm:ss` ya da `d.hh:mm:ss`).

## Mimari kurallar

Mimari testlerin kontrol edebildiği kurallar bu testlerle denetlenir.

- Modüller birbirini çağırmaz. Yalnızca outbox ve inbox üzerinden iletilen entegrasyon olaylarıyla konuşurlar.
- Bir modül, Vyxum derlemeleri arasından yalnızca `Vyxum.Platform.Abstractions` derlemesine başvurur. Başka bir modüle, sunucuya, platform çalışma zamanına, cihazlara ya da agent'a başvurmaz.
- Her modül tam olarak bir `ModuleDbContext` sahiplenir ve tablo adları kendi önekiyle başlar.
- Platform hiçbir modülü tanımaz. Platform ve `Vyxum.Platform.Abstractions` hiçbir modül derlemesine başvurmaz ve hiçbir modül ad alanını kullanmaz. Abstractions başka hiçbir Vyxum derlemesine başvurmaz.
- Platform ve sunucu hiçbir cihaz derlemesine ve agent'a başvurmaz. Sunucu örnek modüllere başvurmaz.
- `Vyxum.Devices` hiçbir Vyxum derlemesine başvurmaz. Protokoller, taşımalar ve simülatör yalnızca `Vyxum.Devices` derlemesine başvurur.
- `Vyxum.Agent` hiçbir platform derlemesine ve sunucuya başvurmaz.
- Hiçbir modül zorunlu değildir. İsteğe bağlı bir yetenek yoksa modül çalışmaya devam eder; yalnızca yeteneğin kullanılamadığını bildirir.
- Kodda sağlayıcıya özel SQL bulunmaz. Veri erişimi hem SQLite hem SQL Server üzerinde testten geçmelidir.

## F0 sürümünün bilinen sınırları

- **Geçişi olmayan modüller.** Etkin sağlayıcı için geçiş içermeyen bir modülün tabloları bir kez oluşturulur; modül geçiş içerene kadar şeması güncellenemez.
- **Tek düğümlü dağıtıcı.** Her veritabanı için tek bir sunucu örneği çalıştırın. Aynı veritabanındaki birden fazla dağıtıcı birbiriyle koordine edilmez.
- **Outbox temizliği yok.** İletilmiş mesajlar outbox tablolarında tutulur.
- **Eklenti klasöründen yükleme yok.** Modüller yapılandırmada listelenen derlemelerden yüklenir; eklenti klasöründen yükleme F4 fazında planlanmıştır.

## Katkıda bulunma

Bkz. [CONTRIBUTING.md](CONTRIBUTING.md).

## Lisans

Apache Lisansı 2.0. Bkz. [LICENSE](LICENSE).
