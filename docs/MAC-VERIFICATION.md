# Mac preview 0.1.0 verification

The Mac app was built from commit `d087a04a61deae9fe885c9c1f363b3660ec2c53a` in [GitHub Actions run 34730012100](https://github.com/Sophenor/hearth-corvex/actions/runs/34730012100). The workflow succeeded on 13 September 2026 UTC (12 September in the developer's local time).

## Tested package

- `Hearth-Mac.zip`: **515,781 bytes**.
- SHA-256: `252dd3b129943ced5bfa4ef42699a406d9d5a8e1c0799ec78b641ec267358bc2`.
- Universal `arm64` and `x86_64` executable, macOS 13 minimum deployment target.
- Built and tested natively on an Apple Silicon `macos-15` runner, then the exact same ZIP was downloaded and tested natively on `macos-15-intel`.
- Ad-hoc code signature verified during build. This is not Developer ID signing or notarization.
- ZIP includes `Hearth.app` and `START HERE - Mac.txt`.

## Checks performed on both architectures

**33 core checks per architecture:** actual Keychain create/read/update/remove using a unique fixture item; saved conversation reload, backup recovery and interrupted-turn handling; archive preservation; path traversal and symlink rejection; UTF-8 text read limits; native tool permission enforcement; declined writes creating nothing; approved exact-content writes and replacement backups; executable-save rejection; context and tool-loop bounds; reasoning/tool-call continuity; aggregate reported usage; single blank-answer recovery; cancellation before dispatch; actual read-only macOS diagnostics; protection against closing Hearth itself.

**7 native WebKit UI checks per architecture:** bundled-page/native bridge startup and onboarding, starter prompts, Markdown rendering, malicious HTML/link sanitization, native theme action, compact layout and new conversation. Five screenshots from each architecture were captured; setup, chat, dark/compact and welcome views were inspected. The displayed conversation, model and connection in these screenshots are synthetic fixtures.

Separately, a live Corvex protocol smoke test on the **Windows development host** verified the request format used by this Mac implementation: `zai-org/GLM-5.2-FP8` requested `read_text` for a named fixture, received the matching tool-call result and returned the exact fixture text. Two provider requests reported **515 input / 78 output tokens**. This was synthetic test data, with no real user files. The account key was not committed or uploaded to GitHub Actions.

## Limits of this verification

The Mac tests use synthetic provider responses, so these results are not a full live-account Mac session. Actual model behaviour can vary. macOS 13/14 compatibility is a deployment target, not a claim that every supported OS version was tested. Gatekeeper's downloaded-app first-open flow, Apple Dictation accuracy, managed-Mac policies and normal quitting of a real user's application were not exercised on a family member's Mac. No model-driven destructive actions, arbitrary commands or file deletion are offered.

## Windows preservation

The original Windows source, build script, setup guide and Windows verification/design documents were checked against commit `c1de9106384b27c3c07efb6b9242f714b4a9def1` with no differences. The Mac workflow builds only `mac/` and publishes no release automatically.

The Windows release remains `v0.1.0`, release ID `387720193`, with original asset ID `560043904`, size `49,673,400` bytes and SHA-256 `ccfbc06cddeff587ecbc93db6b982c32d7be4ccbf6292ddc36f1baa9de60e39c`. The separate Mac preview uses tag `mac-v0.1.0` and is explicitly not designated latest, preserving the existing Windows `/releases/latest/download/Hearth-Windows.zip` link.
