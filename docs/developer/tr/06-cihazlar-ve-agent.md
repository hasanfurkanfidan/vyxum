# 6. Cihazlar ve Agent

[Dizin](../README.md#türkçe) · Önceki: [5. Veritabanı](05-veritabani.md) · Sonraki: [7. Web arayüzü](07-web-arayuzu.md)

## İki parçalı fikir

Bir terazi okumanın iki bağımsız sorunu vardır:

1. **Taşıma (transport):** baytları taşımak (9600 baud 8N1 seri port ya da TCP soketi). Arayüz `ITransport`: `OpenAsync`, `ReadAsync`, `WriteAsync`, `CloseAsync`, `DiscardInput`.
2. **Protokol:** baytları anlamak (MT-SICS `S S     0.572 kg` gönderir; Toledo Continuous STX ile başlayan çerçeveler gönderir). Arayüz `IScaleProtocol`: `IAsyncEnumerable<WeightReading> ReadAsync(ITransport, CancellationToken)`.

Her taşıma her protokolle çalışır. Bir protokol seri portla mı simülatörle mi konuştuğunu bilmez; simülatörün testlerde kullanılabilmesinin nedeni tam olarak budur.

`WeightReading` markadan bağımsızdır: değer (cihazın gönderdiği haliyle), birim, durum (`Stable`, `Motion`, `Overload`, `Underload`), isteğe bağlı dara, brüt/net işareti, ham metin ve zaman damgası.

`DeviceRunner` bir cihazı hayatta tutar: taşımayı açar, protokolü çalıştırır, okumaları sınırlı bir kanala koyar (kimse okumazsa en eskisi atılır) ve **her** hatada kapatıp artan beklemeyle yeniden açar. Bir saat çıkarılmış cihaz kendiliğinden geri gelmelidir.

`FrameReader`, bayt akışını çerçevelere bölmesi gereken protokollere yardım eder (satır sonu ya da isteğe bağlı sağlama baytlı STX...CR).

## Güvenlik kuralı (iki kez okuyun)

Dükkândaki terazi yasal olarak denetlenen bir ölçü aleti olabilir. Yanlış bir komut onu daraya alabilir, sıfırlayabilir ya da yeniden yapılandırabilir. Bu yüzden:

- Sürücüler **yalnızca ağırlığı okur**.
- MT-SICS yalnızca `S`, `SI`, `SIR` gönderir; kullanıcı açıkça "Sıfırla"ya bastığında basış başına **bir kez** `Z` gönderir. Dara (`T`, `TI`, `TA`), anında sıfırlama (`ZI`), reset (`@`) ve ayar komutları kodda engellenmiştir.
- Toledo Continuous yalnızca dinler. Genel ASCII yalnızca kullanıcının yapılandırdığı istek komutunu gönderir; bu komut yalnızca ağırlığı okumalıdır.
- Keşif tam olarak iki komut gönderir: `SI` ve `I4` (her biri CR LF ile). Her yazma, başka her yazmayı porta tek bayt ulaşmadan reddeden bir beyaz listeden (`WriteGuardTransport`) geçer ve bir test bunu denetler.

Cihazı değiştiren bir komutu asla eklemeyin. Bir müşteri isterse durun ve konuşun.

## Teraziden ekrana

```
Cihaz ─bayt─> ITransport ─> IScaleProtocol ─> WeightReading
   (Vyxum.Devices* içinde, Agent'ın içinde)
        │ DeviceRunner / DeviceFleet
        ▼
   HubWorker ──SignalR /hubs/agent──> AgentHub ──> LiveWeightStore (bellek)
                                                       │ ILiveWeightSource
                       ┌───────────────────────────────┴───────────────┐
                 Weighing modülü                         /api/devices/{id}/live
              (o anki ağırlığı okur)                     (Server-Sent Events → tarayıcı)
```

Önemli noktalar:

- Canlı ağırlıklar **yalnızca bellekte** tutulur ve entegrasyon olayı değildir (saniyede birkaç kez değişirler; saklamak israf olur). Platform bunları `ILiveWeightSource` (Abstractions içinde) ile sunar; böylece bir modül hiçbir marka ya da Agent'ı bilmeden ağırlık alır.
- Agent ile sunucu arasındaki kablo sözleşmesi `AgentHubContract`'tır (hub yolu `/hubs/agent`, anahtar başlığı `X-Vyxum-Agent-Key`). Agent→sunucu metotları: `Register`, `ReportReading`, `ReportStatus`, `GetDevices`, `ReportDiscoveryProgress`. Sunucu→agent çağrıları: `Zero`, `Discover`, `DevicesChanged`. Agent platforma başvuramaz (mimari kural); bu yüzden sözleşmenin **iki kopyası** vardır: `src/Platform/Vyxum.Platform/Devices/AgentHubContract.cs` ve `src/Agent/Vyxum.Agent/Contract/AgentHubContract.cs`. `tests/Vyxum.IntegrationTests/AgentContractTests.cs` bunları eş tutar: birini değiştirirseniz ötekini de değiştirin ve o testi çalıştırın.
- Agent anahtarı (sunucuda `Vyxum:Agents:Key`, agent'ta `Vyxum:Agent:Key`), **başka** bir bilgisayardaki agent'ın bağlanmasını sağlayan şeydir: anahtar yoksa sunucu yalnızca yerel agent'ları kabul eder (`AgentConnectionGate`). Anahtar yalnızca başlıkta kabul edilir, URL'de asla, ve günlüğe yazılmaz.

## Cihaz ayarları nerede durur?

Cihazlar web arayüzünde (Ayarlar → Cihazlar) yapılandırılır ve platform tablosu `plt_Devices` içinde saklanır (`DeviceConfigService`). Sunucu `transport` ve `protocol`'ü JSON nesneleri olarak saklar ve yalnızca bir `type` olduğunu denetler. Protokol adlarını **bilmez**; bu yüzden protokol eklemek platformu hiç değiştirmez. Bir agent bağlanınca `GetDevices` çağırır; ayarlar değişince sunucu `DevicesChanged` gönderir ve agent'ın `DeviceFleet`'i yalnızca değişen cihazları başlatır, durdurur ya da yeniden başlatır. Ayarları agent'ın kullanamadığı cihaz, nedeniyle birlikte `Error` durumunda görünür.

Geliştirme için agent'ın kendi `appsettings*.json` dosyasındaki `Devices` listesi her şeyin yerine geçer. Ayar tablosu için [Agent README](../../../src/Agent/Vyxum.Agent/README.tr.md) dosyasına bakın.

## Gerçek terazi deneme

[README](../../../README.tr.md) içindeki gerçek terazi bölümüne bakın (COM3 üzerinde IND231, `--settings appsettings.Ind231.example.json`).

## Yeni protokol ekleme (adım adım)

"Acme" markasının terazisinin `ST,GS,  1.250kg` satırları bastığını düşünelim.

1. **Proje.** `src/Devices/Vyxum.Devices.Acme/Vyxum.Devices.Acme.csproj` oluşturun; **yalnızca** `Vyxum.Devices`'a başvursun (`Vyxum.Devices.MtSics.csproj`'u kopyalayın). `Vyxum.slnx` içine ekleyin. `Protocol_assemblies_reference_only_vyxum_devices` mimari testi bunu korur; derlemenizi listesine ekleyin.
2. **Ayrıştırıcı saf fonksiyon olarak.** `AcmeParser.Parse(string line)` bir değer ya da "geçersiz" döndürür. G/Ç yok, saat yok. Hatalar burada çıkar; test edilmesini kolay yapın.
3. **Protokol sınıfı** `AcmeProtocol : IScaleProtocol`. `ReadAsync` içinde satırları alın (`FrameReader` ya da kendi döngünüz), ayrıştırıcıyı çağırın ve `WeightReading` `yield return` edin. Bağlantı öldüğünde (çok uzun sessizlik, çok fazla geçersiz çerçeve) `DeviceConnectionLostException` fırlatın ki `DeviceRunner` yeniden bağlansın. Testler saati yönetsin diye `TimeProvider` geçirin. Zaman aşımları için bir options kaydı tutun. Model olarak `ToledoContinuousProtocol` (yalnızca dinler) ve `MtSicsProtocol` (istek/yanıt) sınıflarına bakın.
4. `tests/Vyxum.Devices.Tests/Acme/` içinde **testler**. Test verisi olarak **gerçek bir teraziden kaydedilmiş ham satırlar** koyun. Yalnızca uydurma satırlarla test edilmiş bir ayrıştırıcı ilk gerçek satırda başarısız olur.
5. **Simülatör senaryosu.** `Vyxum.Devices.Simulator` içine bir `ISimulatedDevice` (yalnızca bayt konuşur, protokol sınıfınızı bilmez) ekleyin, bir `SimulatorKind` değeri ve `SimulatorFactory` içinde bir case ekleyin. `Replay` türü için kayıtlı bir dosya `Recordings/` içine konabilir. Böylece protokol donanımsız uçtan uca test edilebilir.
6. **Tür adını Agent'a kaydedin.** `src/Agent/Vyxum.Agent/Configuration/AgentConfiguration.cs` içinde `ProtocolTypes` listesine `"acme"` ekleyin ve `CreateProtocol` içine ayarları doğrulayıp protokolü kuran bir `case "acme":` yazın. Yeni ayar alanlarını `AgentSettings.cs` içindeki `ProtocolSettings`'e ekleyin. Ayar hataları ayarın adını söylemelidir (mevcut case'lere bakın). Türü Agent README'sinin ayar tablosuna ekleyin (İngilizce ve Türkçe).
7. **Web arayüzü.** Cihaz ekranları sundukları protokol türlerini bilir. `src/Web/vyxum-web/src` içinde `mt-sics` arayın (`components/devices/ConnectionFields.tsx` ve `lib/devices.ts` içinde geçer) ve kendinizinkini ekleyin; metinleri kaynak dosyalarına koyun ([7. bölüm](07-web-arayuzu.md)).
8. **İsteğe bağlı:** sıfırlama desteği (`ISupportsZero`, bir kez gönderilir, okuma döngüsüyle sıraya sokulur) ve keşif desteği: paketinizde `IPassiveProbe` (cihazın kendiliğinden gönderdiğinden protokolü tanır) ya da `IActiveProbe` (izinli her yazmayı `AllowedWrites` içinde listeler; yalnızca salt okunur komutlar) uygulayın ve `PortScannerOptions` içindeki listelere ekleyin.
9. **Belge:** iki README'deki Cihazlar tablosunu güncelleyin.

## Yeni taşıma ekleme

Aynı fikir: `Vyxum.Devices.Transports` içinde `ITransport` uygulayın (yalnızca `Vyxum.Devices`'a başvurur), tür adını `TransportTypes` listesine ve `AgentConfiguration` içindeki taşıma bölümüne bir case olarak ekleyin, ayrıca sunucunun `DeviceConfigService.ValidateTransport` denetimine ekleyin. Taşımalar baytları yorumlamamalıdır.
