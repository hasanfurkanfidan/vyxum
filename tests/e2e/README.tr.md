# Uçtan uca ekran denetimi

[English](README.md)

Tek komut gerçek Vyxum'u boş bir veritabanıyla başlatır, gerçek Agent'ı sahte bir teraziye bağlar, ekranları gerçek bir
tarayıcıda bir kullanıcı gibi kullanır, sonuçları doğrular ve her adımın ekran görüntüsünü alır. Sonunda görüntüleriyle
birlikte kısa bir geçti/kaldı raporu yazar.

`src/Web/vyxum-web` içindeki arayüz testleri (Vitest) her ekranı sahte bir API'ye karşı dener ve hızlı güvenlik ağı
olarak kalır. Bu denetim onların göremediğini ekler: sunucunun verdiği derlenmiş arayüz, veritabanı, Agent ve canlı
ağırlıklar, lisans dosyaları ve ekranların görünüşü.

## Çalıştırma

.NET 10 SDK ve Node.js 22 yeterlidir. Windows'ta her zaman kurulu olan Microsoft Edge kullanılır. Linux ve macOS'ta bu
klasörde bir kez `npx playwright install chromium` çalıştırın.

```bash
node tests/e2e/run.mjs
```

Komut web arayüzünü (yalnızca hiç derlenmemişse; `--build-ui` yeniden derler), deneme sunucusunu ve Agent'ı derler,
sonra şunları başlatır:

- `http://127.0.0.1:5390` adresinde boş veri klasörüyle deneme sunucusu (`tests/Vyxum.E2E.Host`);
- deneme sunucusunun içinde, TCP 5391 portunda sahte bir MT-SICS terazi;
- deneme sunucusuna bağlı Agent.

Kurulu Vyxum'un portu (5000) kullanılmaz. 5390 doluysa `--port 5490` verin.

| Seçenek | |
|---|---|
| `-- scenarios/01 scenarios/05` | Yalnızca bu dosyalar (`--` sonrasında, Playwright dosya süzgeci olarak). `01`'i ekleyin: sonraki senaryolar tamamlanmış sihirbaza ihtiyaç duyar. |
| `--grep metin` | Yalnızca başlığı eşleşen senaryolar; başka bölümlerin bağlı olduğu bölümler (`setup`, `core`) yine tam çalışır. |
| `--no-build` | Son çalıştırmadan beri bir şey değişmediyse derlemeleri atlar. |
| `--headed` | Tarayıcıyı gösterir. |
| `--real-url http://localhost:5000` | Gerçek terazinin bağlı olduğu kurulu Vyxum (aşağıya bakın). `none` kapatır. |
| `-- <argümanlar>` | `--` sonrasındaki her şey olduğu gibi Playwright'a gider. |

Bütün senaryolar geçtiyse çıkış kodu 0'dır.

## Rapor

Komut raporu nereye yazdığını sonunda söyler: `artifacts/e2e/<zaman>/` (en yenisinin yolu `artifacts/e2e/latest.txt`
içinde de vardır).

| Dosya | |
|---|---|
| `summary.md` | Kısa rapor: sonuç, kalan senaryolar ve görüntüleri, sahte teraziyle denenenler, gerçek teraziyle denenenler ve yalnızca bir kişinin yapabileceği denemeler (`manual-checks.md`). |
| `shots/<bölüm>/<senaryo>/NN-ad.png` | Bütün ekran görüntüleri, adım sırasıyla numaralı. |
| `html/index.html` | Playwright raporu: her adım, görüntü ve kalan senaryo için iz kaydı (`npx playwright show-trace <trace.zip>`). |
| `logs/host.log`, `logs/agent.log` | Deneme sunucusunun ve Agent'ın çıktısı. |
| `summary.json` | Aynı sonuçlar, bir program için. |

## Bir kodlama işinden sonra

İşi bitiren oturum denetimi çalıştırır ve ürün sahibine bildirir:

1. `node tests/e2e/run.mjs` (kurulu Vyxum çalışıyorsa gerçek terazi de denenir).
2. `summary.md`'yi okur. İşin bozduğu bir şey varsa iş bitti denmeden önce düzeltilir.
3. Sonuç satırını, kalanları ve işin değiştirdiği ekranların görüntülerini gönderir. Hangi denemelerin sahte, hangilerinin
   gerçek teraziyle yapıldığını söyler ve açık kalan elle denemeleri sıralar.
4. İş bir ekranı değiştirdiyse ya da yeni ekran eklediyse senaryosunu da ekler ya da günceller (aşağıda).

## Gerçek terazi

`http://localhost:5000` adresinde (ya da `--real-url` ile verilen yerde) kurulu bir Vyxum yanıt veriyorsa `real-device`
bölümü onu **salt okunur** dener: cihaz listesi terazinin bağlı olduğunu söylüyor mu, yeni okumalar geliyor mu, tartım
ekranı canlı ağırlığı gösteriyor mu. Orada hiçbir şeye tıklamaz, hiçbir şeyi değiştirmez. Sayının doğru ağırlık olup
olmadığını bilinen bir ağırlıkla bir kişinin denemesi gerekir; bu ve diğer elle denemeler `manual-checks.md` içindedir ve
her rapora eklenir.

## Deneme sunucusunun ekledikleri

`tests/Vyxum.E2E.Host` sunucunun kendisini (`Program`, ara katmanlar, web arayüzü) değiştirmeden çalıştırır ve yalnızca
denemeye özel üç şey ekler. Hiçbir sürüme girmez.

- Açılışta üretilen bir lisans imza anahtarına da güvenir. Anahtar yalnızca bellekte yaşar; üretim anahtarları da
  güvenilir kalır.
- `GET /e2e/license?kind=valid|perpetual|not-yet|grace|read-only|tampered|not-a-license&modules=a,b&stations=2` bu
  anahtarla imzalanmış bir lisans dosyası döndürür.
- `POST /e2e/scale?value=1250&unit=g&state=stable|motion|overload|underload` sahte terazinin gösterdiğini değiştirir;
  `silent=true` teraziyi susturur (kablo çekilmiş gibi). `GET /e2e/scale` terazinin aldığı komutları döndürür; böylece
  senaryo teraziye dara ya da sıfırlama komutu gitmediğini kanıtlayabilir.

## Senaryo ekleme

Senaryolar `scenarios/` içindedir. Hepsi aynı sunucuyu şu sırayla kullanır (`playwright.config.ts`):

1. `setup`: `01-setup.spec.ts`, boş veritabanında sihirbaz. Teraziyi, firmayı ve iki ürünü ekler.
2. `core`: dosya sırasıyla `02-…` ile `09-…` arası, Türkçe. Sonraki dosyalar öncekilerin oluşturduğunu kullanabilir
   (tartım senaryosu ürün senaryosunun eklediği ürünü tartar); bunu senaryonun açıklamasında yazın.
3. `looks-…`: `looks.spec.ts` ana ekranları İngilizce ve 1280×800 dokunmatik ekranda dolaşır. Yeni ekranı listesine
   ekleyin.
4. `real-device`: `real-scale.spec.ts`, yalnızca kurulu bir Vyxum varken.

Bir senaryo şöyle yazılır:

```ts
import { expectCleanScreen, exact, test, expect } from '../support/vyxum';

test('Products: a product can be deactivated', async ({ page, t, shot, scale, licenses }) => {
  await page.goto('/products');
  await page.getByRole('button', { name: exact(t('products.Deactivate')) }).click();
  await expect(page.getByText(t('products.Inactive'))).toBeVisible();
  await expectCleanScreen(page);
  await shot('urun-pasif');
});
```

- `t('anahtar', ...değerler)` ekranın gösterdiği metni, sunucunun kaynak dosyalarından bölümün dilinde okur. Senaryoya
  arayüz metni yazmayın; bilinmeyen bir anahtar testi düşürür.
- `shot('ad')` rapor için ekran görüntüsü alır. Bir kişinin bakacağı her adımdan sonra bir tane alın. Adlar kısa ve
  Türkçedir: rapor Türkçe okunur.
- `scale.set({ value, unit, state })`, `scale.silent(true)`, `scale.status()` sahte teraziyi yönetir.
- `licenses.get('valid', { modules: ['samples.paid'] })` `setInputFiles` için bir dosya döndürür.
- `expectCleanScreen(page)` ham kaynak anahtarı (eksik çeviri) ve yana kaydırma görürse düşer.
- Her betik hatası ya da konsol hatası senaryoyu düşürür. Bir ekran bilerek hata yazıyorsa (tartım ekranı reddedilen
  kaydı yazar) o adımdan önce `allowedBrowserErrors` dizisine bir kalıp ekleyin.
- CSS sınıfları yerine rol ve etiket (`getByRole`, `getByLabel`) kullanın; kişinin gördüğü budur ve görünüm değişince
  kırılmaz.
- El gerektiren bir deneme (gerçek ağırlık, kablo, basılı etiket) Türkçe olarak `manual-checks.md`'ye yazılır.

## Ücretli modüller

Açık çekirdek kilitli önizleme, lisans durumları ve salt okunur kip için örnek ücretli modülü
(`tests/SampleModules/Vyxum.Samples.Paid`) kullanır. Ücretli modül deposu kendi ekranlarını aynı araçlarla dener:

1. Orada bir deneme sunucusu projesi, örneğin `tests/Vyxum.Pro.E2E.Host`: `../vyxum/tests/Vyxum.E2E.Host/Vyxum.E2E.Host.csproj`
   ve modül projelerine başvurur, `Main(args) => E2EServer.RunAsync(args, ["Vyxum.Modules.Labels"])` çağırır. Modüle
   özel benzeticiler (sahte yazıcı) orada yeni `/e2e/...` uç noktaları olarak eklenebilir.
2. Kendi `playwright.config.ts` dosyası ve senaryoları: bu klasördeki `support/vyxum.ts`'yi içe aktarır, aynı bölümleri
   kullanır (`setup` kopyalanabilir; `core` modülün senaryolarıdır; lisanslar modülün kimliğiyle `licenses.get`'ten gelir).
3. `node ../vyxum/tests/e2e/run.mjs --host-project tests/Vyxum.Pro.E2E.Host/Vyxum.Pro.E2E.Host.csproj --config tests/e2e/playwright.config.ts`
