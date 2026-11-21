# Installing Vyxum

[Türkçe](KURULUM.md)

This guide is for the person who installs Vyxum on a Windows computer. No technical knowledge is needed. The installation takes about 15 minutes.

## 1. What you need

- A computer with Windows 10 or Windows 11, 64-bit.
- An administrator account on that computer. Windows asks for confirmation during the installation.
- The scale, connected with a serial cable or a USB-serial cable, or a scale on the network (its IP address and port).

That is all. Vyxum does not need an internet connection, a database server or any other program. The installation does not download anything.

## 2. Install

1. Double-click the file `VyxumSetup-<version>.exe` that you received.
2. Windows may show the message **"Windows protected your PC"**. This happens because the setup file is not digitally signed yet. It is safe to continue: click **More info**, then **Run anyway**.
3. Windows asks whether the app may make changes to the computer. Click **Yes**.
4. Follow the installer. It normally needs no choices from you. It does the following:
   - copies Vyxum to `C:\Program Files\Vyxum`;
   - installs two Windows services, **Vyxum Web** and **Vyxum Agent**. They start by themselves every time Windows starts;
   - creates a **Vyxum** icon on the desktop and in the Start menu.
5. At the end, if the **Open Vyxum** box is ticked, Vyxum opens. Click **Finish**.

If the installer asks for a language, choose Türkçe or English.

## 3. First start: the setup wizard

The first time Vyxum opens, a setup wizard appears. It has five steps: **Database**, **Scale**, **Company**, **License** and **Products**. Use **Next** to go on and **Back** to return.

The wizard can only be run on the computer where Vyxum is installed.

**Step 1: Database.** The question is "Where should records be stored?" Keep **Store on this computer (recommended)** selected and click **Next**. Nothing else is needed.

Only if your company has a SQL Server, open **Advanced: SQL Server**, enter the connection string that your IT department gives you and click **Test connection**. You can go on only after the test succeeds.

**Step 2: Scale.** Vyxum looks for the scale by itself. Keep the scale switched on and connected. Vyxum tries every serial port with the usual settings. It sends only read commands to the scale. It never changes the scale's settings.

- When the scale is found, put something on the scale. If the weight on the screen changes, click **This is the right scale**.
- If no scale is found, check the cable and the power of the scale, then click **Search again**.
- If it still does not work, click **Set up manually** and enter the settings from the scale's menu. For a network scale, enter its IP address and port.
- You can click **Skip** and add the scale later under Settings, Devices.

**Step 3: Company.** Type the company name and click **Next**. The name appears on the screens and in the reports.

**Step 4: License.** If you have a license file, select **Load a license file**, then use **Choose file** to select the `.vxlic` file. If you do not have one, keep **Continue free** selected. Weighing, Products and Records work without a license. For details, see the [licensing guide](LICENSING.md).

**Step 5: Products.** Add the first products you will weigh, or click **Skip**. You can add products later on the Products screen.

At the end you see **Setup complete**. Click **Go to weighing** to start weighing. The wizard does not open again. Later changes are made on the Settings screen.

## 4. Daily use

- Double-click the **Vyxum** icon on the desktop. The Vyxum window opens. It has no address bar.
- Press **F11** to switch between full screen and the normal window.
- The Vyxum services run in the background. Leave the computer on during working hours.

When Vyxum starts, it shows "Starting Vyxum…" for a few seconds.

## 5. Settings

Open **Settings** in Vyxum.

- **Devices:** add a new scale with **Add scale**, or change an existing one with **Edit**. You can run a live test there: put something on the scale and check that the weight changes.
- **About:** shows the version, the company, the data folder and the date of the last backup.

## 6. Update

Vyxum is not updated from the internet. To update, use the setup file of the new version:

1. Double-click the new `VyxumSetup-<version>.exe` and follow the steps of section 2.
2. The installer finds the installed version and stops the Vyxum services.
3. It makes a backup of the database automatically, in `C:\ProgramData\Vyxum\backups`.
4. It replaces the program files and starts the services again.

Your records and settings are kept. The backups folder keeps the last 10 backups.

## 7. Uninstall

1. Open **Settings**, then **Apps**, then **Installed apps**.
2. Click **Vyxum**, then **Uninstall**.
3. At the end, Vyxum asks: "Do you also want to delete the database, settings and backups?" The default answer is **No**, and your data is kept. Click **Yes** only if you want to delete all records. This cannot be undone.

## 8. Troubleshooting

**The scale is not found.**
- Check that the scale is switched on and the cable is firmly plugged in at both ends.
- Close other programs that use the same port, for example the scale's own software.
- If you use a USB-serial adapter, install the driver of the adapter first. The adapter maker provides it.
- Click **Search again**, or use **Set up manually**.

**"Starting Vyxum…" stays on the screen.**
- Wait one minute. If Vyxum still does not start, it shows a message. Click **Try again**.
- Restart the computer. This solves the problem in most cases.

**Where are my records and settings?**
All data is in the folder `C:\ProgramData\Vyxum`:
- `vyxum.db`: the records (the database);
- `vyxum.json`: the settings written by the setup. You do not need to edit this file;
- `backups`: the automatic backups.

Do not delete this folder unless you want to remove all data.

**"Windows protected your PC" appears again.** This is normal until the setup file is digitally signed. See section 2.
