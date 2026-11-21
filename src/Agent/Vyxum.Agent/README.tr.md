# Vyxum Agent

[English](README.md)

Vyxum Agent, terazinin bağlı olduğu bilgisayarda çalışır. Teraziyi seri porttan ya da ağdan okur ve canlı ağırlığı
SignalR ile Vyxum web uygulamasına gönderir. Web uygulamasına kendisi bağlanır; hem teraziyle hem web uygulamasıyla
bağlantı koparsa kendiliğinden yeniden bağlanır.

## Konsol uygulaması olarak çalıştırma

Gelen `appsettings.json` ile Agent benzetilmiş bir MT-SICS terazisini (572 g) okur; donanım olmadan çalışır:

```
dotnet run --project src/Agent/Vyxum.Agent
```

`appsettings.json` yerine başka bir ayar dosyası kullanmak için `--settings` verilir:

```
dotnet run --project src/Agent/Vyxum.Agent -- --settings src/Agent/Vyxum.Agent/appsettings.Ind231.example.json
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
| `Devices` | Cihaz listesi, aşağıya bakın | |

Her cihazın bir `Id`, bir `Transport` (taşıma) ve bir `Protocol` ayarı vardır. Her taşıma her protokolle çalışır.

- `Transport.Type`:
  - `serial`: `PortName` (zorunlu, örneğin `COM3`), `BaudRate` (9600), `DataBits` (8), `Parity` (`None`, `Odd`,
    `Even`, `Mark`, `Space`), `StopBits` (`One`, `OnePointFive`, `Two`), `Handshake` (`None`, `XOnXOff`,
    `RequestToSend`, `RequestToSendXOnXOff`), `DtrEnable` (true), `RtsEnable` (true).
  - `tcp`: `Host` ve `Port` (zorunlu), `ConnectTimeout` (5 sn), `KeepAlive` (kapalı).
  - `simulator`: `Simulator` altında `Kind` (`MtSics`, `ToledoContinuous`, `Replay`), `Weight`, `Unit`, `State`,
    `Interval`, `RecordingPath` (kayıttan oynatma).
- `Protocol.Type`:
  - `mt-sics`: `Command` (`SI`, `S` ya da `SIR`), `PollInterval` (200 ms), `ResponseTimeout` (1 sn), `RetriesOnError`,
    `MaxConsecutiveTimeouts`, `Terminator`.
  - `toledo-continuous`: `ChecksumEnabled`, `InactivityTimeout` (3 sn), `MaxConsecutiveInvalidFrames`.
  - `generic-ascii`: `Pattern` (zorunlu, `weight` grubu olan bir düzenli ifade), `StablePattern`, `MotionPattern`,
    `OverloadPattern`, `UnderloadPattern`, `NetPattern`, `DecimalSeparator`, `ThousandsSeparator`, `DefaultUnit`,
    `Terminator`, `RequestCommand` (sorgu modu), `PollInterval`, `ResponseTimeout`, `InactivityTimeout`.

`appsettings.Ind231.example.json`, COM3'e bağlı bir METTLER TOLEDO IND231 örneğidir (9600 8N1, MT-SICS, `SI` komutu).

## Güvenlik: yalnızca okuma komutları

Agent yalnızca ağırlığı okur. MT-SICS ile yalnızca `S`, `SI` ya da `SIR` gönderir; başka her komut (dara `T`, sıfırlama
`Z`, açılış durumuna dönüş `@`, ayar komutları) açılışta reddedilir. Toledo Continuous yalnızca dinler.
`generic-ascii` içinde `RequestCommand` serbest metindir, çünkü her marka kendi komutunu kullanır: buraya yalnızca
ağırlığı okuyan ya da yazdıran bir komut yazılır; dara alan, sıfırlayan ya da cihazı değiştiren bir komut asla yazılmaz.
