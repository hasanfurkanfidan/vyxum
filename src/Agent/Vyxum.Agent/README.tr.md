# Vyxum Agent

[English](README.md)

Vyxum Agent, terazinin bağlı olduğu bilgisayarda çalışır. Teraziyi seri porttan ya da ağdan okur ve canlı ağırlığı
SignalR ile Vyxum web uygulamasına gönderir. Web uygulamasına kendisi bağlanır; hem teraziyle hem web uygulamasıyla
bağlantı koparsa kendiliğinden yeniden bağlanır.

## Cihazlar nereden gelir

Cihazlar web uygulamasında, Ayarlar → Cihazlar altında ayarlanır ve onun veritabanında saklanır. Agent bağlanınca
cihazlarını web uygulamasından ister; orada her değişiklikte yeniden ister. Sonra kendini yeniden başlatmadan yeni
cihazları başlatır, silinenleri durdurur, değişenleri yeniden kurar. Değişmeyen cihazlar çalışmaya devam eder. Agent'ın
kullanamadığı ayarlara sahip bir cihaz çalışmaz; `Error` durumu ve nedeniyle gösterilir.

Bilgisayar adı verilmemiş bir cihaza, cihazlarını ilk isteyen Agent bakar; tek bilgisayarlı kurulumda bu tek Agent'tır.
Bilgisayar adı (`agentName`) verilmiş bir cihaza `AgentName` değeri o ad olan Agent bakar.

Agent'ın kendi ayar dosyasındaki `Devices` listesi geliştirme ve web ayarı kullanılmayan düzenler içindir: listede
cihaz varsa Agent onları kullanır, web uygulamasında ayarlanan cihazları yok sayar. Kurulan Agent boş listeyle gelir.

## Konsol uygulaması olarak çalıştırma

`dotnet run` Agent'ı Development ortamında başlatır (bkz. `Properties/launchSettings.json`); bu ortamda
`appsettings.Development.json` benzetilmiş bir MT-SICS terazisi (572 g) ekler, böylece donanım olmadan çalışır:

```
dotnet run --project src/Agent/Vyxum.Agent
```

`appsettings.json` yerine başka bir ayar dosyası kullanmak için `--settings` verilir:

```
dotnet run --project src/Agent/Vyxum.Agent -- --settings appsettings.Ind231.example.json
```

Her ayar komut satırından da verilebilir, örneğin `--Vyxum:Agent:ServerUrl http://localhost:8080`.

## Windows servisi olarak kurma

Sonraki bir sürümün kurulum dosyası bunu kendisi yapar. O zamana kadar Agent yayımlanır ve yönetici olarak açılmış
bir komut isteminden kaydedilir:

```
dotnet publish src/Agent/Vyxum.Agent -c Release -o C:\Vyxum\Agent
sc.exe create "Vyxum Agent" binPath= "C:\Vyxum\Agent\Vyxum.Agent.exe" start= auto
sc.exe start "Vyxum Agent"
```

`binPath=` ve `start=` sonrasındaki boşluk zorunludur. Servis `appsettings.json` dosyasını kendi klasöründen okur ve
günlüğünü Windows olay günlüğüne yazar. Kaldırmak için: `sc.exe stop "Vyxum Agent"` ve `sc.exe delete "Vyxum Agent"`.

## Ayarlar

Tüm ayarlar `appsettings.json` içindeki `Vyxum:Agent` bölümündedir. Hatalı bir ayar Agent'ı açılışta durdurur ve
hangi ayarın hatalı olduğunu yazar.

| Ayar | Anlamı | Varsayılan |
|---|---|---|
| `ServerUrl` | Vyxum web uygulamasının adresi | `http://localhost:5000` |
| `Key` | Agent anahtarı; yalnızca web uygulamasında anahtar ayarlıysa gerekir (`Vyxum:Agents:Key`). Günlüğe yazılmaz. | boş |
| `AgentName` | Web uygulamasında görünen ad | bilgisayar adı |
| `HeartbeatInterval` | Değişmese de son okuma bu aralıkla yeniden gönderilir | `00:00:01` |
| `ReconnectDelay` | Teraziye ya da sunucuya yeniden bağlanmadan önceki ilk bekleme; her başarısızlıkta iki katına çıkar | `00:00:01` |
| `MaxReconnectDelay` | Denemeler arasındaki en uzun bekleme | `00:00:30` |
| `Devices` | Yerel cihaz listesi, aşağıya bakın. Boş değilse web uygulamasındaki cihazlar yok sayılır. | boş |

Her cihazın bir `Id`, bir `Transport` (taşıma) ve bir `Protocol` ayarı vardır. Her taşıma her protokolle çalışır.
Web uygulamasındaki cihaz ayarlarında aynı `transport` ve `protocol` alanları (küçük harfle başlayarak) bulunur.

- `Transport.Type`:
  - `serial`: `PortName` (zorunlu, örneğin `COM3`), `BaudRate` (9600), `DataBits` (8), `Parity` (`None`, `Odd`,
    `Even`, `Mark`, `Space`), `StopBits` (`One`, `OnePointFive`, `Two`), `Handshake` (`None`, `XOnXOff`,
    `RequestToSend`, `RequestToSendXOnXOff`), `DtrEnable` (true), `RtsEnable` (true).
  - `tcp`: `Host` ve `Port` (zorunlu), `ConnectTimeout` (5 sn), `KeepAlive` (kapalı).
  - `simulator`: `Simulator` altında `Kind` (`MtSics`, `ToledoContinuous`, `Replay`), `Weight`, `Unit`, `State`,
    `Interval`, `RecordingPath` (kayıttan oynatma).
- `Protocol.Type`:
  - `mt-sics`: `Command` (`SI`, `S` ya da `SIR`), `PollInterval` (200 ms), `ResponseTimeout` (1 sn), `ZeroTimeout` (3 sn),
    `RetriesOnError`, `MaxConsecutiveTimeouts`, `Terminator`.
  - `toledo-continuous`: `ChecksumEnabled`, `InactivityTimeout` (3 sn), `MaxConsecutiveInvalidFrames`.
  - `generic-ascii`: `Pattern` (zorunlu, `weight` grubu olan bir düzenli ifade), `StablePattern`, `MotionPattern`,
    `OverloadPattern`, `UnderloadPattern`, `NetPattern`, `DecimalSeparator`, `ThousandsSeparator`, `DefaultUnit`,
    `Terminator`, `RequestCommand` (sorgu modu), `PollInterval`, `ResponseTimeout`, `InactivityTimeout`.

`appsettings.Ind231.example.json`, COM3'e bağlı bir METTLER TOLEDO IND231 örneğidir (9600 8N1, MT-SICS, `SI` komutu).

## Güvenlik: okuma komutları ve istek üzerine sıfırlama

Agent ağırlığı okur. MT-SICS ile ayarlanan okuma komutunu (`S`, `SI` ya da `SIR`) gönderir; yalnızca bir kullanıcı web
uygulamasında "Sıfırla" tuşuna bastığında, o basış için tam bir kez `Z` gönderir. Başka hiçbir komut gönderilmez: dara
(`T`, `TI`, `TA`), anında sıfırlama (`ZI`), açılış durumuna dönüş (`@`) ve ayar komutları kodda engellenir; `Command`
yalnızca okuma komutlarını kabul eder (başka bir değer açılışta reddedilir). `Z`, aynı bağlantıda iki okuma komutunun
arasında gönderilir. Terazi `ES` ya da `ET` (yürütülmedi) yanıtı verirse komut, diğer komutlar gibi yeniden gönderilir;
`ZeroTimeout` içinde yanıt gelmezse yeniden gönderilmez, çünkü terazi sıfırlamış olabilir. Agent kaydolurken hangi
cihazların sıfırlamayı desteklediğini bildirir. Toledo Continuous yalnızca dinler ve sıfırlamayı desteklemez;
`generic-ascii` de desteklemez.
`generic-ascii` içinde `RequestCommand` serbest metindir, çünkü her marka kendi komutunu kullanır: buraya yalnızca
ağırlığı okuyan ya da yazdıran bir komut yazılır; dara alan, sıfırlayan ya da cihazı değiştiren bir komut asla yazılmaz.
