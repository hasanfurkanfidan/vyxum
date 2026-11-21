# 10. Kararlar ve gerekçeleri

[Dizin](../README.md#türkçe) · Önceki: [9. Sorun giderme](09-sorun-giderme.md)

Kod yorumları "decision V-29" gibi numaralı kararlara gönderme yapar. Bu bölüm depodan okunabilen kararları gerekçeleriyle toplar. Özgün karar defteri bu deponun parçası değildir; gelecekteki geliştiricilerin bilmesi gereken yeni bir karar verdiğinizde **buraya ekleyin** (numarası varsa koruyun, yoksa kısa bir ad verin).

## Ürün ve mimari

| Karar | Neden |
|---|---|
| **Müşterinin kendi bilgisayarında, çevrimdışı çalışır** (V-17) | Birçok müşterinin interneti zayıf ya da yok ve verisini kendi makinesinde istiyor. Bu yüzden CDN, uzak yazı tipi, dış çağrı yok; kurulum yardımsız yapılabilmeli. |
| **Modüler monolit** | Tek program kurması ve işletmesi kolay; modüller özellikleri ayrılabilir ve ayrı satılabilir tutar. Mikroservis kurulumu zorlaştırırdı. |
| **Modüller yalnızca olaylarla konuşur (outbox + inbox)** | Modülleri çıkarılabilir tutar. Outbox yayını iş değişikliğiyle bölünmez yapar; inbox işlemenin bir kez olmasını sağlar. |
| **Hiçbir modül zorunlu değil; yetenekler** | Müşteri modüllerin yalnızca bir kısmına sahip olabilir. Başka modül isteyen özellikler `ICapabilityRegistry`'ye bakar. |
| **Platform hiçbir terazi markasını tanımaz** | Yeni markalar platforma dokunmadan eklenebilmeli; platform yalnızca "bir ağırlık okundu"yu bilir. |
| **Agent ayrı programdır ve platforma başvuramaz** | Terazi sunucudan başka bir bilgisayarda olabilir; donanım kodu yalıtılır, donanım sorunu web sunucusunu düşüremez. |
| **Kod veritabanından bağımsız; varsayılan SQLite, SQL Server destekli** | SQLite kurulum istemez (basit kurulum); SQL Server büyük müşterilere hizmet eder. İki sağlayıcıyı her veri testini ikisinde çalıştırarak dürüst tutarız. |
| **İş kuralları koda değil ayara yazılır** | Müşteriler farklıdır (yuvarlama, para birimi, kararlı ağırlık zorunluluğu). Varsayılanlar options sınıflarındadır. |
| **Saklanan zamanlar UTC** | Sorgulanan ya da sıralanan değerler iki sağlayıcıda aynı karşılaştırılmalı. |
| **Uyarılar hata; merkezi paket sürümleri** | Kaliteyi dengede tutar, yükseltmeler tek yerde olur. |
| **Yalnızca MIT/BSD/Apache-2.0 uyumlu bağımlılıklar** | Çekirdek Apache-2.0'dır ve müşterilere dağıtılır. |

## Tartım ve cihazlar

| Karar | Neden |
|---|---|
| **V-03: ücretsiz çekirdekte giriş ekranı yok** | Ücretsiz sürüm tek istasyonluk bir araçtır; bu yüzden API, ulaşabilen her istemciye açıktır. Sunucuyu güvenilir bir ağda tutun. Kullanıcılar modülü sonraki fazda planlıdır. |
| **V-26: agent kapısı** | Anahtarsız yalnızca aynı bilgisayar agent bağlayabilir; anahtarla her yerdeki agent'lar bağlanabilir. Hiç ayar yapmadan hub'ı varsayılan olarak korur. |
| **V-27: canlı ağırlıklar entegrasyon olayı değildir** | Saniyede birçok kez değişir, geçmişe ihtiyaç duymaz. `ILiveWeightSource` arkasında bellekte kalır. |
| **V-28: sürücüler yalnızca okur; sıfırlama yalnızca açık istekle** | Terazi yasal denetime tabi olabilir; yanlış komut onu değiştirebilir. Tehlikeli komutlar kodda engellenir ve test edilir. |
| **V-29: dara yazılımda tutulur, sıfırlama cihaza gönderilir** | Yazılım darası her protokolde (yalnızca dinleyenler dahil) aynı çalışır. Bilinen sınırlama: operatör göstergenin kendi TARE tuşuna da basarsa dara iki kez düşer. |
| **V-32: ürün verisi Weighing'e olayla gelir** | Weighing kendi kopyasını (`wgh_Products`) tutar ve ürün kodu, adı, fiyatını her kayda yazar; ürün değişince eski kayıtlar asla değişmez. |
| **V-33: arayüz metinleri modülün kaynak dosyalarından gelir** | Her modül metinlerinin iki dildeki sahibidir; arayüz yalnızca anahtar içerir. |
| **V-34: yazı tipleri pakete gömülür** | Çevrimdışı kullanım. Paketler MIT; yazı tipi dosyaları gömmeye izin veren SIL OFL 1.1. |
| **V-35: ürünün hedef ağırlığı ve toleransları şimdiden saklanır** | Raporları (ayrı geliştirilen bir modülde) çekirdeği sonradan değiştirmeden hazırlar. |
| **Tutar = net (kg) × birim fiyat, ayara göre yuvarlanır** | Yuvarlama kipi ve basamak `Vyxum:Weighing` ayarından gelir; para birimi her kayıtla saklanır. |
| **Kayıtta istek kimliği (requestId)** | Yanıtı kaybolan bir isteğin yeniden denenmesi ikinci bir kayıt oluşturmamalı. |
| **V-39: cihaz ayarları web veritabanında durur; agent alır** | Yapılandırma tek yerde; değişiklikler agent yeniden başlamadan uygulanır. Agent'ın kendi dosyası geliştirme geçersiz kılması olarak kalır. |
| **V-40: keşif yalnızca `SI` ve `I4` gönderir** | Terazi bulmak onu okumak kadar güvenli olmalı. Ağ taraması yok, tek adres. |

## Kurulum

| Karar | Neden |
|---|---|
| **V-36: Inno Setup** | Olgun, ücretsiz, tek `.exe`, servis ve yedek için betik desteği. Yalnızca derleme makinesinde gerekir. |
| **V-37: veri klasöründe paylaşılan `vyxum.json`** | Sunucu, agent ve masaüstü için güncellemelerden sağ çıkan tek ayar dosyası (program klasöründeki `appsettings.json`'ın aksine). Kurulum dosyası yazar; kurulum sihirbazı veritabanı kısmını değiştirebilir. |
| **V-38: web arayüzünde kurulum sihirbazı** | Müşteri dosya düzenlemeden yapılandırmayı bitirir. |
| **Kendi içinde tam yayımlama** | Müşterinin bilgisayarında .NET kurulu olması gerekmez. |
| **Yükseltmeden önce yedek (kurulum dosyası ve sunucu)** | Başarısız bir geçiş asla veri kaybı demek olmamalı. |

## Açık sorular (henüz karar yok)

- Ayrı geliştirilen bir modülün kendi ekranlarını React uygulamasına nasıl getireceği.
- Bu depo dışındaki modüllerin `Vyxum:Modules` listesinin ötesinde nasıl keşfedileceği (eklenti klasörü lisanslama fazı için planlıdır).
- Kurulum dosyası için kod imzalama.
- İletilmiş outbox iletilerinin temizlenmesi; birden çok dağıtıcının eşgüdümü.
- Geçişi olmayan modüller yükseltilemez.
