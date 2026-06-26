# Markazor Minimal Site + Notes

This sample uses semantic project names instead of Azure Static Web Apps defaults.

When creating the Static Web App, use these build settings:

```text
app_location: src/Markazor.MinimalSite.Web
api_location: src/Markazor.MinimalSite.Functions
output_location: wwwroot
```

The `/setup` page is available only after the Static Web App has already been deployed, so it can verify these values but cannot replace this deployment-time configuration.

Configure these Static Web Apps environment variables for the Functions API:

```text
GITHUB_APP_CLIENT_SECRET=...
MARKAZOR_AUTH_COOKIE_SECRET=...
```

`MARKAZOR_AUTH_COOKIE_SECRET` is used only to protect OAuth cookies. If it is omitted, the Functions API falls back to `GITHUB_APP_CLIENT_SECRET`, but a separate value is recommended.

The sample Functions app exposes these Markazor endpoints:

- `GET /api/setup/status`
- `POST /api/auth/github/start`
- `POST /api/auth/github/callback`
- `POST /api/auth/github/refresh`

Use `/setup/github-callback` as the GitHub App callback URL. That browser page posts the returned `code` and `state` to `/api/auth/github/callback`.

The sample includes `public/markazor.settings.json` with public site title, description, URLs, repository, GitHub client id, and theme settings. After deployment, open `/setup`, create the GitHub App, paste the Client ID into the browser-local field, add `GITHUB_APP_CLIENT_SECRET` to Static Web Apps, install the app on the repository, and authorize GitHub. Then open `/manage` to edit and save the canonical public settings back to the repository, or upload a PNG site icon to the fixed `assets/site-icon.png` asset. The editor is enabled only when the configured repository, branch, tree, and pull/push permissions pass.

`/editor` uses the live GitHub tree as the content list source of truth and only enriches matching files with build-time `SiteIndex` metadata. It supports single-file create/update/delete, asset upload, dirty-document navigation guards, remote/local recovery after SHA conflicts, and multi-select draft publishing. New drafts are stored as flat `drafts/*.md` files; `kind: post` publishes to `posts/`, and `kind: note` publishes to `notes/`. Publishing happens in one atomic Git commit; a branch-head conflict is recalculated and retried once. A successful publish still needs the Azure-generated Static Web Apps workflow to deploy the new public site version.

Public reader routes are `/`, `/posts`, `/posts/{slug}`, `/notes`, `/notes/{slug}`, `/categories`, `/tags`, and `/archive`. The home page shows the mixed latest writing feed. Configure the site title, description, URLs, theme, and PNG site icon through `/manage`; configure language and page size through `options.Site` in the Web project's `Program.cs`.

Only `posts/**` and `notes/**` are compiled and published. `drafts/**` stays out of the browser bundle and public static files, and is available only to the authenticated editor through GitHub. Repository assets under `assets/**` are published to `/assets/**`, while public Markdown is staged under `/_markazor/content/**`. Published content under `posts/**` or `notes/**` with `draft: true` fails the build with `MZ001`.

The published PWA caches public Markdown for offline reading. When a new deployment installs a waiting service worker, the site displays an explicit Reload prompt instead of replacing the running app automatically. The Static Web Apps configuration also applies no-store to setup/manage/editor paths, revalidates public Markdown, and supplies security headers plus a static 404 response.
