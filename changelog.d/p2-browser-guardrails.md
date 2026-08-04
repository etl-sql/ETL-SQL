### Fixed

- **The Studio capability probe was itself role-gated.** `GET /api/studio/session` exists to answer
  "what may this user do in Studio?", and the shell calls it on every page load to decide whether to
  offer Studio at all. It sat behind the controller's `Admin,Publisher` requirement, so for every
  other role the answer was a 403 rather than an empty capability list — a console error on every
  sign-in, and a capability check that could not be asked without already holding the capability.

- **The browser test lane's intermittent total failure is root-caused and fixed.** The
  security-event outbox defaults to a machine-wide SQLite database under `LocalApplicationData`,
  opened before the host is built. A previous test process still shutting down held it, so the next
  host failed to start at all and every test reported a millisecond. Each test factory now gets its
  own file. Worth knowing beyond tests: two Portal or Orchestrator processes on one host share that
  database in production too.

### Added

- `BrowserApiContractTests` — exercises the real endpoints and validates their responses against the
  same `critical-api-contracts.json` the browser client validates against. The contract already
  existed and was already enforced, but only *in the user's session*: a server-side rename reached
  production and a `TypeError` on somebody's screen was the first thing that noticed. The contract
  file is read rather than restated, because a C# copy of the field list would be a second source of
  truth that agrees with the browser's until the day it quietly does not.

- `RoleJourneyTests` — Viewer, Publisher, DataSteward and OrchestratorManager journeys through a real
  browser, asserting in both directions: the surfaces a role can use are offered, and the ones it
  cannot are absent rather than merely guarded. A navigation that offers what it cannot deliver gets
  a 403 when pressed and reads as the product being broken rather than as a permission the user
  lacks. Hiding the entry point is not enough on its own, so navigating directly to `/admin.html` is
  asserted to be refused too.

- `ContainerBuildContextTests` — guards `.dockerignore` against the Dockerfiles in both directions.
  Excluding something a Dockerfile copies breaks the image build for whoever builds a container next;
  failing to exclude something nothing copies costs nothing visible at all, which is why `tests/`
  had been shipping ~14 GB of fixtures to the Docker daemon on every build.

- Browser sessions now record failed HTTP requests with method and URL. The browser's own console
  error for a failed request says only "the server responded with 403" — no URL — which is the
  difference between a finding someone can act on and one they have to reproduce by hand.

### Changed

- `tests/` and `artifacts/` are excluded from the Docker build context. `docs/` and `snippets/` are
  deliberately not: both images copy them for the embedded runtime help.
