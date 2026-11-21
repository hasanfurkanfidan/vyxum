# 5. Veritabanı ve geçişler

[Dizin](../README.md#türkçe) · Önceki: [4. Modüller ve olaylar](04-moduller.md) · Sonraki: [6. Cihazlar ve Agent](06-cihazlar-ve-agent.md)

## Tek veritabanı, çok sahip

Vyxum **EF Core** ve iki sağlayıcı kullanır: **SQLite** (varsayılan; tek dosya, kurulum yok) ve **SQL Server**. PostgreSQL planlıdır. Sağlayıcı `Vyxum:Database:Provider` ile seçilir (`Sqlite` ya da `SqlServer`); SQL Server için ayrıca `Vyxum:Database:ConnectionString` gerekir. SQLite varsayılan olarak veri klasöründeki `vyxum.db` dosyasını kullanır.

Tüm modüller ve platform **tek veritabanını** paylaşır. Karışmasın diye her sahibin kendi bağlamı vardır:

- Platformun `PlatformDbContext`'i vardır (kurulu modüller, cihaz ayarları, platform ayarları). Geçmiş tablosu `__plt_migrations`.
- Her modülün tam bir `ModuleDbContext`'i ve bir **tablo öneki** vardır (Products için `prd_`, Weighing için `wgh_`). SQLite'ta şema olmadığından her iki sağlayıcıda önek kullanılır. Geçmiş tablosu: `__<önek>_migrations`.

## Veri erişim kodu için kurallar

- **Sağlayıcıya özel SQL yazmayın.** İki sağlayıcıda da çalışan LINQ ve EF Core API'leri kullanın. (SQL Server ve SQLite örneğin `DateTimeOffset` ve `decimal` sıralama ve karşılaştırmasında farklıdır; birinde çalışan kod diğerinde bozulabilir. Testlerin ikisinde de çalışmasının nedeni budur.)
- `DateTime`'ı **UTC** saklayın. `ModuleDbContext` dönüştürmeyi zaten yapar. Filtrelediğiniz ya da sıraladığınız değerleri UTC `DateTime` (ya da tick) olarak saklayın.
- Her veri erişimi değişikliği, `TestDatabase` ile iki sağlayıcıda da test edilir.
- SQLite `synchronous=FULL` ile çalışır (`SqliteDurabilityInterceptor`): elektrik kesintisi, ekranın "kaydedildi" dediği bir kaydı kaybettirmemelidir.

## Açılışta ne olur?

Sunucu başlarken `ModuleDatabaseInitializer` çalışır:

1. İki modülün aynı tablo önekini kullanmadığını denetler.
2. Platform ve her modül için bekleyen geçişleri belirler.
3. Veritabanında zaten veri varsa ve bekleyen bir şey varsa önce **yedek** alır (yalnızca SQLite): veri klasöründeki `backups/` içinde `vyxum-<tarih>-<saat>-migration.db` adlı bir kopya; en yeni 10 tanesi saklanır. SQL Server'ı Vyxum yedeklemez.
4. Önce platformun, sonra her modülün geçişlerini uygular ve her modülü ve sürümünü platformun kurulu modüller tablosuna yazar.

İki özel durum: geçişlerden önce yapılmış bir veritabanında ilk geçişin tabloları vardır ama geçmiş satırı yoktur; başlatıcı ilk geçişi uygulanmış sayar (*baseline*) ve yalnızca sonrakileri çalıştırır. Etkin sağlayıcı için **hiç** geçişi olmayan modül (örneğin bir test modülü) için tablolar bir kez oluşturulur ve yükseltilemez.

## Geçiş ekleme

Geçişler **üretilir**, elle yazılmaz. Her projede sağlayıcı başına bir tane olmak üzere iki klasörde durur: `Migrations/Sqlite` ve `Migrations/SqlServer`.

1. Modeli değiştirin (varlık ya da `ConfigureModel`).
2. Aracı bir kez geri yükleyin: `dotnet tool restore`.
3. Depo kökünden, geçişi **iki sağlayıcı için aynı adla** oluşturun (örnek: Products):

```bash
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/Sqlite -- --provider Sqlite
dotnet ef migrations add AddProductOrigin --project src/Modules/Products/Vyxum.Modules.Products --output-dir Migrations/SqlServer -- --provider SqlServer
```

Weighing için `src/Modules/Weighing/Vyxum.Modules.Weighing`, platform için `src/Platform/Vyxum.Platform` kullanın. Sonuncuyu geri almak için aynı `--project` ve `-- --provider` değerleriyle `dotnet ef migrations remove` çalıştırın.

4. Üretilen kodu okuyun. Beklediğinizi yaptığından emin olun; özellikle sütun yeniden adlandırmada (EF sil+ekle üretebilir, bu veri kaybettirir; geçişi elle `RenameColumn` kullanacak şekilde düzeltin).
5. Testleri çalıştırın. Model ile geçişler iki sağlayıcıdan birinde uyuşmuyorsa `Every_model_change_has_a_migration` (`tests/Vyxum.Modules.Tests/MigrationTests.cs` ve platform testlerinde) başarısız olur.
6. Veri silen ya da dönüştüren bir geçiş, veritabanında kayıtlar varken çalıştırılan bir test ister.

**Yeni bir bağlamın ilk geçişi:** EF iki model anlık görüntüsüne de `<Bağlam>ModelSnapshot` adını verir. İlk SQLite geçişinden sonra bu sınıfı ve dosyasını `<Bağlam>SqliteModelSnapshot`, ilk SQL Server geçişinden sonra `<Bağlam>SqlServerModelSnapshot` olarak yeniden adlandırın. Sonraki geçişler ek iş gerektirmez. (`ProviderMigrations` iki kümeyi böyle ayırır; yorumlarına bakın.)

**Yayınlanmış bir geçişi asla düzenlemeyin ya da silmeyin.** Müşterilerin veritabanları onu zaten içerir. Yerine yeni bir geçiş ekleyin.

## `dotnet ef` sağlayıcıyı nasıl bulur?

`DesignTimeDbContextFactory<TContext>` (Abstractions içinde), `--`'den sonra verdiğiniz `--provider` argümanını okur. Bu yüzden her modüle tek satırlık fabrika sınıfı yeter.

## Pratikte yedekler ve yükseltmeler

- Kurulum dosyası da yükseltmeden önce SQLite veritabanını kopyalar ([8. bölüm](08-surum.md)).
- SQL Server için müşteriye yükseltmeden önce yedek almasını söyleyin.
- SQLite'ta geri yüklemek için: "Vyxum Web" servisini durdurun, bir yedeği `vyxum.db` üzerine kopyalayın, servisi başlatın.
