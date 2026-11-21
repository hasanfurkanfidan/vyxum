# 1. Genel bakış

[Rehber dizini](../README.md#türkçe) · Sonraki: [2. Başlarken](02-baslarken.md)

Bu bölüm Vyxum'un neyden oluştuğunu ve nedenini anlatır. İlk önce bunu okuyun. Kodu henüz bilmeniz gerekmez.

## Vyxum ne yapar?

Vyxum bir tartım yazılımıdır. Bir dükkânda ya da fabrikada, bir bilgisayara kabloyla (seri port) ya da ağla bağlı bir **terazi** vardır. Operatör bir ürün seçer, malı teraziye koyar ve bir tuşa basar. Vyxum ağırlığı kaydeder, ödenecek tutarı hesaplar ve listelenebilen, iptal edilebilen, Excel'e aktarılabilen bir kayıt tutar.

Vyxum **müşterinin kendi bilgisayarına** kurulur. Bulut kısmı yoktur ve **internetsiz** çalışmak zorundadır. Aşağıdaki birçok tercih bu tek gerçekten çıkar: yazı tipleri pakete gömülür, dış servis yoktur, varsayılan veritabanı bir dosyadır.

## Üç program

Vyxum çalışırken üç program birlikte çalışır:

| Program | Proje | Nedir |
|---|---|---|
| **Web sunucusu (host)** | `src/Host/Vyxum.Host` | ASP.NET Core uygulaması. Web ekranlarını, HTTP API'sini ve veritabanını sunar. "Vyxum Web" Windows servisi olarak çalışır. |
| **Agent** | `src/Agent/Vyxum.Agent` | Terazinin yanında duran, ağırlığı okuyup sunucuya gönderen küçük program. "Vyxum Agent" Windows servisi olarak çalışır. |
| **Masaüstü penceresi** | `src/Desktop/Vyxum.Desktop` | Web ekranlarını tarayıcı çerçevesi olmadan gösteren pencere (`Vyxum.exe`). Web uygulamasının yalnızca bir çerçevesidir. |

```
 terazi ──kablo/ağ──> Agent ──SignalR──> Web sunucusu ──HTTP──> Tarayıcı / Masaüstü penceresi
                                              │
                                              └──> Veritabanı (SQLite dosyası ya da SQL Server)
```

Agent neden ayrı? Terazi *belirli bir bilgisayara* bağlıdır, ama sunucu başka odadaki bir makinede çalışıyor olabilir. En basit kurulumda ikisi aynı bilgisayardadır. Agent'ı ayırmak tek sunucunun birkaç bilgisayardaki terazilere hizmet vermesini sağlar ve donanım kodunu iş kodundan uzak tutar.

## Modüler monolit

Sunucu **tek bir programdır**, ama içi **modüllere** ayrılmıştır. Modül bir özellik alanıdır; örneğin Products (ürün kataloğu) ya da Weighing (tartım kayıtları). Bu depoda iki modül vardır. Başka modüller ayrı bir depoda geliştirilir ve aynı yolla yüklenir.

Neden modül? Bazı müşteriler yalnızca bazı özellikler için öder. Bu yüzden bir modül, başka bir modül yokken de çalışmalı ve geri kalanı değiştirmeden eklenebilmelidir. Bunu sağlamak için kod sıkı kurallara uyar (**değişmezler**; [`CLAUDE.md`](../../../CLAUDE.md) içinde listelenir, testlerle denetlenir):

1. **Modüller birbirini çağırmaz.** Weighing, Products'ı çağırmaz; birbirlerine *entegrasyon olayları* gönderirler ([4. bölüm](04-moduller.md)).
2. Bir modül Vyxum derlemelerinden yalnızca birine başvurur: `Vyxum.Platform.Abstractions`.
3. **Platform** (`Vyxum.Platform`) hiçbir modüle başvurmaz ve hiçbir modülü adıyla tanımaz.
4. Hiçbir modül zorunlu değildir. Bir modül başka bir modülün sunduğu özelliği istiyorsa `ICapabilityRegistry`'ye var mı diye sorar ve yoksa idare eder.
5. Platform hiçbir terazi markasını tanımaz. Yalnızca "bir ağırlık okundu" olayını bilir.
6. Kod veritabanından bağımsızdır: elle SQL yok; yalnızca hem SQLite hem SQL Server'da çalışan LINQ ve EF Core özellikleri.

Bir değişiklik bunlardan birini bozacaksa durun ve konuşun. Mimari testler (`tests/Vyxum.ArchitectureTests`) zaten başarısız olur.

## Katmanlar, alttan üste

| Katman | Projeler | İşi |
|---|---|---|
| Sözleşmeler | `Vyxum.Platform.Abstractions` | Modüllerin kullanabileceği küçük arayüz ve taban sınıf kümesi (`IModule`, `ModuleDbContext`, `IIntegrationEvent`, `ILiveWeightSource`, ...). |
| Platform | `Vyxum.Platform` | Modülleri bulur ve denetler, veritabanını oluşturur ve yükseltir, olayları iletir, Agent'larla konuşur, kurulum sihirbazı, arayüz metinleri. |
| Modüller | `Vyxum.Modules.Products`, `Vyxum.Modules.Weighing` | Özellikler. |
| Host | `Vyxum.Host` | Her şeyi birleştirir: platformu başlatır, modülleri listeler, web arayüzünü sunar. |
| Cihazlar | `Vyxum.Devices*` | Terazi okuma. Platformdan tamamen bağımsızdır. Yalnızca Agent kullanır. |
| Agent | `Vyxum.Agent` | Cihazları çalıştırır, sunucuya bildirir. |
| Web arayüzü | `src/Web/vyxum-web` | React ekranları. |

Platform ile cihaz dünyası birbirini doğrudan görmez. Tek köprü **agent hub**'ıdır (bir SignalR bağlantısı) ve modüllerin o anki ağırlığı okumak için kullandığı küçük `ILiveWeightSource` arayüzüdür.

## Sözlük

- **Terazi / cihaz**: tartım donanımı.
- **Taşıma (transport)**: baytların cihaza nasıl gittiği (seri port, TCP ya da simülatör).
- **Protokol**: bu baytların nasıl anlaşıldığı (MT-SICS, Toledo Continuous, genel ASCII).
- **Modül**: kendi tabloları, uç noktaları ve metinleri olan özellik alanı.
- **Yetenek (capability)**: bir modülün sunduğu adlandırılmış özellik, örneğin `products.catalog`.
- **Entegrasyon olayı**: bir modülün yayınladığı, diğerlerinin işleyebildiği ileti.
- **Outbox / inbox**: program çökse bile olay iletimini güvenli yapan veritabanı tabloları ([4. bölüm](04-moduller.md)).
- **Geçiş (migration)**: üretilmiş, sürümlü veritabanı değişikliği ([5. bölüm](05-veritabani.md)).
- **Veri klasörü**: Vyxum'un verilerini tuttuğu yer: Windows'ta `%ProgramData%\Vyxum`, başka yerde çalışma klasörü altındaki `data/`.
- **`vyxum.json`**: veri klasöründeki, sunucu, agent ve masaüstü penceresinin paylaştığı ayar dosyası.

## Bu depoda ne var, ne yok

Bu depo **açık çekirdektir** (Apache-2.0). Ek modüller başka yerde geliştirilir ve buraya hiçbir zaman eklenmez. İş bilgisi (fiyat, satış planı) ve imza anahtarı gibi gizli bilgiler bu depoya asla konmaz.
