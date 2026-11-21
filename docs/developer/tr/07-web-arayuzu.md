# 7. Web arayüzü

[Dizin](../README.md#türkçe) · Önceki: [6. Cihazlar ve Agent](06-cihazlar-ve-agent.md) · Sonraki: [8. Kurulum dosyası ve sürüm](08-surum.md)

## Temel bilgiler

Ekranlar, `src/Web/vyxum-web` içindeki bir **React 19 + TypeScript + Vite** uygulamasıdır. Sıradan bir tek sayfa uygulamasıdır: `npm run build` statik dosyaları `src/Host/Vyxum.Host/wwwroot` içine üretir ve ASP.NET Core sunucusu bunları sunar. Müşteri Node.js kurmaz. `wwwroot` klasörü commit edilmez.

Sunucu, `/api`, `/hubs` ya da `/health` olmayan bilinmeyen yolları `index.html`'e gönderir; böylece istemci tarafı yolları (örneğin `/records`) tarayıcı yenilemesinde bozulmaz.

Komutlar (`src/Web/vyxum-web` içinde, Node.js 22):

| Komut | Ne yapar |
|---|---|
| `npm ci` | `package-lock.json`'daki tam paket sürümlerini kurar. |
| `npm run dev` | Canlı yenilemeli Vite geliştirme sunucusu (genelde http://localhost:5173); `/api` ve `/hubs` isteklerini `http://localhost:5000` adresindeki sunucuya yönlendirir. |
| `npm run typecheck` | TypeScript denetimi. |
| `npm test` | Vitest (jsdom) ve Testing Library. |
| `npm run build` | Tür denetimi, sonra sunucunun `wwwroot` klasörüne derleme. |

CI `typecheck`, `test` ve `build` çalıştırır.

## Klasör rehberi (`src/`)

| Klasör | İçerik |
|---|---|
| `main.tsx`, `App.tsx` | Başlangıç, sağlayıcılar, yollar. |
| `shell/` | Ekranların çerçevesi: kenar çubuğu (`Sidebar.tsx`), menü tanımı (`navigation.ts`), kurulum kapısı, modül sağlayıcısı, dil anahtarı. |
| `pages/` | Ekran başına bir dosya: tartım, kayıtlar, ürünler; `settings/` (cihazlar, hakkında); `setup/` (ilk açılış sihirbazı). |
| `components/devices/` | Sihirbaz ile Ayarlar → Cihazlar ekranının ortak parçaları. |
| `lib/` | Düz TypeScript: API çağrıları (`api.ts`, `weighing.ts`, `products.ts`, `devices.ts`, `setup.ts`), sayı/tarih biçimleme, ondalık girişi. Burada React yok. |
| `i18n/` | Metinleri yükler, ekranlara `t(key)` işlevi verir. |
| `styles/` | Alan başına CSS dosyaları; tasarım değerleri `tokens.css` içinde. |
| `test/` | Test yardımcıları: sahte API, sahte `EventSource`, gömülü metin denetleyicisi. |

## Kodda metin yok

Kullanıcının gördüğü her cümle sunucudan gelir. Her modülün (ve sunucunun, platformun kurulum/cihaz bölümlerinin) `Strings.resx` (İngilizce) ve `Strings.tr.resx` (Türkçe) dosyası vardır. Platform bunları birleştirip `GET /api/ui/strings` ile `Önek.Anahtar` olarak sunar (örneğin `products.Title`; sunucunun kendi öneki `shell`). Bileşende kelimeleri değil `t('products.Title')` yazarsınız. Arayüz varsayılan olarak Türkçedir; kullanıcı dili menüden değiştirir ve seçim bir çerezde hatırlanır.

Bunu iki test korur: `noHardcodedText.test.ts`, bir `.tsx` dosyasında etiketler arasında düz metin varsa; `ResourceFileTests` (mimari testler), Türkçe dosyada anahtar eksikse ya da boş değer varsa başarısız olur. Metni yalnızca tek dilde olan bir ekran **bitmiş değildir**.

Metin eklemek için: anahtarı sahibi projenin `Strings.resx` **ve** `Strings.tr.resx` dosyalarına ekleyin, sunucuyu yeniden derleyin, `t('<önek>.<Anahtar>')` kullanın. Geliştirmede eksik anahtar için konsol uyarı verir.

## Çevrimdışı

Yazı tipleri (Instrument Sans, JetBrains Mono) `@fontsource` paketlerinden gelir ve derlemeye gömülüdür. CDN bağlantısı, uzak yazı tipi ya da analitik eklemeyin. Her yeni npm paketinin lisansını denetleyin (yalnızca MIT, BSD, Apache-2.0 uyumlu).

## Hangi menü girdileri görünür?

`shell/navigation.ts` menüyü tanımlar. Bir `module` (örneğin `weighing`) belirten girdi yalnızca o modül yüklüyse görünür; liste `GET /api/ui/modules` ile gelir. Platform ekranları (ayarlar) her zaman görünür. İsteğe bağlı modüllerin özellikleri yeteneklere bağlıdır; örneğin operatör sütunu yalnızca `identity.users` varsa.

**Henüz yapılmadı:** ayrı geliştirilen bir modülün kendi ekranlarını bu uygulamaya getirmesinin bir yolu. `navGroups` içindeki boş `modules` grubu bunun için ayrılmıştır; kararı sonraki fazdadır. Bir modül arayüzü yapmadan önce, JavaScript'inin `wwwroot`'a nasıl ulaşacağına ve yolların nasıl kaydedileceğine karar verin ve kararı [10. bölüme](10-kararlar.md) yazın.

## Ekran ekleme (yerleşik modül için)

1. API çağrılarını `lib/<ad>.ts` içine, sayfayı `pages/` içine ekleyin.
2. `App.tsx` içine bir yol, `shell/navigation.ts` içindeki `routes`'a bir yol ve `module`'ü belirtilmiş bir menü girdisi ekleyin.
3. Metinleri iki `.resx` dosyasına da ekleyin.
4. Sayfanın yanına bir test yazın (`ProductsPage.test.tsx`'e bakın; `test/fakeApi.ts` sunucuyu taklit eder).
5. `npm run typecheck && npm test && npm run build` çalıştırın.

## Canlı ağırlık

Tartım ekranı bir cihazı `/api/devices/{id}/live` üzerinde `EventSource` (Server-Sent Events) ile izler (`components/devices/useLiveDevice.ts`). Tarayıcı kendiliğinden yeniden bağlanır. Sunucu önce mevcut durumu, sonra her değişikliği gönderir.
