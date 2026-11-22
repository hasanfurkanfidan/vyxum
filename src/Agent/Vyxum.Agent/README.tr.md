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
| `Key` | Agent anahtarı; web uygulamasında anahtar ayarlıysa gerekir (`Vyxum:Agents:Key`). Kurulum, kurduğu bilgisayarda ikisine de aynı rastgele anahtarı yazar; başka bilgisayardaki bir agent için anahtarı oradaki `vyxum.json` dosyasından kopyalayın. Günlüğe yazılmaz. | boş (kurulum yazar) |
| `AgentName` | Web uygulamasında görünen ad | bilgisayar adı |
| `HeartbeatInterval` | Değişmese de son okuma bu aralıkla yeniden gönderilir | `00:00:01` |
| `ReconnectDelay` | Teraziye ya da sunucuya yeniden bağlanmadan önceki ilk bekleme; her başarısızlıkta iki katına çıkar | `00:00:01` |
| `MaxReconnectDelay` | Denemeler arasındaki en uzun bekleme | `00:00:30` |
| `Devices` | Yerel cihaz listesi, aşağıya bakın. Boş değilse web uygulamasındaki cihazlar yok sayılır. | boş |
| `Printing` | Bu bilgisayardaki etiket yazıcıları, aşağıda "Yazdırma" bölümüne bakın | açık, TCP port 9100 |

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
`generic-ascii` içinde `RequestCommand` şu okuma ya da yazdırma komutlarından biri olmalıdır: `P`, `W`, `S`, `SI`, `Q`,
`IP`, `READ`, ENQ (`\u0005`) veya ESC P (`\u001bP`). Başka bir değeri agent açılışta, web uygulaması da cihaz kaydedilirken
reddeder; böylece dara alan, sıfırlayan ya da cihazı değiştiren bir komut ayarlanamaz.

## Cihaz arama

Web uygulaması Agent'tan bağlı terazileri bulmasını isteyebilir (Kurulum sihirbazı → Terazi, Ayarlar → Cihazlar → Ara).
Agent, çalışan cihazlarından hiçbirinin kullanmadığı her seri portu tarar; çalışan bir cihazın kullandığı port o cihazın
kimliğiyle `InUse` (kullanımda) olarak bildirilir ve hiç açılmaz. Portlar aynı anda taranır; sıradan bir bilgisayarda
arama birkaç saniyede biter, her port en fazla yaklaşık 25 saniye sürer.

Her portta şu ayarlar sırayla denenir, ilk eşleşmede o port biter: 9600, 19200, 4800, 2400, 38400 ve 115200 baud, her
biri 8N1, 7E1 ve 7O1 ile. Port her ayar için açılır ve yeniden kapatılır. Her ayarda:

1. Agent hiçbir şey göndermeden yaklaşık 0,7 saniye dinler. Toledo Continuous çerçeveleri ya da tekrar eden düz ASCII
   ağırlık satırları (bir sayı ve isteğe bağlı birim) burada tanınır. 7E1 ve 7O1 ile yalnızca aynı baud hızında 8N1
   bir şey aldıysa dinler.
2. MT-SICS `SI` komutunu (ağırlığı bir kez oku) gönderir. Yanıt `ES`, `ET`, meşgul ya da bozuk olursa (8N1 ile 0,5 saniye
   içinde yanıt gelmezse de) `SI`'yi bir kez daha gönderir. Geçerli bir ağırlık yanıtından sonra `I4` (seri numarası)
   gönderir.

**Arama sırasında yalnızca bu iki komut gönderilir: `SI` ve `I4`, ikisi de CR LF ile.** Her yazma, başka her şeyi daha
porta tek bayt ulaşmadan reddeden bir izin listesinden geçer; bunu bir test denetler. Dara (`T`), sıfırlama (`Z`),
açılış durumuna dönüş (`@`) ve ayar komutları asla gönderilmez (V-28 ve V-40 kararları).

Ağdaki bir terazi için IP adresi ve port girilir: Agent yalnızca o adresi aynı yolla dener (dinle, sonra `SI` ve `I4`).
Ağ taraması yapılmaz.

Bulunan cihaz, yukarıdaki cihaz ayarlarıyla aynı biçimde taşıma ve protokol ayarları, örnek bir okuma ve seri numarasıyla
döner; olduğu gibi kaydedilebilir.

## Yazdırma

Web uygulaması, bu bilgisayara ağdan ya da seri porttan bağlı yazıcılara baskı işi (örneğin etiket) gönderebilir.
Agent işin içine bakmaz: bağlantıyı açar, baytları bir kez yazar, iş isterse yazıcının cevabını okur (durum sorgusu) ve
bağlantıyı kapatır. Agent bir işi asla yeniden göndermez; web uygulamasına cevap ulaşmazsa iş "belirsiz" olarak
bildirilir ve sorulmadan yeniden basılmaz. Aynı anda tek iş çalışır.

| Ayar (`Printing:`) | Anlamı | Varsayılan |
|---|---|---|
| `Enabled` | Agent baskı işi alır mı | `true` |
| `AllowedTcpPorts` | Ağ yazıcısının kullanabileceği TCP portları. Başka porta giden iş reddedilir; böylece Agent başka hizmetlere veri göndermek için kullanılamaz. | `[9100]` |
| `ConnectTimeout` | Ağ yazıcısına bağlanmanın en uzun süresi | `00:00:05` |

Agent'ın bir terazisinin kullandığı seri port baskı için asla açılmaz. Seri yazıcılar işte verilen hızda 8 veri biti,
eşliksiz ve bir dur biti ile kullanılır. USB yazıcılar henüz desteklenmiyor.
