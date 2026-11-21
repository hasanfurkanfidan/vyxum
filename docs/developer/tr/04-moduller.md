# 4. Modüller ve olaylar

[Dizin](../README.md#türkçe) · Önceki: [3. Depo haritası](03-depo-haritasi.md) · Sonraki: [5. Veritabanı ve geçişler](05-veritabani.md)

## Bir modül nasıl yüklenir?

1. Sunucu `AddVyxumPlatform(configuration, HostModules.Load(configuration))` çağırır.
2. `HostModules.BuiltIn` (`src/Host/Vyxum.Host/HostModules.cs`) her zaman yüklenen derlemeleri listeler. Ek derlemeler, `Vyxum:Modules` yapılandırma listesiyle (örneğin `Vyxum:Modules:0`) adlarıyla eklenebilir. Bu depo dışındaki modüller böyle yüklenir.
3. `ModuleDiscovery`, bu derlemelerde `IModule` uygulayan her genel sınıfı bulur ve oluşturur (genel, parametresiz bir kurucu gerekir).
4. Her modülün `ConfigureServices` metodu çalışır. Yalnızca bir şeyleri *kaydeder*; henüz hiçbir şey çözülmez.
5. `ModuleValidator` **tüm** modülleri birlikte denetler; bir sorun varsa bütün sorunları listeleyen tek bir `ModuleValidationException` fırlatır ve sunucu başlamaz. Denetlenenler: modül ve yetenek kimlikleri küçük harfli, noktayla ayrılmış kebab-case olmalı; kimlikler benzersiz olmalı; her yeteneğin tam bir sağlayıcısı olmalı; her `Requires` yeteneği bulunmalı; bağımlılık döngüsü olmamalı; olay işleyicisi olan modülün DbContext'i olmalı; bir olay adı+sürümü tek modülce yayınlanmalı; her olay türünde `[IntegrationEvent]` olmalı; bir DbContext tek modüle ait olmalı.
6. Açılışta bir arka plan servisi veritabanını oluşturur ya da yükseltir ([5. bölüm](05-veritabani.md)), bir başkası olayları iletir.
7. `MapVyxumModules` her modülün `MapEndpoints` metodunu çağırır.

## Manifest

`IModule.Manifest` modülü tanımlar:

```csharp
Manifest = new()
{
    Id = "weighing",                              // küçük harf kebab, nokta olabilir
    Version = new Version(1, 0),
    Provides = ["weighing.records"],              // sunulan yetenekler (her birinin tek sağlayıcısı)
    Requires = [],                                // olmak zorunda, yoksa açılış başarısız
    Optional = ["products.catalog", "labels.print"], // varsa kullanılır
    Publishes = [typeof(WeighingRecorded)],       // yayınladığı olaylar
}
```

**Requires mı Optional mı?** 4. kural hiçbir modülün zorunlu olmadığını söyler. `Optional`'ı tercih edin ve çalışma zamanında `ICapabilityRegistry.IsAvailable("labels.print")` ile sorun; özelliği gösterin ya da gizleyin. Weighing bu sayede Products ve etiket modülü olmadan da çalışır. Arayüz de nelerin yüklü olduğunu `GET /api/ui/modules` ile öğrenir; menü girdileri yalnızca yüklü modüller için görünür.

## Çağrı yerine olay

Modüller birbirini çağıramaz, ama Weighing'in ürün verisine ihtiyacı var. Çözüm: Products bir ürün her değiştiğinde tam güncel durumu taşıyan `products.product-saved` olayını **yayınlar**; Weighing bunu **işler** ve verinin **kendi kopyasını** kendi tablosunda (`wgh_Products`) tutar. Weighing ayrıca tartım anında ürün kodunu, adını ve fiyatını her kayda yazar; böylece sonradan ürün değişse bile eski kayıtlar değişmez.

Olay, sabit bir kablo adı ve sürümü veren bir öznitelikli düz `record`'dur:

```csharp
[IntegrationEvent("products.product-saved", 1)]
public sealed record ProductSaved(Guid ProductId, string Code, /* ... */) : IIntegrationEvent;
```

Olaylar JSON (System.Text.Json, camelCase) olarak taşınır; bu yüzden tüketen modül aynı ad ve sürümle **kendi kopyasını** tanımlar, yayıncıya başvurmaz. `src/Modules/Weighing/.../ProductSaved.cs` dosyasını `src/Modules/Products/.../ProductSaved.cs` ile yan yana inceleyin. Bir olayı değiştirmek için isteğe bağlı alanlar ekleyin (mevcut olaylar böyle genişletildi) ya da yeni bir sürüm yayınlayın.

### Outbox ve inbox (olaylar neden kaybolmaz)

- **Yayın:** `db.Publish(new ProductSaved(...))` modülün `{önek}_OutboxMessages` tablosuna bir satır ekler. İş verisiyle **aynı** `SaveChanges` içinde, tek işlemde yazılır. Ya ikisi birden olur ya hiçbiri.
- **İletim:** `OutboxDispatcher` (arka plan servisi) her modülün outbox'ını yoklar (varsayılan 0,5 sn, `Vyxum:Outbox:PollingInterval`) ve bekleyen her ileti için kayıtlı işleyicileri çalıştırır.
- **Bir kez işleme:** her işleyici, modülünün DbContext işlemi içinde çalışır. `{önek}_InboxMessages` satırı `(MessageId, HandlerName)` aynı işlemde yazılır. İleti iki kez gelirse (örneğin çökmeden sonra) inbox satırı ikinci çalıştırmayı etkisiz kılar.
- **Hata ve yeniden deneme:** işleyici hata fırlatırsa değişiklikleri geri alınır, `Attempts`/`LastError` yazılır ve ileti artan gecikmeyle yeniden denenir (1 sn, ikiye katlanarak, en fazla 5 dk; `Vyxum:Outbox:Retry*`). Yalnızca başarısız işleyiciler yeniden çalışır.

Aklınızda tutmanız gerekenler: işleyiciler tekrar çalışmaya dayanıklı ve hızlı olmalı; iletim sırası yalnızca yayın zamanına göre en iyi çabadır; veritabanı başına **tek sunucu** çalıştırın (birden fazla dağıtıcı eşgüdümlenmez); iletilmiş iletiler henüz temizlenmez.

## Yeni modül ekleme (kontrol listesi)

Diyelim `Labels` ekliyorsunuz.

1. **Proje.** `src/Modules/Labels/Vyxum.Modules.Labels/Vyxum.Modules.Labels.csproj` oluşturun. Products `.csproj` dosyasını kopyalayın: **yalnızca** `Vyxum.Platform.Abstractions`'a ve `PrivateAssets=all` ile `Microsoft.EntityFrameworkCore.Design`'a başvurur. `Vyxum.slnx` içine ekleyin.
2. **Modül sınıfı** `LabelsModule : IModule`, manifestiyle (kimlik, sürüm, `Provides`, `Publishes`). `ConfigureServices` içinde `context.AddDbContext<LabelsDbContext>()`, servis kayıtları, tükettiğiniz olaylar için `context.AddEventHandler<TEvent, THandler>()` ve `services.AddUiStrings("labels", LabelsStrings.ResourceManager)` çağırın.
3. **DbContext** `LabelsDbContext : ModuleDbContext`, `TablePrefix => "lbl"` (harf ve rakam, harfle başlar, benzersiz) ve `ConfigureModel` ile. Tablo adlarına önek otomatik eklenir. `DateTime`'ı UTC kullanın.
4. **Tasarım zamanı fabrikası:** `internal sealed class LabelsDesignTimeFactory : DesignTimeDbContextFactory<LabelsDbContext>;`
5. **Metinler:** `Resources/Strings.resx` (İngilizce, anahtarları tanımlar) ve `Resources/Strings.tr.resx` (Türkçe, aynı anahtarlar, boş değer yok). Products'taki gibi `ResourceManager` sunan bir `LabelsStrings` sınıfı. Türkçe dosya eksik ya da farklıysa bir test başarısız olur.
6. **Uç noktalar:** `MapEndpoints` içinde `/api/labels` gibi bir grup. Çevrilmiş metin değil, hata kodu/anahtarı döndürün; arayüz çevirir.
7. Her iki sağlayıcı için **geçişler** ([5. bölüm](05-veritabani.md)).
8. **Sunucuya kaydedin:** `Vyxum.Host.csproj` içinde bir `ProjectReference` ve `HostModules.BuiltIn` içinde bir satır. (Bu depo dışında üretilen modüller bunun yerine `Vyxum:Modules` ile yüklenir.)
9. **Mimari testler:** bağımlılık kuralları kapsasın diye modül derlemesini `tests/Vyxum.ArchitectureTests/ArchitectureTests.cs` içindeki listeye ekleyin. Test edebilmek için test projelerinin de ona başvurması gerekir.
10. **Testler:** `tests/Vyxum.Modules.Tests/ProductsApiTests.cs` tarzında bir API testi (SQLite ve SQL Server'da çalışsın diye `TestDatabase` kullanın) ve isteğe bağlı yetenekleri yokken modülün çalıştığını gösteren bir test.
11. **Belge:** modülün uç noktalarını ve ayarlarını README'de (İngilizce ve Türkçe) anlatın.

Arayüzde ekran gerekiyorsa [7. bölüme](07-web-arayuzu.md) bakın.

## Modül ayarları

Bir modül ayarlarını `Vyxum:<Ad>` yapılandırma bölümünden, options deseniyle okur ve `WeighingOptions` gibi **açılışta doğrular** (`ValidateOnStart`). İş kuralları koda gömülmez: yuvarlama, para birimi, "kararlı ağırlık zorunlu" gibi şeyler varsayılanı olan ayarlardır.
