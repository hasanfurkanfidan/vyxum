# 8. Kurulum dosyası ve sürüm çıkarma

[Dizin](../README.md#türkçe) · Önceki: [7. Web arayüzü](07-web-arayuzu.md) · Sonraki: [9. Sorun giderme](09-sorun-giderme.md)

## Müşterinin aldığı

Tek dosya: **Inno Setup 6** ile üretilen `VyxumSetup-<sürüm>.exe` (yalnızca derleme aracıdır, ürünün parçası değildir). Windows 10/11 x64 için, kendi içinde tam (müşterinin bilgisayarında .NET gerekmez) yayımlanmış üç programı kurar. Son kullanıcı adımları [docs/KURULUM.md](../../KURULUM.md) içindedir (İngilizce: [docs/INSTALL.md](../../INSTALL.md)).

## Dosyalar

| Dosya | Rol |
|---|---|
| `installer/build-installer.ps1` | Arayüzü derler, üç programı yayımlar, çıktıyı denetler, Inno Setup derleyicisini çalıştırır. |
| `installer/vyxum.iss` | Inno Setup betiği (UTF-8 **BOM'lu**; öyle bırakın). Metinler Türkçe ve İngilizce; dil Windows'u izler. |
| `installer/vyxum.default.json` | Varsayılan `vyxum.json`: `{"Urls":"http://localhost:5000","Vyxum":{"Agent":{"ServerUrl":"http://localhost:5000"}}}`. |
| `installer/THIRD-PARTY-NOTICES.txt` | Pakete gömülü bileşenlerin lisansları. Müşteriye giden bir paket eklediğinizde güncelleyin. |
| `installer/README.md` | Kısa başvuru. |

## Kurulum dosyasını yerelde derleme (Windows)

Önkoşullar: .NET 10 SDK, Node.js 22, Inno Setup 6 (`ISCC.exe` `PATH` üzerinde ya da `Program Files (x86)\Inno Setup 6` içinde).

```powershell
./installer/build-installer.ps1
```

Çıktı: `artifacts/installer/VyxumSetup-<sürüm>.exe`. Seçenekler: `-Version 1.2.3`, `-OutputDir <yol>`, `-SkipCompile` (yalnızca `artifacts/publish/{web,agent,desktop}` içine yayımlar).

Betik sırasıyla şunları yapar: sürümü okur (`-Version`, yoksa `Directory.Build.props` içindeki `<Version>`); `npm ci` + `npm run build`; Host, Agent ve Desktop için `dotnet publish` (Release, `win-x64`, kendi içinde tam, arayüz zaten derlendiği için `-p:SkipUiBuild=true` ile); `web\wwwroot\index.html`, `Vyxum.Host.exe`, `Vyxum.Agent.exe`, `Vyxum.exe` ve `WebView2Loader.dll` dosyalarının var olduğunu denetler; `ISCC` çalıştırır.

CI, her push ve çekme isteğinde `windows-latest` üzerinde kurulum dosyasını derler ve `VyxumSetup` adıyla yükler (`.github/workflows/ci.yml` içindeki `installer` işi).

## Kurulum müşterinin bilgisayarında ne yapar?

- `Program Files\Vyxum` içine kopyalar (`Web\`, `Agent\`, `Vyxum.exe`). Yönetici hakkı ister.
- `%ProgramData%\Vyxum\vyxum.json` dosyasını yalnızca yoksa oluşturur (`onlyifdoesntexist`) ve kaldırırken asla silmez (`uninsneveruninstall`).
- `sc.exe` ile iki Windows servisi kaydeder: **Vyxum Web** (`Web\Vyxum.Host.exe`) ve **Vyxum Agent** (`Agent\Vyxum.Agent.exe`); otomatik başlatma, kurtarma "5 sn sonra yeniden başlat" üç kez ve hata bayrağı (kurulum sihirbazı veritabanını değiştirince sunucunun bilerek hata koduyla çıkması da yeniden başlatmayı tetikler).
- Masaüstü ve başlat menüsü kısayolları oluşturur.
- **Güncelleme:** yeni kurulum dosyasını çalıştırmak iki servisi durdurur, SQLite veritabanını (ve `-wal`/`-shm`) `%ProgramData%\Vyxum\backups\vyxum-<tarih>-before-setup-<eski sürüm>.db` olarak kopyalar, dosyaları değiştirir ve servisleri başlatır. `vyxum.json`'a dokunulmaz. Yeni sunucu açılışta bekleyen geçişleri uygular ([5. bölüm](05-veritabani.md)), önce kendi yedeğini alarak.
- **Kaldırma:** servisleri durdurup siler, program dosyalarını kaldırır; verilerin silinip silinmeyeceğini sorar (varsayılan Hayır).

## Yeni sürüm nasıl çıkarılır?

Bu depoda henüz otomatik bir sürüm hattı yoktur; sürüm elle, şu sırayla çıkarılır:

1. `main`in CI'da yeşil olduğundan emin olun (derleme, SQLite **ve** SQL Server testleri, web kontrolleri, kurulum işi).
2. **Sürüm:** `Directory.Build.props` içindeki `<Version>` değerini (tek yer; derlemeler, Hakkında ekranı ve kurulum dosyası buradan alır) kendi commit'inde değiştirin, örneğin `Release 0.2.0`.
3. **Geçiş denetimi:** son sürümden beri her model değişikliğinin iki sağlayıcı için geçişi olmalı (testler zorlar). Yayınlanmış bir geçişi asla düzenlemeyin.
4. **Yükseltme testi:** *önceki* sürümün kurulum dosyasını temiz bir Windows makinesine (sanal makine) kurun, birkaç ürün ve tartım oluşturun, sonra *yeni* kurulum dosyasını üstüne çalıştırın. Denetleyin: servisler çalışıyor, veri duruyor, `backups\` içinde bir yedek dosyası var.
5. Kurulum dosyasını derleyin (CI çıktısı ya da betik) ve **temiz kurulumu** da, kurulum sihirbazı ve simüle ya da gerçek bir terazi dahil, test edin.
6. Paketler değiştiyse `installer/THIRD-PARTY-NOTICES.txt` dosyasını güncelleyin.
7. Commit'i etiketleyin (`git tag v0.2.0`), etiketi gönderin ve kurulum dosyasını bir GitHub sürümüne ekleyin.
8. Ekranlar ya da adımlar değiştiyse son kullanıcı kılavuzlarını (iki dilde) güncelleyin.

**Kod imzalama:** bu depoda imzalama düzeni yoktur ve imza anahtarları ya da sertifikaları asla commit edilmemelidir. İmzasız kurulum dosyası Windows SmartScreen'in uyarı göstermesine yol açar. İmzalama eklenince sertifikayı depo dışında tutun (CI sırrı ya da donanım anahtarı) ve adımı buraya yazın.
