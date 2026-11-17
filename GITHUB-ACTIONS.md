# GitHub Actions: Windows build

1. Create a **private** GitHub repository, e.g. `DevBridge`.
2. Upload **the contents of this DevBridge folder** to repository root, including `.github/workflows/build.yml`.
3. Open **Actions > Build DevBridge for Windows > Run workflow** (branch `main`). Push to `main` also triggers a build.
4. Open the completed run and download **DevBridge-win-x64** under **Artifacts**. Extract the downloaded artifact, then extract `DevBridge-win-x64.zip`.
5. Run `DevBridge.exe --open` from PowerShell or open `http://127.0.0.1:5198` after launching `DevBridge.exe`. Windows SmartScreen may show an unsigned-app warning.

For a persistent downloadable release, go to **Releases > Draft a new release**, create tag `v0.1.0`, publish it, and wait for the tag-triggered build. Find the ZIP on the release page.

The GitHub runner has Node.js and .NET SDK; your work PC needs neither. ADB is not bundled; specify the full path to adb.exe in DevBridge settings. The workflow is untested until GitHub runs it.

**Important:** do not commit real API tokens, passwords, internal Swagger documents or private production endpoints. The app's API-proxy and trace-ingestion endpoints are for local trusted use only.
