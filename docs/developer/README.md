# Vyxum developer guide / Vyxum geliştirici rehberi

**English** · [Türkçe](#türkçe)

This guide is for a developer who has **never seen Vyxum**. It assumes you know basic C#/.NET and web development, and nothing about this project. Following it you can understand the code, build, run and test it, change the database, add a scale protocol or a module, and produce a release. It was written so the project can be continued without its original authors.

Read the chapters in order the first time. Later, use them as a reference.

| # | English | Türkçe |
|---|---|---|
| 1 | [The big picture](en/01-big-picture.md) | [Genel bakış](tr/01-genel-bakis.md) |
| 2 | [Getting started](en/02-getting-started.md) | [Başlarken](tr/02-baslarken.md) |
| 3 | [Repository map](en/03-repository-map.md) | [Depo haritası](tr/03-depo-haritasi.md) |
| 4 | [Modules and events](en/04-modules.md) | [Modüller ve olaylar](tr/04-moduller.md) |
| 5 | [Database and migrations](en/05-database.md) | [Veritabanı ve geçişler](tr/05-veritabani.md) |
| 6 | [Devices and the Agent](en/06-devices-and-agent.md) | [Cihazlar ve Agent](tr/06-cihazlar-ve-agent.md) |
| 7 | [Web UI](en/07-web-ui.md) | [Web arayüzü](tr/07-web-arayuzu.md) |
| 8 | [Installer and releases](en/08-release.md) | [Kurulum dosyası ve sürüm çıkarma](tr/08-surum.md) |
| 9 | [Troubleshooting](en/09-troubleshooting.md) | [Sorun giderme](tr/09-sorun-giderme.md) |
| 10 | [Decisions and reasons](en/10-decisions.md) | [Kararlar ve gerekçeleri](tr/10-kararlar.md) |

Other documents: [`CLAUDE.md`](../../CLAUDE.md) (non-negotiable rules), [README](../../README.md) (configuration keys and HTTP API), [CONTRIBUTING](../../CONTRIBUTING.md), [Agent README](../../src/Agent/Vyxum.Agent/README.md), [installer README](../../installer/README.md), user installation guide [INSTALL](../INSTALL.md).

## Quick answers

- *I want to build and run it:* [Chapter 2](en/02-getting-started.md).
- *Where is the code for X?* [Chapter 3](en/03-repository-map.md).
- *I want to add a module / a scale protocol / a migration:* chapters [4](en/04-modules.md#add-a-new-module-checklist), [6](en/06-devices-and-agent.md#add-a-new-protocol-step-by-step), [5](en/05-database.md#add-a-migration).
- *I want to ship a version:* [Chapter 8](en/08-release.md#how-to-release-a-new-version).
- *Something is broken:* [Chapter 9](en/09-troubleshooting.md).

## Keeping this guide alive

- A change that alters a command, a path, a rule or a setting updates the guide **in the same pull request**, in both languages (rule V-23).
- When a new phase adds a feature area (licensing, labels, users...), add a chapter file to both `en/` and `tr/`, add a row to the table above, and add its decisions to chapter 10. Add new chapters at the end (11, 12, ...) so existing links stay valid.
- Verify every command and path you write by running it or reading the code. Do not guess.
- Open core only: never put prices, sales plans, customer information or secrets in this repository.

---

## Türkçe

Bu rehber Vyxum'u **hiç görmemiş** bir geliştirici içindir. Temel C#/.NET ve web bilgisi olduğunu, bu proje hakkında hiçbir şey bilmediğinizi varsayar. Rehberi izleyerek kodu anlayabilir, derleyip çalıştırıp test edebilir, veritabanını değiştirebilir, terazi protokolü ya da modül ekleyebilir ve sürüm çıkarabilirsiniz. Proje, ilk yazarları olmadan da sürdürülebilsin diye yazılmıştır.

İlk okumada bölümleri sırayla okuyun; sonra başvuru kaynağı olarak kullanın. Bölümler yukarıdaki tabloda Türkçe adlarıyla listelenmiştir.

**Hızlı cevaplar:** derleyip çalıştırmak için [2. bölüm](tr/02-baslarken.md); kodun yeri için [3. bölüm](tr/03-depo-haritasi.md); modül, protokol ya da geçiş eklemek için [4](tr/04-moduller.md), [6](tr/06-cihazlar-ve-agent.md), [5](tr/05-veritabani.md); sürüm çıkarmak için [8. bölüm](tr/08-surum.md); bir şey bozulduysa [9. bölüm](tr/09-sorun-giderme.md).

**Rehberi güncel tutmak:** bir komutu, yolu, kuralı ya da ayarı değiştiren her değişiklik rehberi **aynı çekme isteğinde**, iki dilde günceller (V-23). Yeni bir faz yeni bir özellik alanı getirirse (lisanslama, etiket, kullanıcılar...) `en/` ve `tr/` içine birer bölüm dosyası ekleyin, yukarıdaki tabloya satır ekleyin, kararlarını 10. bölüme yazın. Yeni bölümleri sona (11, 12, ...) ekleyin; mevcut bağlantılar bozulmaz. Yazdığınız her komutu ve yolu çalıştırarak ya da koddan okuyarak doğrulayın. Bu açık depoya fiyat, satış planı, müşteri bilgisi ya da gizli anahtar konmaz.
