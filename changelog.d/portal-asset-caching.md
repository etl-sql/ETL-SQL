### Changed

- **Portal static assets are now revalidated rather than re-downloaded.** Every response, static
  assets included, carried `Cache-Control: no-store`, so each page navigation refetched roughly
  3.4 MB — about 1.9 MB of it vendored libraries (`echarts`, `tabulator`, `arrow`, `chart`) that had
  not changed since install.

  The policy is now split by what a response is. Documents and API responses stay `no-store`: they
  carry catalog contents, identity and report data, and none of that belongs in a browser cache or
  an intermediary. The asset roots (`/js/`, `/css/`, `/designer/`, `/img/`, `/maps/`) get
  `no-cache, must-revalidate`, which is not "do not cache" — it permits storage and requires
  revalidation on every request, so the browser sends its ETag and receives a 304 instead of the
  file. Staleness risk is nil: an upgraded Portal returns a new ETag and the browser refetches.

- **Removed 71 inert `?v=0.17.0` cache-busting query strings** from the Portal pages. With
  `no-store` in force nothing was cacheable, so they had never done anything — they implied a
  mechanism that was not there. Correspondingly, `Set-Version.ps1` does not need to rewrite them and
  no version-agreement check is needed.

### Added

- `StaticAssetCachingTests` pins both halves of the policy, since each is a mistake someone could
  make in good faith — widening `no-store` back over the assets to be safe, or relaxing the
  documents to make the app feel faster. It asserts a real conditional request returns `304` rather
  than trusting the header alone.
