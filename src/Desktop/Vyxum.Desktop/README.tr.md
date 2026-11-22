# Vyxum.Desktop

Vyxum'un tek bilgisayardaki masaüstü penceresi: web uygulamasını gösteren, WebView2 çevresinde bir WinForms kabuğudur. Ürün web uygulaması olarak kalır; bu pencere yalnızca tarayıcı çerçevesini kaldırır (adres çubuğu yok, kendi simgesi, isteğe bağlı tam ekran).

- Çıktı: `Vyxum.exe` (derleme adı `Vyxum`), `net10.0-windows`. Linux'ta da derlenir (`EnableWindowsTargeting`), ama yalnızca Windows 10/11'de çalışır.
- Adres: `%ProgramData%\Vyxum\vyxum.json` içindeki `Urls` (ilk girdi; `*`, `+`, `0.0.0.0` ve `[::]` sunucu adları `localhost` olur). Komut satırında `--url <adres>` bunu geçersiz kılar. Varsayılan `http://localhost:5000`.
- Tam ekran: F11 açar ve kapatır. Komut satırında `--full-screen` (örneğin masaüstü kısayolunda) ya da `vyxum.json` içinde `Vyxum:Desktop:FullScreen` (`true`) tam ekran başlatır; dokunmatik terminaller içindir.
- `vyxum.json` veritabanı parolasını ve agent anahtarını tuttuğu için kurulum bu dosyayı yalnızca yöneticilerin ve Vyxum servislerinin okumasına izin verir. Standart bir kullanıcının açtığı pencere dosyayı okuyamaz; varsayılanları ve komut satırını kullanır.
- Açılış: "Vyxum başlatılıyor..." yazar ve 500 ms'de bir `GET {adres}/health` çağırır. 60 saniye içinde yanıt gelmezse "Yeniden dene" düğmeli bir ileti gösterir. Sunucu sonradan giderse (örneğin yeniden başlarsa) bekleme görünümü kendiliğinden döner.
- Uygulamanın adresinin dışına çıkan bağlantılar varsayılan tarayıcıda açılır.
- WebView2 çalışma zamanı yoksa (Windows 10/11 ile gelir) adres varsayılan tarayıcıda açılır ve pencere kapanır.
- İkinci kez açılış çalışan pencereyi öne getirir.
- WebView2 veri klasörü: `%LocalAppData%\Vyxum\WebView2`. Release derlemesinde sağ tık menüsü ve geliştirici araçları kapalıdır.
- Metinler `Resources/Strings.resx` (İngilizce) ve `Resources/Strings.tr.resx` (Türkçe) dosyalarındadır; Windows görüntü diline göre seçilir.
- `vyxum.ico` geçici bir simgedir (vurgu renginde düz kare).

Yayımlama (kurulum dosyası bunu kullanır):

```
dotnet publish src/Desktop/Vyxum.Desktop -c Release -r win-x64 --self-contained -o <klasör>
```

Adres ve sağlık mantığının testleri: `tests/Vyxum.Desktop.Tests` (her platformda çalışır).
