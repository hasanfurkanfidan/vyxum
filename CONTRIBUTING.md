# Contributing to Vyxum

Thank you for your interest. This guide is short on purpose. Türkçe bölüm aşağıdadır.

## Build and test

```bash
dotnet build -warnaserror
dotnet test
```

SQL Server tests run when `VYXUM_TEST_SQLSERVER` is set to a connection string without a database name; otherwise they are skipped. See the [README](README.md) for a Docker example. Please run the SQL Server tests before opening a pull request that touches data access.

## Rules for changes

- **One change per pull request.** A pull request does one thing and is easy to review. Split unrelated work.
- **Tests are required.** Every behavior change or bug fix comes with a test. Data access changes must pass on both SQLite and SQL Server.
- **Build with no warnings.** `dotnet build -warnaserror` must succeed.
- **Respect the architecture rules** listed in the [README](README.md#architecture-rules). Modules talk only through integration events and reference only `Vyxum.Platform.Abstractions`. The platform does not know any module. Do not write provider-specific SQL.
- **New packages must be license-compatible.** Only MIT, BSD, and Apache-2.0 compatible licenses are accepted. Check the license before adding a NuGet package and mention it in the pull request.
- **Code is in English:** names, comments, and documentation for developers.
- **UI strings go in both languages.** Every user-facing string lives in a resource file, in both `Strings.resx` (English) and `Strings.tr.resx` (Turkish). Do not hard-code text in code.
- **No internet dependencies in the product.** The product must work offline. Do not add CDN links, remote fonts, or calls to external services.

## Adding a protocol

Each protocol is its own project, `src/Devices/Vyxum.Devices.<Name>`, which references only `Vyxum.Devices`. Implement `IScaleProtocol`. Keep the parser a pure function, and test it with unit tests that include recorded raw lines from a real scale. Add a scenario to `Vyxum.Devices.Simulator` so the protocol runs without hardware. Finally, register the type name in the Agent's configuration factory (`AgentConfiguration`). The protocol must only read the weight; never send commands that tare, zero or change the device. The only exception is zero on an explicit user request: a protocol may implement `ISupportsZero` and send its zero command once per call, serialized with its read loop.

## Pull requests

- Describe what changed and why.
- List how you tested it.
- Keep commits focused. Each working step is its own commit with an English, imperative message.

## Yeni protokol ekleme

Her protokol kendi projesinde, `src/Devices/Vyxum.Devices.<Ad>` içinde yer alır ve yalnızca `Vyxum.Devices` derlemesine başvurur. `IScaleProtocol` arayüzünü uygulayın. Ayrıştırıcıyı saf bir fonksiyon olarak tutun ve gerçek bir teraziden alınmış ham satırları içeren birim testleriyle test edin. Protokolün donanımsız çalışması için `Vyxum.Devices.Simulator` içine bir senaryo ekleyin. Son olarak tür adını Agent'ın yapılandırma fabrikasına (`AgentConfiguration`) kaydedin. Protokol yalnızca ağırlığı okumalıdır; cihazı dara alan, sıfırlayan ya da değiştiren komutlar göndermeyin.

---

# Vyxum'a katkıda bulunma

İlgilendiğiniz için teşekkürler. Bu rehber bilerek kısa tutulmuştur.

## Derleme ve test

```bash
dotnet build -warnaserror
dotnet test
```

SQL Server testleri, `VYXUM_TEST_SQLSERVER` veritabanı adı içermeyen bir bağlantı dizesi olarak tanımlıysa çalışır; aksi halde atlanır. Docker ile örnek için [README](README.tr.md) dosyasına bakın. Veri erişimine dokunan bir değişiklik göndermeden önce SQL Server testlerini çalıştırın.

## Değişiklik kuralları

- **Bir çekme isteğinde tek bir değişiklik.** Her çekme isteği tek bir iş yapar ve kolay incelenir. İlgisiz işleri ayırın.
- **Testler zorunludur.** Her davranış değişikliği ve hata düzeltmesi bir testle gelir. Veri erişimi değişiklikleri hem SQLite hem SQL Server üzerinde geçmelidir.
- **Uyarısız derleme.** `dotnet build -warnaserror` hatasız tamamlanmalıdır.
- **Mimari kurallara uyun.** Bkz. [README](README.tr.md#mimari-kurallar). Modüller yalnızca entegrasyon olaylarıyla konuşur ve yalnızca `Vyxum.Platform.Abstractions` derlemesine başvurur. Platform hiçbir modülü tanımaz. Sağlayıcıya özel SQL yazılmaz.
- **Yeni paketlerin lisansı uyumlu olmalıdır.** Yalnızca MIT, BSD ve Apache-2.0 ile uyumlu lisanslar kabul edilir. NuGet paketi eklemeden önce lisansı kontrol edin ve çekme isteğinde belirtin.
- **Kod İngilizcedir:** ad, yorum ve geliştiriciye dönük belgeler İngilizce yazılır.
- **Arayüz metinleri iki dilde bulunur.** Kullanıcıya görünen her metin kaynak dosyasındadır: hem `Strings.resx` (İngilizce) hem `Strings.tr.resx` (Türkçe). Metin koda gömülmez.
- **Ürün internete bağımlı olmaz.** Ürün çevrimdışı çalışmalıdır. CDN bağlantısı, uzak yazı tipi ya da dış servis çağrısı eklemeyin.

## Çekme istekleri

- Neyin neden değiştiğini açıklayın.
- Nasıl test ettiğinizi yazın.
- Commit'leri odaklı tutun. Çalışan her adım, İngilizce ve emir kipinde bir mesajla ayrı bir commit olur.
