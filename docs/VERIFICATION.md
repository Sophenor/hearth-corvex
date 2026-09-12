# Version 0.1 verification

The Windows executable was tested on 12 September 2026 before publication.

- 24 isolated local checks passed: account-key encryption and disconnection,
  conversation persistence and backup recovery, interrupted replies, selected-folder
  boundaries, approved/declined file writes, preservation of replaced files,
  context limits, cancellation, and a bounded blank-answer continuation.
- 7 native interface checks passed, with screenshots: startup, setup dismissal,
  starter prompts, saved-chat and Markdown display, theme switching, compact
  window layout and new conversations.
- The packaged live suite passed 28 checks, including Corvex GLM 5.2 and Kimi K2.7
  reading a synthetic text file through the app's actual tool loop. A multi-turn
  context check also passed.
- The curated source package built with locked dependencies, with no warnings or errors.
- NuGet's dependency audit reported no known vulnerable packages at the time of
  testing. This does not constitute an independent security audit.

No family documents were used in testing and no real user applications were
closed. Real microphone accuracy and other Windows installations need a small
user pilot. Newly available Corvex models require separate compatibility testing.

Tests are in `src/Hearth/Verification.cs` and the interface fixture is in
`MainWindow.cs`. Developer commands and their privacy/cost implications are
described in [the development notes](DEVELOPMENT.md). Reports contain local
machine paths, so those raw development reports are not included in this public
repository. The screenshot in the README uses synthetic sample data.
