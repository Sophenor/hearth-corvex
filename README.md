# Hearth for Corvex

A friendly Windows chat app with saved conversations, powered by **your own Corvex Token Factory account**.

## [⬇ Download Hearth for Windows](https://github.com/Sophenor/hearth-corvex/releases/latest/download/Hearth-Windows.zip)

**Windows 10/11 · 64-bit Intel/AMD · approximately 50 MB · preview 0.1**

You do **not** need a GitHub account, an OpenAI subscription, or a local AI model to use the app.

### Get started

1. Click **Download Hearth for Windows** above.
2. Right-click the downloaded ZIP and choose **Extract All**.
3. Open **Hearth.exe** in the extracted folder.
4. Follow the app's guide: sign in to Corvex on its website, create an API key, then paste that key into Hearth once.
5. Start chatting. Your conversations are saved automatically on your computer.

Use **your own API key**, not your Corvex password. Never share the key with anyone else. If you have a Corvex invitation, complete that signup first; Hearth does not create accounts or grant free credits.

[Full setup and troubleshooting guide](START%20HERE.txt) · [Release and downloads](https://github.com/Sophenor/hearth-corvex/releases/latest)

![Hearth's Windows interface, shown with sample connection data](screenshots/welcome.png)

### What it can do

- Help draft emails, explain things, plan, and work through everyday questions.
- Keep conversations in a searchable sidebar, with light/dark themes and export.
- Switch between the models available through your Corvex account.
- With your permission, inspect basic computer performance and read small text files in a folder you choose.
- Ask before saving a text draft or requesting that an app close normally.
- Use installed Windows dictation where available, without a large speech-model download.

GLM 5.2 and Kimi K2.7 have been tested through Corvex, including actual file-tool use. New models appear when Corvex lists them; a listing alone does not guarantee tool compatibility.

### Before you use this preview

**This is an unsigned independent app, not an official Corvex product.** Windows may show an unknown-publisher warning. Download only from this repository or someone you trust, and do not disable your antivirus or Windows security protections. Signing and easier installation are future improvements.

The app needs Microsoft Edge WebView2 Runtime, which most recent Windows installations already have. If it is missing, Hearth offers Microsoft's installation page. Windows 11 x64 is the main target. This is not a Mac or native Windows ARM release.

Prompts and permitted tool results go directly to Corvex and use your own account's balance. Credit amounts, pricing and expiry are controlled by Corvex. Your API key is protected using Windows account encryption. Chats are ordinary local files under `%LOCALAPPDATA%\HearthCorvex`; they are not automatically synced to the cloud.

This version does not include live web search, PDF/Word/image attachments, email sending or unrestricted desktop automation. It never force-quits apps or deletes files. Voice availability and accuracy depend on Windows. Check important AI answers.

### Source and development

Hearth uses .NET 10, WinForms/WebView2, and Microsoft's open-source AI function-calling components. The executable bundles .NET, so recipients do not need developer tools.

- [Design, model connection and exact tool limits](docs/DEVELOPMENT.md)
- [Verification summary](docs/VERIFICATION.md)
- [Source licence](LICENSE.txt) and [third-party notices](THIRD-PARTY-NOTICES.txt)

To build from source on Windows, install the .NET 10 SDK and run `./build.ps1` from the repository folder. Source archives are available on the release page; ordinary users should download **Hearth-Windows.zip**, not GitHub's source-code ZIP.
