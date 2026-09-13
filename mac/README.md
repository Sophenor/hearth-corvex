# Hearth for Mac

A separate native Mac implementation of Hearth, using AppKit, WKWebView, Foundation and Security/Keychain. The Windows source and release are independent.

**Preview:** macOS 13+, universal Apple Silicon (`arm64`) and Intel (`x86_64`). Internet access and the recipient's own Corvex API key are required. No Electron, local model, .NET runtime or developer tools are needed by recipients.

[Installation and family setup guide](START%20HERE%20-%20Mac.txt)

## Behaviour and limits

- Local saved conversations, search, model selection, Markdown, light/dark themes, export and archiving.
- Direct HTTPS requests to `https://api.tokenfactory.corvex.cloud/v1`. Available models and optional prices come from its live catalogue. Redirects are rejected so the API key cannot follow a redirect to another host.
- API key stored as a Keychain generic password with service `com.sophenor.hearth.corvex`. No bundled account, environment-key import or key in chat exports.
- Conversation JSON and preferences under `~/Library/Application Support/HearthCorvex`, with owner-only permissions and a previous-file backup. Interrupted replies are marked and are not automatically restarted. Chats are not encrypted or cloud-synced.
- A bounded tool loop: at most eight tool calls and nine provider requests per turn, 8,192 output tokens per request, ten-minute overall limit, cancellation and cumulative reported usage. Estimated prices are not the provider's billing statement. Replies are shown when complete, without token streaming.
- Computer help is off initially. After consent it reads a diagnostic snapshot of OS, physical RAM, load averages, home-volume free space and visible apps. A fixed read-only `/bin/ps` invocation supplies resource samples; the model cannot supply shell commands. A normal quit requires separate native approval and fresh app identity checks. No force quit.
- Folder access is opt-in for one folder. Text reads are limited to 100 KB; path traversal, symlinks and known credential paths are blocked. This is not a general secret detector: choose folders carefully. Text saves are restricted to `.txt`, `.md`, `.csv`, require native approval of full content/path, and back up replaced files. No deletion tools.
- macOS Dictation integration uses the system feature and its configured shortcut. It is not a bundled speech model and availability/processing depends on macOS settings.
- No web search, PDF/Office/image parsing, email sending, arbitrary commands or general desktop control. The AI cannot access files unless the folder tool is enabled. Important answers still require human judgment.

## Build

On a Mac with Xcode Command Line Tools and Python 3:

```sh
bash mac/build.sh
mac/build/Hearth.app/Contents/MacOS/Hearth --self-test --report "$PWD/mac/build/reports/core.json"
mac/build/Hearth.app/Contents/MacOS/Hearth --ui-test --report "$PWD/mac/build/reports/ui/report.json"
```

`build.sh` compiles both architectures with a macOS 13 deployment target, bundles all UI resources, creates a universal executable and icon, verifies an ad-hoc code signature, and packages `Hearth-Mac.zip` with a SHA-256 checksum. It does not build or edit Windows files. Ad-hoc signing is **not** Developer ID signing or Apple notarization; see the setup guide for the per-app first-open process.

The Mac-only GitHub workflow builds on an Apple Silicon macOS runner, executes native core and WebKit UI checks, then downloads and tests the **same ZIP** on an Intel macOS runner. Test fixtures use temporary folders and a uniquely named Keychain item. They do not use a real Corvex account or close user applications. UI screenshots use explicitly synthetic chat/model data. Passing these tests does not establish live provider compatibility, dictation accuracy or first-open behaviour on every macOS version.

## Third-party code

- Marked **18.0.13**, MIT; vendored `marked.js` SHA-256 `b147274a9ce27d17276587167e49483d719f6893eeca3a3667a59797661d3556`.
- DOMPurify **3.4.15**, Apache-2.0 OR MPL-2.0; vendored `purify.js` SHA-256 `f263b05369e050fa175d4ecb9c9358eb4253602d510297adfb31df48b2f1c4d5`.
- Hearth code: MIT, same as the repository.

Complete notices are in `Resources/licenses` and included inside the app. `prepare_resources.py` embeds the local JavaScript and hashes each inline script for the content security policy. Rendered model HTML is sanitized; scripts, remote images, frames, forms and unsafe links are excluded. The native bridge accepts messages only from the app's main bundled page and validates enabled tools independently of model instructions.
