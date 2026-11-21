# Vyxum kurulum dosyası

`VyxumSetup-<sürüm>.exe`, [Inno Setup 6](https://jrsoftware.org/isinfo.php) ile üretilen tek bir kurulum programıdır (karar V-36). Inno Setup yalnızca derleme bilgisayarında ve CI'da gerekir; ürüne girmez.

## Bu bilgisayarda derleme

Gerekenler (Windows x64):

- .NET 10 SDK (`global.json`)
- Node.js 22
- Inno Setup 6 (`ISCC.exe`, `Program Files (x86)\Inno Setup 6` içinde ya da `PATH` üzerinde)

```powershell
./installer/build-installer.ps1
```

Çıktı: `artifacts/installer/VyxumSetup-<sürüm>.exe`.

Seçenekler:

| Seçenek | Anlamı |
|---|---|
| `-Version 1.2.3` | Ürün sürümü. Varsayılan: `Directory.Build.props` içindeki `<Version>` (tek sürüm kaynağı). |
| `-OutputDir <yol>` | Kurulum dosyasının yazılacağı klasör. |
| `-SkipCompile` | Yalnızca uygulamaları `artifacts/publish` içine yayımlar; Inno Setup çalıştırmaz. |

Betik web arayüzünü derler (`npm ci`, `npm run build`), Host, Agent ve Desktop'ı yayımlar (Release, win-x64, self-contained, klasör olarak) ve `artifacts/publish/{web,agent,desktop}` içine koyar, sonucu denetler (`wwwroot/index.html`, `WebView2Loader.dll`) ve `ISCC` çalıştırır.

## Kurulum dosyası ne yapar

- `Program Files\Vyxum` içine kurar (`Web\`, `Agent\`, `Vyxum.exe`); yönetici hakkı ister; Türkçe ve İngilizce (Windows diline göre seçilir).
- `%ProgramData%\Vyxum\vyxum.json` dosyasını yalnızca yoksa oluşturur (varsayılan adres `http://localhost:5000`).
- **Vyxum Web** ve **Vyxum Agent** Windows servislerini kaydeder (otomatik başlar). Kurtarma: 5 saniye sonra yeniden başlat, üç kez; süreç kendi isteğiyle hata koduyla çıksa da uygulanır (Web, sihirbazda veritabanı değişince bunu bilerek yapar).
- Masaüstü ve başlat menüsü kısayolları oluşturur, sonunda Vyxum'u açmayı önerir.
- **Güncelleme:** yeni kurulum dosyası çalıştırılır. İki servisi durdurur, `vyxum.db` dosyasını (ve `-wal`/`-shm`) `%ProgramData%\Vyxum\backups\vyxum-<tarih>-before-setup-<eski sürüm>.db` olarak yedekler, dosyaları değiştirir, servisleri başlatır. `vyxum.json` hiç değişmez.
- **Kaldırma:** servisleri durdurup siler, program dosyalarını kaldırır. `%ProgramData%\Vyxum` içindeki veriler, sonda sorulan soruya "Evet" denmedikçe kalır (varsayılan "Hayır").

## Dosyalar

- `vyxum.iss` Inno Setup betiği (BOM'lu UTF-8; böyle kalmalı).
- `vyxum.default.json` varsayılan ortak ayarlar.
- `THIRD-PARTY-NOTICES.txt` pakete giren bileşenlerin lisansları.
