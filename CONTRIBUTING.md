# Contributing to Vyxum

Thank you for your interest. This guide is short on purpose. Türkçe bölüm aşağıdadır.

## Build and test

```bash
dotnet build -warnaserror
dotnet test
```

SQL Server tests run when `VYXUM_TEST_SQLSERVER` is set to a connection string without a database name; otherwise they are skipped. See the [README](README.md) for a Docker example. Please run the SQL Server tests before opening a pull request that touches data access.

To build without the web UI, as CI does for the .NET job, use `dotnet build -warnaserror -p:SkipUiBuild=true`. See [Web UI](#web-ui).

## Rules for changes

- **One change per pull request.** A pull request does one thing and is easy to review. Split unrelated work.
- **Tests are required.** Every behavior change or bug fix comes with a test. Data access changes must pass on both SQLite and SQL Server.
- **Build with no warnings.** `dotnet build -warnaserror` must succeed.
- **Respect the dependency rules** below. Do not write provider-specific SQL.
- **New packages must be license-compatible.** Only MIT, BSD, and Apache-2.0 compatible licenses are accepted. Check the license before adding a NuGet package and mention it in the pull request.
- **Code is in English:** names, comments, and documentation for developers.
- **UI strings go in both languages.** Every user-facing string lives in a resource file, in both `Strings.resx` (English) and `Strings.tr.resx` (Turkish). Do not hard-code text in code.
- **No internet dependencies in the product.** The product must work offline. Do not add CDN links, remote fonts, or calls to external services.

## Dependency rules

The architecture tests in `tests/Vyxum.ArchitectureTests` check these rules, and the [README](README.md#architecture-rules) lists the same rules. A change that breaks a rule fails the build. A new rule needs a new test. The check reads the references in the compiled assemblies, so a project reference that no code uses may not be caught; do not leave unused references in a `.csproj`.

- Modules do not call each other. They communicate only through integration events, delivered via the outbox and inbox.
- A module references only `Vyxum.Platform.Abstractions` among Vyxum assemblies. It does not reference another module, the host, the platform runtime, the devices or the agent.
- Each module owns exactly one `ModuleDbContext`, and its table names start with its prefix.
- The platform knows no module. The platform and `Vyxum.Platform.Abstractions` reference no module assembly and use no module namespace. Abstractions references no other Vyxum assembly.
- The platform and the host reference no device assembly and not the agent. The host does not reference sample modules.
- `Vyxum.Devices` references no Vyxum assembly. Protocols, transports and the simulator reference only `Vyxum.Devices`.
- `Vyxum.Agent` references no platform assembly and not the host.

## Modules

Products and Weighing are built into the host. `HostModules.BuiltIn` in `src/Host/Vyxum.Host/HostModules.cs` lists them, so the host always loads them. A new module needs its project, a project reference from the host, a line in `HostModules.BuiltIn`, and an entry in the module list of the architecture tests. The user-facing description is in the [README](README.md#modules).

## Database migrations

Schema changes are EF Core migrations, generated with `dotnet ef` (a repository-local tool: run `dotnet tool restore` once). Never write SQL by hand. The platform and every module have their own migrations, in two folders, one per provider: `Migrations/Sqlite` and `Migrations/SqlServer`. Each context records them in its own history table (`__plt_migrations` for the platform, `__<prefix>_migrations` for a module). On start the host applies what is pending, after a SQLite backup.

After changing a model, add a migration for both providers with the same name. From the repository root, for the Products module:

```bash
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/Sqlite -- --provider Sqlite
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/SqlServer -- --provider SqlServer
```

Use `src/Modules/Weighing/Vyxum.Modules.Weighing` for Weighing and `src/Platform/Vyxum.Platform` for the platform. To undo the last one, run `dotnet ef migrations remove` with the same `--project` and `-- --provider` arguments. Review the generated code, and run the tests: a model change without a migration for both providers fails `Every_model_change_has_a_migration`. A migration that drops or rewrites data needs a test with existing rows.

A new module (including a module outside this repository) ships its migrations the same way. Its project references `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets=all` and contains one line:

```csharp
internal sealed class DesignTimeFactory : DesignTimeDbContextFactory<MyDbContext>;
```

For a new context, EF names both model snapshots `<Context>ModelSnapshot`. After creating the first SQLite migration, rename that snapshot class and its file to `<Context>SqliteModelSnapshot`; after the first SQL Server migration, rename it to `<Context>SqlServerModelSnapshot`. Later migrations need nothing extra.

## Web UI

The UI is in `src/Web/vyxum-web` (React, TypeScript, Vite). CI uses Node.js 22; use the same version.

- `npm ci` installs the packages. `npm run dev` starts Vite with a proxy to `http://localhost:5000`, so start the host on that port first.
- `npm run typecheck`, `npm test` and `npm run build` are the checks CI runs. `npm run build` writes to `src/Host/Vyxum.Host/wwwroot`, which is not committed.
- Put every user-facing string in a module's `Strings.resx` and `Strings.tr.resx`. The UI reads them from `/api/ui/strings`; the code must not contain literal text.
- Do not add CDN links, remote fonts or calls to external services.

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

Web arayüzünü derlemeden yapılandırmak için `dotnet build -warnaserror -p:SkipUiBuild=true` kullanın; CI .NET işi bunu yapar. Bkz. [Web arayüzü](#web-arayüzü).

## Değişiklik kuralları

- **Bir çekme isteğinde tek bir değişiklik.** Her çekme isteği tek bir iş yapar ve kolay incelenir. İlgisiz işleri ayırın.
- **Testler zorunludur.** Her davranış değişikliği ve hata düzeltmesi bir testle gelir. Veri erişimi değişiklikleri hem SQLite hem SQL Server üzerinde geçmelidir.
- **Uyarısız derleme.** `dotnet build -warnaserror` hatasız tamamlanmalıdır.
- **Mimari kurallara uyun.** Bkz. [README](README.tr.md#mimari-kurallar). Modüller yalnızca entegrasyon olaylarıyla konuşur ve yalnızca `Vyxum.Platform.Abstractions` derlemesine başvurur. Platform hiçbir modülü tanımaz. Sağlayıcıya özel SQL yazılmaz.
- **Yeni paketlerin lisansı uyumlu olmalıdır.** Yalnızca MIT, BSD ve Apache-2.0 ile uyumlu lisanslar kabul edilir. NuGet paketi eklemeden önce lisansı kontrol edin ve çekme isteğinde belirtin.
- **Kod İngilizcedir:** ad, yorum ve geliştiriciye dönük belgeler İngilizce yazılır.
- **Arayüz metinleri iki dilde bulunur.** Kullanıcıya görünen her metin kaynak dosyasındadır: hem `Strings.resx` (İngilizce) hem `Strings.tr.resx` (Türkçe). Metin koda gömülmez.
- **Ürün internete bağımlı olmaz.** Ürün çevrimdışı çalışmalıdır. CDN bağlantısı, uzak yazı tipi ya da dış servis çağrısı eklemeyin.

## Bağımlılık kuralları

Mimari testler (`tests/Vyxum.ArchitectureTests`) bu kuralları denetler; [README](README.tr.md#mimari-kurallar) aynı kuralları listeler. Bir kuralı bozan değişiklik derlemeyi başarısız kılar. Yeni bir kural için yeni bir test gerekir. Denetim derlenmiş derlemelerdeki başvuruları okur; hiçbir kodun kullanmadığı bir proje başvurusu yakalanmayabilir, bu yüzden .csproj dosyalarında kullanılmayan başvurular bırakmayın.

- Modüller birbirini çağırmaz. Yalnızca outbox ve inbox üzerinden iletilen entegrasyon olaylarıyla konuşurlar.
- Bir modül, Vyxum derlemeleri arasından yalnızca `Vyxum.Platform.Abstractions` derlemesine başvurur. Başka bir modüle, sunucuya, platform çalışma zamanına, cihazlara ya da agent'a başvurmaz.
- Her modül tam olarak bir `ModuleDbContext` sahiplenir ve tablo adları kendi önekiyle başlar.
- Platform hiçbir modülü tanımaz. Platform ve `Vyxum.Platform.Abstractions` hiçbir modül derlemesine başvurmaz ve hiçbir modül ad alanını kullanmaz. Abstractions başka hiçbir Vyxum derlemesine başvurmaz.
- Platform ve sunucu hiçbir cihaz derlemesine ve agent'a başvurmaz. Sunucu örnek modüllere başvurmaz.
- `Vyxum.Devices` hiçbir Vyxum derlemesine başvurmaz. Protokoller, taşımalar ve simülatör yalnızca `Vyxum.Devices` derlemesine başvurur.
- `Vyxum.Agent` hiçbir platform derlemesine ve sunucuya başvurmaz.

## Modüller

Products ve Weighing modülleri sunucuya dahildir. `src/Host/Vyxum.Host/HostModules.cs` içindeki `HostModules.BuiltIn` bunları listeler; sunucu bunları her zaman yükler. Yeni bir modül için projesi, sunucudan bir proje başvurusu, `HostModules.BuiltIn` içinde bir satır ve mimari testlerin modül listesinde bir kayıt gerekir. Kullanıcıya dönük açıklama [README](README.tr.md#modüller) dosyasındadır.

## Veritabanı geçişleri

Şema değişiklikleri `dotnet ef` ile üretilen EF Core geçişleridir (depoya yerel araç: bir kez `dotnet tool restore` çalıştırın). SQL elle yazılmaz. Platformun ve her modülün kendi geçişleri vardır; sağlayıcı başına bir klasör: `Migrations/Sqlite` ve `Migrations/SqlServer`. Her bağlam bunları kendi geçmiş tablosuna yazar (platform için `__plt_migrations`, modül için `__<önek>_migrations`). Sunucu açılışta, SQLite yedeğini aldıktan sonra bekleyen geçişleri uygular.

Modeli değiştirdikten sonra iki sağlayıcı için aynı adla bir geçiş ekleyin. Depo kökünden, Products modülü için:

```bash
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/Sqlite -- --provider Sqlite
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/SqlServer -- --provider SqlServer
```

Weighing için `src/Modules/Weighing/Vyxum.Modules.Weighing`, platform için `src/Platform/Vyxum.Platform` kullanın. Sonuncuyu geri almak için aynı `--project` ve `-- --provider` değerleriyle `dotnet ef migrations remove` çalıştırın. Üretilen kodu inceleyin ve testleri çalıştırın: iki sağlayıcıdan biri için geçişi olmayan model değişikliği `Every_model_change_has_a_migration` testini kırar. Veri silen ya da dönüştüren bir geçiş, içinde kayıt olan bir veritabanıyla test edilir.

Yeni bir modül (bu deponun dışındaki bir modül de) geçişlerini aynı yolla taşır. Projesi `Microsoft.EntityFrameworkCore.Design` paketine `PrivateAssets=all` ile başvurur ve tek bir satır içerir:

```csharp
internal sealed class DesignTimeFactory : DesignTimeDbContextFactory<MyDbContext>;
```

Yeni bir bağlamda EF iki model anlık görüntüsüne de `<Bağlam>ModelSnapshot` adını verir. İlk SQLite geçişinden sonra bu sınıfı ve dosyasını `<Bağlam>SqliteModelSnapshot` olarak, ilk SQL Server geçişinden sonra `<Bağlam>SqlServerModelSnapshot` olarak yeniden adlandırın. Sonraki geçişler ek bir iş gerektirmez.

## Web arayüzü

Arayüz `src/Web/vyxum-web` klasöründedir (React, TypeScript, Vite). CI Node.js 22 kullanır; aynı sürümü kullanın.

- `npm ci` paketleri kurar. `npm run dev` Vite'ı `http://localhost:5000` adresine yönlendirmeyle başlatır; bu yüzden sunucuyu önce bu portta başlatın.
- CI'ın çalıştırdığı kontroller `npm run typecheck`, `npm test` ve `npm run build`dir. `npm run build` çıktısını, commit edilmeyen `src/Host/Vyxum.Host/wwwroot` klasörüne yazar.
- Kullanıcıya görünen her metni bir modülün `Strings.resx` ve `Strings.tr.resx` dosyalarına koyun. Arayüz bunları `/api/ui/strings` üzerinden okur; kodda düz metin bulunmamalıdır.
- CDN bağlantısı, uzak yazı tipi ya da dış servis çağrısı eklemeyin.

## Çekme istekleri

- Neyin neden değiştiğini açıklayın.
- Nasıl test ettiğinizi yazın.
- Commit'leri odaklı tutun. Çalışan her adım, İngilizce ve emir kipinde bir mesajla ayrı bir commit olur.
