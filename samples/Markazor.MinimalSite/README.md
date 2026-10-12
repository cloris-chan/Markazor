# Markazor Minimal Site

This sample uses semantic project names instead of Azure Static Web Apps defaults.

When creating the Static Web App, use these build settings:

```text
app_location: src/Markazor.MinimalSite.Web
api_location: src/Markazor.MinimalSite.Functions
output_location: wwwroot
```

The `/studio/setup` page is available only after the Static Web App has already been deployed, so it can verify these values but cannot replace this deployment-time configuration.

After deployment, open `/studio/setup` or choose **Setup** in Studio:

1. **Create a GitHub App** using the prefilled link, then paste its public Client ID into Setup.
2. **Add the secret to Azure:** in the Static Web App's **Settings → Environment variables**, add `GITHUB_APP_CLIENT_SECRET` with the app's Client secret. Apply the change and choose **Check again** in Setup.
3. **Connect your repository:** install the app on this repository, then choose **Authorize GitHub**.
4. **Open Settings:** review the sample's public settings and choose **Save Settings**. After Azure deploys the change, return to Setup and check again to enable Write.

The Client ID carries over to Settings. A separate random `MARKAZOR_AUTH_COOKIE_SECRET` is optional; otherwise the API uses the Client secret to protect sign-in cookies. Secrets belong in Azure, not in the repository.

See the [setup walkthrough](https://github.com/cloris-chan/Markazor/blob/main/docs/user-guide.md#3-finish-setup-in-studio) for portal steps, organization-owned apps, field examples, and troubleshooting.

The sample Functions app exposes these Markazor endpoints:

- `GET /api/setup/status`
- `POST /api/auth/github/start`
- `POST /api/auth/github/callback`
- `POST /api/auth/github/refresh`

Use `/studio/setup/github-callback` as the GitHub App callback URL and `/studio/setup` as its Setup URL. The callback page posts the returned `code` and `state` to `/api/auth/github/callback`.

The sample includes `public/markazor.settings.json` for the site title, description, URLs, repository, public GitHub Client ID, and theme. Setup is available before authorization. Settings can also upload a PNG site icon to `assets/site-icon.png`.

`/studio/write` uses the live GitHub tree as the content list source of truth and only enriches matching files with build-time `SiteIndex` metadata. It supports single-file create/update/delete, asset upload, dirty-document navigation guards, remote/local recovery after SHA conflicts, and multi-select draft publishing. New drafts are stored as flat `drafts/*.md` files and publish to `posts/`. Publishing happens in one atomic Git commit; a branch-head conflict is recalculated and retried once. A successful publish still needs the Azure-generated Static Web Apps workflow to deploy the new public site version.

Public reader routes are `/`, `/posts`, `/posts/{slug}`, `/categories`, `/categories/{name}`, `/tags`, `/tags/{name}`, and `/archive`. Lists use path-based pagination such as `/page/2` and `/posts/page/2`. The home page shows the latest articles. Configure the site title, description, URLs, theme, and PNG site icon through `/studio/settings`. The current interface and date formatting are English.

The build emits complete public HTML using the shared reader components. `/posts/the-shape-of-a-good-page` is a reading specimen covering headings, nested lists, code, wide tables, captioned images, and repeated footnotes.

Only `posts/**` is compiled and published. `drafts/**` stays out of the browser bundle and public static files, and is available only to the authenticated editor through GitHub. Repository assets under `assets/**` are published to `/assets/**`, while public Markdown is staged under `/_markazor/content/**`. Published content under `posts/**` with `draft: true` fails the build with `MZ001`.

The published PWA caches generated public HTML and public Markdown for offline reading. When a new deployment installs a waiting service worker, the site displays an explicit Reload prompt instead of replacing the running app automatically. The Static Web Apps configuration also applies no-store to all Studio paths, revalidates public Markdown, and supplies security headers plus a static 404 response.
