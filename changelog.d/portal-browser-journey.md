### Added

- Added an opt-in Playwright browser lane (`scripts/test-lane.ps1 -Lane browser`) covering the critical Portal journey end to end in a real Chromium: first-run sign-in through the forced password change, creating a user, creating a folder, publishing a report into it, and running that report until rendered rows appear. The lane runs against a Kestrel-hosted Portal on a loopback port, fails on any unhandled JavaScript exception, is excluded from the default filter by `Category=Browser`, and is wired into the pre-release gate and CI.

### Fixed

- Fixed the forced first-run password change signing the user into a dead session. Changing a password invalidates every session for the account, so the Portal sent the user into the app holding an already-invalidated token and silently bounced them back to the login page — the first thing a new deployment does looked like a failed sign-in. The new password is now exchanged for a fresh session before entering the app, and a failure to re-authenticate says the password *was* changed instead of reporting a password-change error.
- Fixed unhandled promise rejections from the report catalog's view transitions. Navigating faster than the animation skips the in-flight transition, and its rejected `ready`/`finished` promises were left unhandled on the page. A throw inside the update callback still surfaces.
