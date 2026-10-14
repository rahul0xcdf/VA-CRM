# Installing Vinayaka Agencies on another computer, and keeping backups

This guide covers three things: putting the software on a new computer, moving your existing books
to it, and the backups that mean a dead disk is an inconvenience rather than a disaster.

---

## Part 1 — Installing on a new computer

### What you need

- A Windows 10 or Windows 11 computer, 64-bit.
- The file `VinayakaAgencies-Setup.msi`, on a USB stick or a shared folder.
- An administrator account on that computer, or somebody who can type the password.

**You do not need an internet connection**, and you do not need to install .NET or any other
prerequisite first. Everything the software needs is inside the installer.

### Steps

1. Copy `VinayakaAgencies-Setup.msi` to the new computer. Run it from the computer's own disk rather
   than from the USB stick.
2. **Windows will warn you.** You will see a blue box saying *"Windows protected your PC"* or
   *"Microsoft Defender SmartScreen prevented an unrecognised app from starting"*.

   This is expected and is not a sign of a problem. The installer is not signed with a purchased
   code-signing certificate, so Windows has no publisher name to show you. Click **More info**, then
   **Run anyway**. You will only see this once per computer.

3. Click through the wizard: welcome, the licence notice, and the install location. The default
   location is `C:\Program Files\Vinayaka Agencies` and there is normally no reason to change it.
4. Click **Install** and approve the Windows administrator prompt.
5. The installer finishes and puts **Vinayaka Agencies** on the Start Menu and on the desktop.

### The first launch

1. Start it from the desktop shortcut.
2. If this computer has never run the software, you are asked for the **company details** — the
   legal name, the state code, and the GSTIN. These end up on filed returns, so type them carefully.
3. You are then asked to create the **owner account**: a user name and a password of at least eight
   characters. Write the password down somewhere safe. There is no way to recover it — that is
   deliberate, because a password that can be recovered by anyone is not a password.
4. The books open.

> **Important: this is a new, empty set of books.** Installing on a second computer does not copy
> anything from the first. If you want your existing books on this computer, stop here and do Part 2
> instead of entering anything.

### A note about two computers

This install keeps its books in a file on the computer it is installed on. **Two computers running
this software have two separate sets of books** — they do not see each other and they do not merge.

Billing from two counters at once, sharing one set of books over the office network, is a separate
piece of work (Phase 9's SQL Server step) and it is **not finished yet**. If that is what you need,
say so, because it changes what has to be built and needs SQL Server Express installed on the main
billing PC.

What you *can* do today:

- Run the software on a second computer as a **separate** install (a second shop, or a spare).
- Move your books from one computer to another, using Part 2.

---

## Part 2 — Moving your existing books to the new computer

Use this when the new computer should carry on from where the old one left off.

### On the old computer

1. Open the software and sign in.
2. Press **Alt+G**, type `Backup`, and press Enter — or go to **Gateway → Administration →
   Backup & Restore**.
3. Press **Back up now** and wait for the message *"Backed up and verified"*.

   "Verified" matters. It means the software reopened the file it had just written, checked every
   part of it against a recorded fingerprint, and opened the database inside it to confirm it is a
   working database. A backup that cannot say "verified" should not be relied on.

4. Note the folder shown on screen under **Where your books and backups are**. It will be
   `C:\ProgramData\VinayakaAgencies\backups`.
5. Copy the newest `vinayaka-backup-*.zip` from that folder onto your USB stick.

### On the new computer

1. Install the software (Part 1) and create the company and owner account as prompted. It does not
   matter much what you type for the company — the restore will replace it.
2. Copy the backup file into `C:\ProgramData\VinayakaAgencies\backups` on the new computer.
3. Open **Backup & Restore** and press **Refresh**. Your backup appears in the list.
4. Select it and press **Verify**. Do not skip this. If it says anything other than *"This backup is
   intact"*, stop and go back to the old computer.
5. Press **Restore…**, read the warning, then press **Confirm restore**.
6. You will see *"Restored. The trial balance and stock summary match the backup exactly."* That
   sentence is the software telling you it compared the restored books against the originals and
   found them identical, line for line.
7. **Close the software and open it again.** This is required — what is on screen is still from the
   old, empty books until you do.
8. Check your trial balance and your stock summary against the old computer. They should match.

Keep the old computer's books untouched until you have done step 8.

---

## Part 3 — Backups, from now on

### What happens without you doing anything

- A backup is taken **every time you close the software normally**, and verified.
- The newest **30** backups are kept. Older ones are removed automatically.
- The most recent backup is **never** removed, whatever the settings say.

A backup covers the database, your invoice templates, your logo and your settings. Restoring one
brings all of it back.

### The one thing you should change

By default, **every copy of your books is on this one computer.** Fire, theft or a failed disk takes
them all. The Backup & Restore screen says so in orange, and it will keep saying so until you fix
it.

To fix it, set a second destination — a network folder, or a USB drive you leave plugged in. Edit
`C:\ProgramData\VinayakaAgencies\appsettings.Local.json` (create it if it is not there):

```json
{
  "Backup": {
    "SecondDestination": "D:\\VinayakaBackups"
  }
}
```

Restart the software. Every backup from then on is written to both places. If the drive is unplugged
or the share is down, the software still takes the local backup and tells you the second copy
failed — it will never lose the copy it *can* make because of the one it cannot.

**Best practice for a small shop:** a USB drive that lives in the shop during the day and goes home
in somebody's pocket at night. That is a genuine off-site backup and costs nothing.

### Please read this about security

**The database is not encrypted.** Anyone who gets hold of the file, or of a backup of it, can read
every customer, every price and every voucher. Your GST portal credentials and user passwords inside
it *are* protected and cannot be read.

This was a deliberate choice. Encrypting the whole database would protect against a stolen computer,
but it would also mean that a forgotten passphrase is indistinguishable from a dead disk — and your
books must be kept for eight years by law. Losing them to a forgotten password would be worse than
the risk being avoided.

The better protection, which Windows already gives you:

- Turn on **BitLocker** on the computer's disk (Settings → Privacy & security → Device encryption).
  Windows escrows the recovery key against your Microsoft account, so it cannot be lost the way a
  passphrase can.
- **BitLocker-encrypt the backup drive** if it leaves the premises.

If you would rather have the database encrypted anyway, despite the recovery risk, say so — it is a
change worth making *before* there are backups in circulation, not after.

---

## Part 4 — Upgrading to a newer version

1. Take a backup first. Always.
2. Run the new `VinayakaAgencies-Setup.msi`. It replaces the old version in place; you do not
   uninstall first.
3. **Your books are not touched.** They live in `C:\ProgramData\VinayakaAgencies`, which the
   installer does not own and never modifies.
4. Start the software. If the new version needs to change the shape of the database, you will see a
   **Database upgrade required** window listing what will be applied. Make sure nobody else is using
   the software, then confirm. This is one operator's deliberate act by design — it is not done
   quietly while people are launching.

### Upgrading from a version before this one

Versions before this one kept the books under your Windows user profile rather than in a shared
location, which meant a second Windows account on the same computer would have seen a completely
empty set of books. The upgrade fixes that: on the first launch after upgrading, the software moves
your books to the shared location, logs what it moved, and leaves a note called
`moved-to-programdata.txt` in the old folder explaining where they went.

You do not have to do anything. If the software finds books in *both* places — which can happen if
two Windows accounts each used the old version — it moves nothing, keeps both, and records a warning
in the log naming both files. Ask for help at that point rather than deleting either one.

---

## Part 5 — Uninstalling

Settings → Apps → **Vinayaka Agencies** → Uninstall.

**Your books and backups are left alone.** `C:\ProgramData\VinayakaAgencies` is not removed by the
uninstaller, by design. If you genuinely want the books gone you must delete that folder yourself —
and take a copy first.

---

## When something goes wrong

Open **Backup & Restore** and press **Collect diagnostics**. It writes one file to
`C:\Users\<you>\AppData\Local\VinayakaAgencies\diagnostics` containing the recent logs, any crash
reports, your settings and a summary of the computer.

Credentials and passwords are removed from that file before it is written, so it is safe to send.
Attach it to an email to whoever is helping you, and say what you were doing at the time — that
sentence is often worth more than everything else in the file.
