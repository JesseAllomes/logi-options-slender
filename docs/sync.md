# Settings sync

Slender can keep `config.json` in step across your PCs. You sign in with a **personal Microsoft account**; the file is stored in that account's OneDrive app folder (`OneDrive\Apps\<app name>\slender\config.json`). There is no server. Device pictures, status and logs are never synced.

Sign-in is off until you do the one-time setup below. The settings app says "Sign-in isn't set up yet" until then.

## One-time setup (about five minutes)

You create a free app registration on your own Microsoft account. The client ID it gives you is not a secret.

1. Go to <https://portal.azure.com> and sign in with your **personal** Microsoft account (the one whose OneDrive you want to use). If asked, accept the free account sign-up; no subscription or card is needed.
2. Search for **App registrations** and open it, then **New registration**.
3. Name: `Slender`. Supported account types: **Personal Microsoft accounts only**. Leave the redirect URI empty for now. Register.
4. Open **Authentication** (or **Authentication (Preview)**), **Add a platform**, choose **Mobile and desktop applications**, tick or add the redirect URI `http://localhost`, and save. Make sure **Allow public client flows** is **Yes**.
5. Open **API permissions**, **Add a permission**, **Microsoft Graph**, **Delegated permissions**, search `Files.ReadWrite.AppFolder`, tick it, and add it. (No admin consent is needed for a personal account.)
6. Open **Overview** and copy the **Application (client) ID**.
7. Create `slender.sync.json` in `%LOCALAPPDATA%\Slender` (it survives updates), with:

   ```json
   {
     "clientId": "paste-the-application-client-id-here"
   }
   ```

   A copy next to `Slender.Tray.exe` also works, but the installer replaces that folder on update, so prefer `%LOCALAPPDATA%\Slender`.
8. Restart Slender, open Settings, General, **Sync**, and choose **Sign in with Microsoft**. Your browser opens; approve access to the Slender app folder.

Do the same on each PC (same client ID, same Microsoft account).

## How syncing works

`config.json` is the unit. After each sync, `%LOCALAPPDATA%\Slender\sync.json` records the account, the remote eTag and the hash of the file at that time.

| This PC changed | Cloud changed | What happens |
| --- | --- | --- |
| no | no | nothing |
| no | yes | the cloud copy replaces this PC's file |
| yes | no | this PC's file is uploaded |
| yes | yes | this PC's version is kept and uploaded; the cloud copy is saved beside it as `config.conflict-<timestamp>.json`, and Settings shows "Settings were changed on another PC; kept this PC's version." |

A new PC that has no `config.json` takes the cloud copy. First sign-in on a PC that already has a different cloud copy counts as a conflict (this PC wins, the cloud copy is backed up). Identical content is simply adopted.

The tray syncs at start, and a few seconds after you change a setting. It does nothing when you are signed out, and quietly retries later when offline. **Sync now** in Settings forces a pass.

## Privacy

- Scope is `Files.ReadWrite.AppFolder`: Slender can only see its own app folder in your OneDrive, not your other files.
- The sign-in token cache is encrypted with Windows DPAPI and stored in `%LOCALAPPDATA%\Slender\msal-token-cache.bin`. Nothing secret is written to the repository or to `slender.sync.json`.
- **Sign out** removes the cached token and the sync record. Your settings stay on this PC and in OneDrive. To revoke access entirely, remove Slender at <https://account.microsoft.com/privacy/app-access>.

## Self-update (optional)

By default the tray looks in the `artifacts\releases` folder of the repo it was built from (`scripts\Build-Setup.ps1` bakes the path in), so shipping an update is just running `.\scripts\Build-Setup.ps1` again. Add `"updateSource"` to `slender.sync.json` only to override that: a folder path (or UNC share) holding the release files, or a GitHub repository URL. A build without the baked-in path (a dev build) and no `updateSource` checks nothing.

The tray checks 30 seconds after start, every 6 hours, and on demand (tray menu **Check for updates**, Settings, General **Check now**). A new version downloads in the background; a balloon and a **Restart to update** menu item then apply it at once, or it installs silently the next time Slender exits. Details in `docs/install.md`.
