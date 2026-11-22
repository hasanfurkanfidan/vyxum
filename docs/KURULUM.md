# Vyxum kurulumu

[English](INSTALL.md)

Bu kılavuz Vyxum'u bir Windows bilgisayara kuracak kişi içindir. Teknik bilgi gerekmez. Kurulum yaklaşık 15 dakika sürer.

## 1. Gerekenler

- Windows 10 ya da Windows 11 yüklü, 64 bit bir bilgisayar.
- Bu bilgisayarda yönetici hesabı. Kurulum sırasında Windows onayınızı isteyecek.
- Terazi: seri kablo ya da USB-seri kablo ile bağlı bir terazi, ya da ağdaki bir terazi (IP adresi ve port).

Hepsi bu kadar. Vyxum için internet bağlantısı, veritabanı sunucusu ya da başka bir program gerekmez. Kurulum sırasında hiçbir şey indirilmez.

## 2. Kurulum

1. Size verilen `VyxumSetup-<sürüm>.exe` dosyasına çift tıklayın.
2. Windows **"Windows bilgisayarınızı korudu"** uyarısı gösterebilir. Bunun nedeni kurulum dosyasının henüz dijital olarak imzalanmamış olmasıdır. Devam etmek güvenlidir: **Ek bilgi**, ardından **Yine de çalıştır** seçeneğine tıklayın.
3. Windows, uygulamanın bilgisayarda değişiklik yapmasına izin verip vermediğinizi sorar. **Evet** deyin.
4. Kurulum sihirbazını izleyin. Genellikle sizden bir seçim istemez. Şunları yapar:
   - Vyxum'u `C:\Program Files\Vyxum` klasörüne kopyalar;
   - **Vyxum Web** ve **Vyxum Agent** adında iki Windows hizmeti kurar. Bu hizmetler Windows her açıldığında kendiliğinden başlar;
   - masaüstüne ve Başlat menüsüne **Vyxum** simgesi koyar.
5. Kurulum bitince **Vyxum'u aç** kutusu işaretliyse Vyxum açılır. **Son** düğmesine tıklayın.

Kurulum sihirbazı dil sorarsa Türkçe ya da English seçin.

## 3. İlk açılış: kurulum sihirbazı

Vyxum ilk kez açıldığında bir kurulum sihirbazı çıkar. Dört adımı vardır: **Veritabanı**, **Terazi**, **Firma** ve **Ürünler**. İleri gitmek için **İleri**, geri dönmek için **Geri** düğmesini kullanın.

Kurulum sihirbazı yalnızca Vyxum'un kurulu olduğu bilgisayardan yapılabilir.

**1. adım: Veritabanı.** Soru: "Kayıtlar nerede saklansın?" **Bu bilgisayarda sakla (önerilir)** seçili kalsın ve **İleri**'ye tıklayın. Başka bir şey yapmanız gerekmez.

Yalnızca kurumunuzda SQL Server varsa **Gelişmiş: SQL Server** bölümünü açın, bilgi işlem biriminizin verdiği bağlantı dizesini girin ve **Bağlantıyı dene** düğmesine tıklayın. Deneme başarılı olmadan devam edemezsiniz.

**2. adım: Terazi.** Vyxum teraziyi kendi başına arar. Teraziniz açık ve bağlı olsun. Vyxum her seri portu olağan ayarlarla dener. Teraziye yalnızca okuma komutları gönderir; teraziyi hiçbir şekilde değiştirmez.

- Terazi bulunursa teraziye bir şey koyun. Ekrandaki ağırlık değişiyorsa **Bu terazi doğru** düğmesine tıklayın.
- Terazi bulunamazsa kablonun ve terazinin açık olduğunu kontrol edip **Tekrar ara** düğmesine tıklayın.
- Yine olmazsa **Elle ayarla** düğmesine tıklayın ve terazinin menüsündeki ayarları girin. Ağdaki bir terazi için IP adresini ve portunu yazın.
- **Atla** düğmesine basıp teraziyi daha sonra Ayarlar, Cihazlar bölümünden de ekleyebilirsiniz.

**3. adım: Firma.** Firma adını yazın ve **İleri**'ye tıklayın. Firma adı ekranlarda ve raporlarda görünür.

**4. adım: Ürünler.** Tartacağınız ilk ürünleri ekleyin ya da **Atla** deyin. Ürünleri daha sonra Ürünler ekranından da ekleyebilirsiniz.

Sonunda **Kurulum tamamlandı** yazısı görünür. Tartıma başlamak için **Tartıma geç** düğmesine tıklayın. Sihirbaz bir daha açılmaz. Sonraki değişiklikler Ayarlar ekranından yapılır.

## 4. Günlük kullanım

- Masaüstündeki **Vyxum** simgesine çift tıklayın. Vyxum penceresi açılır. Pencerede adres çubuğu yoktur.
- Tam ekran ile normal pencere arasında geçiş için **F11** tuşuna basın.
- Vyxum hizmetleri arka planda çalışır. Çalışma saatlerinde bilgisayarı açık tutun.

Vyxum açılırken birkaç saniye "Vyxum başlatılıyor…" yazısı görünür.

## 5. Ayarlar

Vyxum'da **Ayarlar**'ı açın.

- **Cihazlar:** Yeni bir terazi eklemek için **Terazi ekle**, var olan bir teraziyi değiştirmek için **Düzenle** düğmesini kullanın. Burada canlı deneme de yapabilirsiniz: teraziye bir şey koyun ve ağırlığın değiştiğini görün.
- **Hakkında:** Sürümü, firmayı, veri klasörünü ve son yedeğin tarihini gösterir.

## 6. Güncelleme

Vyxum internetten güncellenmez. Güncellemek için yeni sürümün kurulum dosyasını kullanın:

1. Yeni `VyxumSetup-<sürüm>.exe` dosyasına çift tıklayın ve 2. bölümdeki adımları izleyin.
2. Kurulum, yüklü sürümü bulur ve Vyxum hizmetlerini durdurur.
3. Veritabanının yedeğini otomatik olarak `C:\ProgramData\Vyxum\backups` klasörüne alır.
4. Program dosyalarını değiştirir ve hizmetleri yeniden başlatır.

Kayıtlarınız ve ayarlarınız korunur. Yedek klasöründe son 10 yedek tutulur.

## 7. Kaldırma

1. **Ayarlar**'ı, ardından **Uygulamalar**'ı ve **Yüklü uygulamalar**'ı açın.
2. **Vyxum**'a tıklayın, ardından **Kaldır**'a basın.
3. Sonunda Vyxum şu soruyu sorar: "Veritabanı, ayarlar ve yedekler de silinsin mi?" Varsayılan cevap **Hayır**'dır ve verileriniz korunur. Tüm kayıtları silmek istiyorsanız **Evet**'e tıklayın. Bu işlem geri alınamaz.

## 8. Sorun giderme

**Terazi bulunamıyor.**
- Terazinin açık olduğunu ve kablonun iki ucunun da sağlam takılı olduğunu kontrol edin.
- Aynı portu kullanan diğer programları kapatın. Örneğin teraziye ait kendi yazılımı.
- USB-seri adaptör kullanıyorsanız önce adaptörün sürücüsünü kurun. Sürücüyü adaptörün üreticisi verir.
- **Tekrar ara** düğmesine tıklayın ya da **Elle ayarla** kullanın.

**"Vyxum başlatılıyor…" yazısı ekranda kalıyor.**
- Bir dakika bekleyin. Vyxum hâlâ açılmazsa bir uyarı gösterir. **Yeniden dene** düğmesine tıklayın.
- Bilgisayarı yeniden başlatın. Çoğu durumda bu sorunu çözer.

**Kayıtlarım ve ayarlarım nerede?**
Bütün veriler `C:\ProgramData\Vyxum` klasöründedir:
- `vyxum.db`: kayıtlar (veritabanı);
- `vyxum.json`: kurulum sırasında yazılan ayarlar. Bu dosyayı elle düzenlemeniz gerekmez;
- `backups`: otomatik yedekler.

Bu klasörü yalnızca yöneticiler açabilir, çünkü kayıtlarınızı ve veritabanı parolasını tutar. Tüm verileri silmek istemiyorsanız bu klasörü silmeyin.

**"Windows bilgisayarınızı korudu" uyarısı yine çıkıyor.** Kurulum dosyası dijital olarak imzalanana kadar bu normaldir. 2. bölüme bakın.
