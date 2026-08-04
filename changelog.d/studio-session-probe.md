### Fixed

- **The Studio capability probe still answered 403 to most roles**, despite an earlier attempt to
  open it. `GET /api/studio/session` exists to answer "what may this user do in Studio?", and the
  Portal shell calls it on every page load — but an action-level `[Authorize]` does **not** override
  a class-level `[Authorize(Roles = …)]` in ASP.NET Core; both apply. Only `[AllowAnonymous]` takes
  an action out of that policy, so the endpoint now uses it and restates the authentication
  requirement explicitly.

  With that fixed, every non-admin role loads the report library with **no failed requests at all**.
