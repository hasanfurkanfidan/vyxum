# 9. Sorun giderme

[Dizin](../README.md#türkçe) · Önceki: [8. Kurulum dosyası ve sürüm](08-surum.md) · Sonraki: [10. Kararlar](10-kararlar.md)

Biçim: **belirti → olası neden → ne yapmalı.**

## Derleme

- **`dotnet: command not found` ya da SDK hataları** → .NET 10 yok ya da çok eski. Kurun; `global.json` `10.0.100` ya da daha yeni bir özellik bandı ister.
- **Derleme, hata sayılan uyarılarla başarısız** → beklenen durum: uyarılar hatadır. Uyarıyı düzeltin; `TreatWarningsAsErrors`'ı kapatmayın.
- **`BuildWebUi` adımında hata / "npm was not found"** → Node.js 22 kurulu değil. Kurun ya da `-p:SkipUiBuild=true` ile derleyin. Sunucu o zaman "The web UI is not built" yazar ve siz `src/Web/vyxum-web` içinde `npm ci && npm run build` çalıştırana kadar `/` 404 döner.
- **Paket sürümü hatası** → sürümler yalnızca `Directory.Packages.props` içine yazılır.
- **`dotnet ef` başarısız** → önce `dotnet tool restore`; `-- --provider Sqlite|SqlServer` geçirin.

## Testler

- **SQL Server testleri atlandı görünüyor** → `VYXUM_TEST_SQLSERVER` ayarlı değil. SQL Server'ı başlatın ([2. bölümdeki](02-baslarken.md) Docker komutu) ve veritabanı adı olmadan ayarlayın.
- **SQL Server testleri bağlanamıyor** → kapsayıcının açılması yaklaşık 20 sn sürer; `docker ps` bakın; bağlantı dizesinde `TrustServerCertificate=True` olmalı.
- **`Every_model_change_has_a_migration` başarısız** → modeli değiştirdiniz ama **iki** sağlayıcı için geçiş eklemediniz ([5. bölüm](05-veritabani.md)).
- **Bir mimari test başarısız** → yasak bir başvuru eklediniz. Testi değiştirmek yerine `ProjectReference`'ı kaldırın. Kural gerçekten değişecekse önce konuşun.
- **Bir Türkçe kaynak testi başarısız** → `Strings.tr.resx` içinde bir anahtar eksik ya da boş değer var.
- **Test tek başına geçiyor, başkalarıyla başarısız** → paylaşılan durum ya da sabit port arayın; testler benzersiz veritabanı kullanır ve dosya paylaşmamalıdır.

## Sunucuyu çalıştırma

- **Sunucu açılmıyor ve modül sorunları listesi yazıyor** → `ModuleValidationException`: her satırı okuyun (yinelenen kimlik, eksik zorunlu yetenek, özniteliksiz olay, ...).
- **`Configuration 'Vyxum:Database:ConnectionString' is required`** → sağlayıcı `SqlServer` ama bağlantı dizesi yok.
- **Port kullanımda** → `--urls http://localhost:5001` geçirin (agent'a da `--Vyxum:Agent:ServerUrl` verin).
- **Veritabanı dosyası kilitli / açılamıyor** → başka bir sunucu örneği aynı veri klasörünü kullanıyor. Veritabanı başına tek sunucu çalıştırın.
- **Olaylar takılmış görünüyor** → `<önek>_OutboxMessages` tablosuna bakın: `ProcessedAt` boş, `Attempts` artan ve `LastError` dolu bir satır hangi işleyicinin başarısız olduğunu gösterir; yeniden denemeler 5 dk'ya kadar yavaşlar.
- **Veri ve günlükler nerede?** → veri: `%ProgramData%\Vyxum` (Windows) ya da çalışma klasörü altındaki `data/`. Windows servisi olarak günlükler Windows olay günlüğüne, `dotnet run` ile konsola gider.
- **Kurulum sihirbazı veritabanını değiştirince sunucu kapanıyor** → bilerek; servis olarak kurtarma ayarlarıyla yeniden başlar, konsoldan başlattıysanız elle yeniden başlatın.

## Agent ve teraziler

- **`/api/devices` boş** → hiçbir agent bağlanmamış. Agent'ın `ServerUrl`'ini, sunucunun çalıştığını ve agent anahtarını denetleyin. Sunucuda `Vyxum:Agents:Key` yoksa yalnızca **aynı bilgisayardaki** agent'lar kabul edilir (uzak agent 403 alır); anahtar varsa agent `Vyxum:Agent:Key` ile aynı değeri göndermelidir (yanlış anahtar: 401).
- **Cihaz durumu `Error`** → ayarları geçersiz; ileti ayarın adını söyler.
- **Cihaz `Disconnected` ve yeniden deniyor** → yanlış port, portu başka program kullanıyor (örneğin başka bir Vyxum Agent ya da terminal programı), yanlış baud ya da eşlik, kablo. `/api/devices` içindeki `lastError` daha fazlasını söyler.
- **Keşif `InUse` diyor** → çalışan bir cihaz o portu zaten kullanıyor; agent onu asla açmaz.
- **Bağlı ama ağırlık yok** → protokol uyuşmazlığı. Ayarlar → Cihazlar → Ara'yı kullanın ya da agent günlüğündeki ham metne bakın. MT-SICS için komut `SI` olsun.
- **Sıfırla düğmesi pasif** → cihazın protokolü sıfırlamayı desteklemiyor (yalnızca MT-SICS destekler).
- **Agent ayar değişiklikleri etkisiz** → agent'ın kendi `Devices` listesi boş değilse web ayarlarındaki cihazlar yok sayılır; listeyi boşaltın.

## Kurulum dosyası

- **Kurulum servislerin durdurulamadığını söylüyor** → bilgisayarı yeniden başlatın ve kurulumu tekrar çalıştırın.
- **Bir servis oluşturulamadı** → Windows Hizmetler ekranını açın; "Vyxum Web" ve "Vyxum Agent"ı elle başlatın; `sc.exe query "Vyxum Web"` çalıştırın.
- **Derleme betiği: "Inno Setup 6 (ISCC.exe) was not found"** → Inno Setup 6'yı kurun ya da `-SkipCompile` kullanın.
- **Masaüstü penceresi bir dakika "Starting Vyxum..." gösteriyor** → sunucu yapılandırılmış adreste (`vyxum.json` içindeki `Urls`'in ilk girdisi) `GET /health` isteğine yanıt vermiyor.

## Web arayüzü

- **Metinler `products.Title` gibi anahtar olarak görünüyor** → anahtar kaynak dosyalarında yok ya da eklendikten sonra sunucu yeniden derlenmedi.
- **`npm run build` sonrası arayüz eski içeriği gösteriyor** → tarayıcıyı sert yenileyin; derlemenin `src/Host/Vyxum.Host/wwwroot` içine yazdığını denetleyin.
- **`npm run dev` API hataları gösteriyor** → sunucu 5000 portunda çalışmıyor.
