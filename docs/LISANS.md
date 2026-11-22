# Vyxum lisansı

[English](LICENSING.md)

Bu kılavuz lisans dosyasının ne olduğunu ve Vyxum'a nasıl yükleneceğini anlatır. Teknik bilgi gerekmez.

## 1. Ücretsiz olan kısımlar

**Tartım**, **Ürünler** ve **Kayıtlar** ücretsizdir. Lisans istemezler ve lisans olmadan da tam olarak çalışırlar.

Lisans yalnızca ücretli modülleri açar. Hangi modüllerin ücretli olduğunu ve durumlarını **Ayarlar**, **Modüller ve lisans** ekranında görürsünüz.

## 2. Lisans dosyası nedir

Lisans dosyası, uzantısı `.vxlic` olan küçük bir dosyadır. Bizden ya da yetkili satıcınızdan alırsınız. Dosyada şunlar yazılıdır:

- lisans sahibinin firma adı;
- açılacak modüller;
- istasyon sayısı: ücretli modülleri kullanabilecek cihaz (terazi) sayısı;
- geçerlilik: başlangıç ve bitiş tarihi, ya da süresiz olduğu;
- lisans türü ve lisans numarası.

Dosyayı açıp düzenlemeyin. Değiştirilmiş bir dosya geçersiz olur ve kabul edilmez.

## 3. Lisans dosyasını yükleme

**Kurulum sihirbazında.** Sihirbaz beş adımdan oluşur ve **Lisans** adımı dördüncü adımdır (Firma'dan sonra, Ürünler'den önce).

1. **Lisans** adımında **Lisans dosyası yükle** seçeneğini işaretleyin.
2. **Dosya seç** düğmesine tıklayın ve `.vxlic` dosyasını seçin.
3. Dosya yüklenince "Lisans yüklendi: ..." yazısı ve firma adı görünür.
4. **İleri**'ye tıklayın.

Lisans dosyanız yoksa **Ücretsiz devam et** seçeneğini bırakıp devam edebilirsiniz. Lisansı daha sonra yükleyebilirsiniz.

**Kurulumdan sonra.** **Ayarlar**'ı açın, **Modüller ve lisans** bölümüne gidin ve **Lisans dosyası yükle** bölümünden `.vxlic` dosyasını seçip yükleyin. Yeni bir modül açmak için Vyxum'u yeniden kurmanız gerekmez.

## 4. Lisans durumu

**Modüller ve lisans** ekranındaki **Lisans** bölümü lisansın durumunu bir satırda gösterir:

| Durum | Anlamı |
|---|---|
| imza doğrulandı | Dosya bizden geldi ve değiştirilmemiş. Lisans geçerli. |
| henüz geçerli değil | Lisans süresi henüz başlamadı. Başlangıç tarihinde açılır. |
| süresi doldu, ek süre {N} gün | Lisans süresi doldu. Ücretli modüller {N} gün daha çalışır. |
| süresi doldu, salt okunur | Ek süre de bitti. Ücretli modüller salt okunur. |
| lisans yok | Yüklü bir lisans yok. Yalnızca ücretsiz kısımlar çalışır. |
| geçersiz: {neden} | Dosya kabul edilmedi. Neden satırda yazar (aşağıya bakın). |

**Geçersiz dosya nedenleri:**

- **İmza tutmuyor: dosya değiştirilmiş ya da bozulmuş.** Yeni bir dosya isteyin.
- **Bu bir Vyxum lisans dosyası değil.** Bizden aldığınız dosyayı seçtiğinizi kontrol edin.
- **Dosya bir lisans dosyası olamayacak kadar büyük.** Dosya bozulmuş olabilir. Yeni bir dosya isteyin.
- **Lisans dosyasının içeriği eksik ya da okunamıyor.** Yeni bir dosya isteyin.
- **Bu lisans dosyası Vyxum'un daha yeni bir sürümü için hazırlanmış.** Vyxum'u güncelleyin ya da güncel biçimde yeni bir dosya isteyin.
- **Dosya, bu Vyxum sürümünün tanımadığı bir anahtarla imzalanmış.** Vyxum'u güncelleyin ya da bizden destek isteyin.
- **Dosya okunamadı.** Dosyanın başka bir program tarafından açık olmadığını kontrol edip yeniden deneyin.

## 5. Süre dolunca ne olur

1. **Süre bittiğinde 30 gün ek süre başlar.** Bu sürede ücretli modüller çalışmaya devam eder. Ekranın üstünde "Lisans süresi doldu; ücretli modüller N gün daha çalışır." uyarısı görünür. Uyarının yanındaki **Lisans ayarları** bağlantısıyla lisans ekranına gidebilirsiniz.
2. **Ek süre bitince ücretli modüller salt okunur olur.** Uyarı "Lisans süresi bitti; ücretli modüller salt okunur." yazısına döner. Ücretli modüllerde yeni kayıt ya da değişiklik yapılamaz.
3. **Verileriniz hiçbir zaman silinmez.** Mevcut kayıtlar, ürünler ve ayarlar olduğu gibi kalır.
4. **Tartım, Ürünler ve Kayıtlar her zaman çalışır.** Süre dolsa da bu kısımlar kullanılabilir.

Yeni bir lisans dosyası yüklediğinizde ücretli modüller hemen yeniden açılır.

## 6. İstasyonlar

Bir **istasyon**, bir cihazdır (terazi). Lisansta kaç istasyon olduğu yazılıdır.

1. Cihazları önce **Ayarlar**, **Cihazlar** bölümünden ekleyin.
2. **Modüller ve lisans** ekranındaki **İstasyonlar** bölümünde, ücretli modülleri kullanacak cihazları seçin.
3. **İstasyonları kaydet** düğmesine tıklayın. "İstasyonlar kaydedildi." yazısı görünür.

Seçilmeyen cihazın durumu **kapalı** olur. Bu cihazda ücretli modüller kullanılmaz, ama tartım yapılabilir. **Tartımın kendisi istasyon sayısıyla sınırlanmaz.**

Lisanstaki istasyon sayısından fazla cihaz seçerseniz kayıt yapılmaz: "Lisans, seçilenden az istasyona izin veriyor." Bir cihazın seçimini kaldırıp yeniden kaydedin.

## 7. Lisansı kaldırma

1. **Modüller ve lisans** ekranında **Lisansı kaldır** düğmesine tıklayın.
2. Açılan soruda **Kaldır**'a tıklayın. Vazgeçmek için **Vazgeç**'e tıklayın.

Lisans kaldırılınca ücretli modüller yeniden kilitlenir. **Kayıtlarınız silinmez.** Lisansı istediğiniz zaman aynı dosyayla ya da yeni bir dosyayla yeniden yükleyebilirsiniz.

## 8. İnternet gerekmez

Lisans dosyası bilgisayarınızda, internete bağlanmadan doğrulanır. Vyxum lisans için hiçbir sunucuya bağlanmaz. Lisansı yüklemek ve kullanmak için internet bağlantısı gerekmez.

## 9. Bilgisayarın saati

Vyxum, bilgisayarın saatini izler. Saat, Vyxum'un daha önce gördüğü bir zamanın gerisindeyse ekranda uyarı görünür: "Bu bilgisayarın saati, Vyxum'un daha önce gördüğü bir zamanın gerisinde." Lisans süresi en son görülen zamana göre sayılır. Saati geri almak lisans süresini uzatmaz. Saati doğru ayarlayın.

## 10. Lisans nasıl alınır

Lisans almak için bizimle ya da yetkili satıcınızla iletişime geçin.
