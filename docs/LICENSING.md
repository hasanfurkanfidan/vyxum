# Vyxum licensing

[Türkçe](LISANS.md)

This guide explains what a license file is and how to load it into Vyxum. No technical knowledge is needed.

## 1. What is free

**Weighing** (Tartım), **Products** (Ürünler) and **Records** (Kayıtlar) are free. They need no license and work fully without one.

A license only opens the paid modules. You can see which modules are paid, and their status, on the **Settings**, **Modules and license** screen.

## 2. What a license file is

A license file is a small file with the extension `.vxlic`. You receive it from us or from your reseller. The file states:

- the company name of the license holder;
- the modules it opens;
- the number of stations: how many devices (scales) may use the paid modules;
- the validity: the start and end dates, or that the license has no end date;
- the license type and the license number.

Do not open the file and edit it. A changed file is invalid and is not accepted.

## 3. Loading a license file

**In the setup wizard.** The wizard has five steps. The **License** step is the fourth one: after Company and before Products.

1. On the **License** step, select **Load a license file**.
2. Click **Choose file** and select the `.vxlic` file.
3. When the file is loaded, you see "License loaded for ..." with the company name.
4. Click **Next**.

If you do not have a license file, keep **Continue free** selected and go on. You can load a license later.

**After the setup.** Open **Settings**, go to **Modules and license**, and use **Upload license file** to select and load the `.vxlic` file. You do not need to reinstall Vyxum to open a new module.

## 4. License status

The **License** section of the **Modules and license** screen shows the status of the license in one line:

| Status | Meaning |
|---|---|
| signature verified | The file came from us and was not changed. The license is valid. |
| not valid yet | The license period has not started. It opens on the start date. |
| expired, {N} days of grace left | The license period has ended. Paid modules keep working for {N} more days. |
| expired, read-only | The grace period has ended too. Paid modules are read-only. |
| no license | No license is loaded. Only the free parts work. |
| invalid: {reason} | The file was not accepted. The reason is shown on the line (see below). |

**Reasons a file is invalid:**

- **The signature does not match: the file was changed or damaged.** Ask for a new file.
- **This is not a Vyxum license file.** Check that you selected the file we sent you.
- **The file is too large to be a license file.** The file may be damaged. Ask for a new file.
- **The contents of the license file are incomplete or unreadable.** Ask for a new file.
- **This license file was made for a newer version of Vyxum.** Update Vyxum, or ask for a file in the current format.
- **The file was signed with a key this version of Vyxum does not know.** Update Vyxum, or contact us for help.
- **The file could not be read.** Check that another program does not have the file open, and try again.

## 5. What happens when the license expires

1. **When the period ends, a 30-day grace period starts.** Paid modules keep working during this time. A strip at the top of the screen shows: "License expired; paid modules keep working for N more days." The **License settings** link next to it takes you to the license screen.
2. **When the grace period ends, paid modules become read-only.** The strip then reads "License term ended; paid modules are read-only." You cannot add new records or make changes in the paid modules.
3. **Your data is never deleted.** Existing records, products and settings stay as they are.
4. **Weighing, Products and Records always work.** They keep working even after the license has expired.

When you load a new license file, the paid modules open again straight away.

## 6. Stations

A **station** is one device (scale). The license states how many stations it allows.

1. First add the scales under **Settings**, **Devices**.
2. On the **Modules and license** screen, in the **Stations** section, choose the devices that may use the paid modules.
3. Click **Save stations**. "Stations saved." appears.

A device that is not chosen shows the state **off**. It does not use the paid modules, but you can still weigh on it. **Weighing itself is never limited by the number of stations.**

If you choose more devices than the license allows, the choice is not saved: "The license allows fewer stations than chosen." Clear the choice of one device and save again.

## 7. Removing a license

1. On the **Modules and license** screen, click **Remove license**.
2. In the question that appears, click **Remove**. To keep the license, click **Cancel**.

When the license is removed, the paid modules are locked again. **Your records are not deleted.** You can load a license again at any time, with the same file or a new one.

## 8. No internet needed

The license file is checked on your computer, without an internet connection. Vyxum does not contact any server to check the license. Loading and using a license does not need an internet connection.

## 9. The computer's clock

Vyxum watches the clock of the computer. If the clock is behind a time that Vyxum has already seen, a warning appears: "This computer's clock is behind a time Vyxum has already seen." The license period is counted from the latest time Vyxum has seen, so setting the clock back does not extend the license. Set the clock to the correct time.

## 10. How to get a license

To get a license, contact us or your reseller.
