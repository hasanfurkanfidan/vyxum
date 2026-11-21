# 2. Başlarken

[Dizin](../README.md#türkçe) · Önceki: [1. Genel bakış](01-genel-bakis.md) · Sonraki: [3. Depo haritası](03-depo-haritasi.md)

Hedef: boş bir bilgisayardan çalışan, test edilmiş bir Vyxum'a.

## Ne kurulmalı?

| Araç | Sürüm | Neden |
|---|---|---|
| .NET SDK | 10 (`global.json` `10.0.100` sürümünü sabitler, daha yeni özellik bantlarını kabul eder) | .NET tarafındaki her şeyi derler ve çalıştırır. |
| Node.js | 22 | Web arayüzünü derler. Yalnızca geliştiricilere gerekir, müşteriye gerekmez. |
| Git | herhangi | |
| Docker | isteğe bağlı | Yalnızca SQL Server testlerini yerelde çalıştırmak için. |
| Inno Setup 6 | yalnızca Windows'ta, yalnızca kurulum dosyası için | Bkz. [8. bölüm](08-surum.md). |

Kontrol: `dotnet --version` `10.x`, `node --version` `v22.x` yazar.

Kurulum dosyası ve Masaüstü penceresini çalıştırmak (yalnızca Windows) dışındaki her şey Linux ve macOS'ta da olur. Masaüstü projesi Linux'ta yine de *derlenir*.

## Kodu alın ve derleyin

```bash
git clone https://github.com/hasanfurkanfidan/vyxum.git
cd vyxum
dotnet build -warnaserror
```

`-warnaserror` şarttır: `Directory.Build.props` uyarıları zaten hata yapar (`TreatWarningsAsErrors`) ve derleme uyarısız olmalıdır. Paket sürümleri yalnızca `Directory.Packages.props` içindedir (merkezi paket yönetimi); hiçbir `.csproj` içine sürüm numarası yazmayın.

`src/Host/Vyxum.Host/wwwroot` klasörü yoksa ilk `dotnet build` web arayüzünü de derler (`npm ci` ve `npm run build` çalıştırır). Bunun için Node.js gerekir. Atlamak için:

```bash
dotnet build -warnaserror -p:SkipUiBuild=true
```

CI .NET işinde bu bayrağı kullanır, arayüzü ayrı bir işte derler.

## Testleri çalıştırın

```bash
dotnet test
```

Testler **xUnit v3** ve doğrulamalar için **Shouldly** kullanır (FluentAssertions yasaktır). `global.json`, xUnit v3'ün kullandığı `Microsoft.Testing.Platform` çalıştırıcısını seçer.

Her veri erişimi testi **SQLite** ve **SQL Server** üzerinde çalışır. SQLite hiçbir şey istemez. `VYXUM_TEST_SQLSERVER` ortam değişkeni **veritabanı adı içermeyen** bir bağlantı dizesi taşımıyorsa SQL Server durumları *bir gerekçeyle atlanır*. Her test kendine ait, benzersiz adlı bir veritabanı oluşturur ve sonunda siler. Yerelde SQL Server için:

```bash
docker run -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Vyxum_Test_123!' \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

export VYXUM_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=Vyxum_Test_123!;TrustServerCertificate=True'
dotnet test
```

(PowerShell: `$env:VYXUM_TEST_SQLSERVER = '...'`.) Bu parola yalnızca yerel testler içindir. CI aynı değişkeni bir SQL Server 2022 servis kapsayıcısıyla ayarlar (`.github/workflows/ci.yml`).

Test yardımcıları `src/Platform/Vyxum.Platform.Testing` içindedir: `TestDatabase` (test başına bir veritabanı, iki sağlayıcıdan biri), `TestModuleHost` (bellek içi modül sunucusu) ve `TestClock`.

Web arayüzü testleri ayrıdır: [7. bölüm](07-web-arayuzu.md).

## Web sunucusunu çalıştırın

```bash
dotnet run --project src/Host/Vyxum.Host --urls http://localhost:5000
```

5000 portunu kullanın: arayüz geliştirme sunucusu ve agent varsayılanları bunu bekler. Ayar gerekmez. İlk açılışta sunucu veri klasöründe `vyxum.db` SQLite dosyasını ve tüm tabloları oluşturur (böyle başlatınca `src/Host/Vyxum.Host/data/`). Kontrol:

```bash
curl http://localhost:5000/health
# {"status":"ok","modules":[{"id":"products",...},{"id":"weighing",...}],"database":"Sqlite"}
```

Tarayıcıda http://localhost:5000 adresini açın. İlk seferde **kurulum sihirbazı** çıkar.

## Simüle edilmiş terazi ile Agent çalıştırın

Gerçek teraziye gerek yok. İkinci bir terminalde:

```bash
dotnet run --project src/Agent/Vyxum.Agent
```

`dotnet run`, agent'ı *Development* ortamında başlatır; `appsettings.Development.json` 572 g gösteren **simüle** bir MT-SICS terazisi ekler. Görmek için http://localhost:5000/api/devices adresini, canlı ağırlık için tartım ekranını açın.

Gerçek terazi için [6. bölüm](06-cihazlar-ve-agent.md).

## Web arayüzünde canlı yenilemeyle çalışın

```bash
cd src/Web/vyxum-web
npm ci          # bir kez
npm run dev     # Vite geliştirme sunucusu, genelde http://localhost:5173
```

Vite `/api` ve `/hubs` isteklerini `http://localhost:5000` adresine yönlendirir; sunucuyu açık tutun.

## Ayarlar tek yerde

Ayarlar şu sırayla okunur; **sonraki kazanır**: programın yanındaki `appsettings.json`, veri klasöründeki `vyxum.json`, ortam değişkenleri (`Vyxum__Database__Provider`), komut satırı (`--Vyxum:Database:Provider SqlServer`). Tam anahtar tablosu [README](../../../README.tr.md) içindedir. SQL Server'ı elle denemek için:

```bash
dotnet run --project src/Host/Vyxum.Host -- \
  --Vyxum:Database:Provider SqlServer \
  --Vyxum:Database:ConnectionString "Server=localhost,1433;Database=Vyxum;User Id=sa;Password=Vyxum_Test_123!;TrustServerCertificate=True"
```

## Her commit'ten önce

1. `dotnet build -warnaserror` hiç uyarı vermeden geçer.
2. `dotnet test` geçer (veri erişimine dokunduysanız `VYXUM_TEST_SQLSERVER` ayarlıyken).
3. Arayüze dokunduysanız: `npm run typecheck`, `npm test`, `npm run build`.
4. Bir commit = testleriyle birlikte çalışan tek bir adım. Commit mesajları İngilizce, emir kipinde ve kısadır ("Add product origin field").

Daha fazla kural [CONTRIBUTING.md](../../../CONTRIBUTING.md) ve [`CLAUDE.md`](../../../CLAUDE.md) içindedir.
