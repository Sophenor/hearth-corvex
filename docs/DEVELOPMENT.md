# Hearth for Corvex

A small Windows chat application for family and friends. Version 0.1 is a working
preview: local saved conversations, direct Corvex inference, and a bounded tool
loop with explicit local permissions. See **START HERE.txt** for recipient setup.

## Connection and current models

The API endpoint is `https://api.tokenfactory.corvex.cloud/v1`. Requests use each
person's own API key. The key is verified against `/models` before being saved
using Windows DPAPI for the current user. Hearth never imports the developer's
environment key into the normal UI and ships with no credentials or chats.

Verified against the actual account and model catalogue on 12 September 2026:

| API model ID | Input USD / million | Output USD / million |
| --- | ---: | ---: |
| `zai-org/GLM-5.2-FP8` | 0.75 | 2.40 |
| `moonshotai/Kimi-K2.7-Code` | 0.55 | 2.20 |

These are catalogue rates, not guaranteed future prices or billed balances.
GLM 5.3 and DeepSeek were not listed at inspection. The app refreshes the catalogue
on connection/startup and on request; it does not invent future IDs. Existing
conversations keep their selected model. Newly listed models need compatibility
testing before claiming their tools work reliably. Both currently listed models
passed an actual selected-file tool invocation through the production harness.

The documented [Corvex dashboard](https://tokenfactory.corvex.cloud/app) integration
uses an API key. No supported third-party password-login flow was found. The app
therefore opens Corvex's own [key page](https://tokenfactory.corvex.cloud/app/settings/keys)
and guides the user through one copy/paste. It does not collect Corvex passwords.

## Foundation choice

I evaluated [Cherry Studio](https://github.com/CherryHQ/cherry-studio) and
[Jan](https://github.com/janhq/jan), both substantial AGPL desktop projects. They
already offer broad AI functionality, but configuring their larger interfaces,
provider setup and agent permissions would expose more complexity than this
family-specific first version needs. No source was copied from either project.

Hearth reuses Microsoft's MIT-licensed
[Microsoft.Extensions.AI](https://github.com/dotnet/extensions) function-invoking
client and the MIT [OpenAI .NET client](https://github.com/openai/openai-dotnet)
for Corvex's OpenAI-compatible API. That SDK name does not mean requests go to
OpenAI: the endpoint is fixed to Corvex. The UI is our own small HTML/CSS interface
inside native WinForms/WebView2. No local language model or Python/Node service
runs on the recipient's machine. .NET is bundled. WebView2 is a Microsoft runtime,
not an open-source component; its redistribution terms are included separately.

Coding harnesses such as [OpenCode](https://github.com/anomalyco/opencode) and
[Codex](https://github.com/openai/codex) are valuable for coding, but their broad
shell-oriented capabilities are not necessary for this initial everyday helper.
This choice is based on the required Windows UX and tested API compatibility,
not evidence that any harness is universally best for GLM 5.2 or GLM 5.3.
Reference: [Microsoft function-calling documentation](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/use-function-calling).

## Implemented behaviour

- Local sidebar chats, title search, rename, archive, Markdown export, theme switch.
- Dynamic model list, current catalogue rates, per-turn observed token usage and estimates.
- Bounded tool loop: up to eight invocation iterations, serial tools, ten-minute
  task cancellation window, maximum output setting 8,192 tokens per request.
- One text-only final-answer continuation if a provider returns no final text;
  its reported usage is included. No automatic retry of HTTP/provider failures.
- Optional computer summary: sampled CPU, memory, fixed-disk free space, visible
  app names/PIDs/memory. CPU sampling is diagnostic evidence, not a full profiler.
- Graceful app close only after a fresh observation and native confirmation.
  No force-kill or closing protected Windows components.
- Scoped listing and small text-file reads (100 KB) after folder opt-in.
- Approved `.txt`, `.md` and `.csv` saves, up to 100,000 characters, with unique
  previous-version backups. No arbitrary shell, executable writes or deletion.
- Installed Windows speech recognition, with a Win+H fallback explanation.
- User messages are saved before inference. Interrupted work is marked, not
  automatically resumed. Recent whole turns enter context; older chats stay saved.

## Important limits

This is not a complete ChatGPT/Codex replacement. It has no web-search provider,
PDF/Office/image ingestion, email integration, screenshots, unrestricted desktop
control, scheduled background agents or cross-device sync. The UI displays the
completed answer rather than streaming individual tokens. Dictation depends on
Windows' installed recognizers and has not been accuracy-tested with real speech.
An eight-iteration bound is not a fixed currency budget. Provider billing remains
authoritative; failed/cancelled requests may still consume credits.

Local tool permissions are enforced in code, with plain-text model output treated
as data. HTML is disabled in Markdown; CSP blocks remote resources and inline code
other than the bundled script hash. Navigation and the native message bridge
accept only the app's embedded document. This is not a security certification:
do not grant access to secrets, and review important outputs and proposed writes.

No code-signing certificate, installer reputation or automatic update service is
included. An email attachment cannot be guaranteed to pass email/security filters.
The ZIP contains a self-contained executable and instructions/licences; users
extract and open it. Actual WebView2 runtime behaviour on other PCs still needs a
small family pilot. Windows 11 x64 is the primary target; no Mac/ARM-specific build.

## Build and verify

Install .NET 10 SDK, then run `./build.ps1` in PowerShell. It publishes a self-contained
win-x64 compressed executable and creates `dist/Hearth-Windows-0.1.0.zip`.
Dependency versions are pinned in the project and lock file.

`Hearth.exe --self-test --report <absolute-report-path>` runs isolated local checks.
`Hearth.exe --ui-test --report <absolute-report-path>` runs an offscreen native UI
fixture and writes screenshots. Neither uses a real key or calls Corvex.
`--live-test` additionally uses the developer's user-level `CORVEX_API_KEY`
environment variable and incurs Corvex usage against synthetic inputs only.
The ordinary app ignores that variable. Live tests must not be run casually on
recipients' accounts.

Verification reports and screenshots are in `verification/`. Failed intermediate
reports are retained for diagnosis; use the latest release verification records.
The source archive excludes generated build folders, user data and credentials.
