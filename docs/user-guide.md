# User Guide

This guide describes how to create, deploy, configure, and operate a Markazor site.

## 1. Create a Site Repository

Start from the [Markazor.Template](https://github.com/cloris-chan/Markazor.Template) GitHub template.

Or use the .NET template locally:

```powershell
dotnet new install Markazor.Templates
dotnet new markazor-site -n MarkazorSite
cd MarkazorSite
```

In Visual Studio, choose **Markazor Site**, set the solution name, and keep **Create in new folder** enabled. Markazor is a repository-level solution template, so the generated solution should be created directly inside that new repository folder.

If you created the site locally, push it to GitHub before creating the Azure Static Web App.

The starter repository begins without `posts/`, `notes/`, `drafts/`, `assets/`, or `public/markazor.settings.json`. `/editor` creates content and asset paths when you write, and `/manage` creates the public settings file when you save settings.

## 2. Create Azure Static Web Apps

Create a new Azure Static Web Apps resource and connect it to the generated GitHub repository.

For a site named `MarkazorSite`, use:

```text
app_location: src/MarkazorSite.Web
api_location: src/MarkazorSite.Functions
output_location: wwwroot
```

For a differently named site, replace `MarkazorSite` with your site name.

Wait for Azure's generated deployment workflow to finish. `/setup` is available only after the first successful deployment.

## 3. Configure the GitHub App

Open the deployed site and go to `/setup`.

The setup page shows the values to use when creating the GitHub App. The generated "Create App" link prefills safe defaults, but the app name and description are only suggestions.

Use these settings:

| Setting | Value |
|---|---|
| Homepage URL | The deployed site root. |
| Callback URL | `https://your-static-web-app.azurestaticapps.net/setup/github-callback` |
| Setup URL | `https://your-static-web-app.azurestaticapps.net/setup` |
| Redirect on update | Enabled |
| Webhook | Inactive |
| Repository permission | Contents: read and write |
| Installation | Only the generated site repository |

After creating the app:

1. Copy the GitHub App Client ID.
2. Paste it into the browser-local Client ID field on `/setup`.
3. Generate a GitHub App Client secret.
4. Add the secret to Azure Static Web Apps environment variables as `GITHUB_APP_CLIENT_SECRET`.
5. Add `MARKAZOR_AUTH_COOKIE_SECRET` as a separate long random value when possible.
6. Install the GitHub App on the site repository.
7. Return to `/setup` and authorize GitHub.

Required Static Web Apps environment variable:

```text
GITHUB_APP_CLIENT_SECRET=...
```

Recommended Static Web Apps environment variable:

```text
MARKAZOR_AUTH_COOKIE_SECRET=...
```

If `MARKAZOR_AUTH_COOKIE_SECRET` is omitted, the Functions API falls back to `GITHUB_APP_CLIENT_SECRET` for cookie protection.

## 4. Save Public Settings

After authorization, open `/manage`.

`public/markazor.settings.json` stores public, non-secret site settings:

- site title and description;
- canonical and alternate site URLs;
- GitHub App Client ID;
- repository owner, name, and default branch;
- theme selection.

`/manage` can also upload a PNG site icon. The file is always saved to `assets/site-icon.png` and served from `/assets/site-icon.png`.

Use **Save Settings** to commit settings back to the repository. If the current values already match the repository file, Markazor skips the commit.

Secrets must stay in Azure Static Web Apps environment variables and must not be committed.

## 5. Verify Repository Access

`/manage` runs repository diagnostics after authorization. The editor should be used only when these checks pass:

- repository exists and is accessible;
- read permission works;
- write permission works;
- configured branch is accessible;
- recursive tree can be read.

If diagnostics fail, check the GitHub App installation target, repository permissions, branch name, and `public/markazor.settings.json`.

## 6. Write Content

Open `/editor` after setup and diagnostics pass.

The editor uses the live GitHub repository tree as the content source. This means new drafts, deleted files, and published files appear immediately in the editor even before the next Azure Static Web Apps deployment updates the public reader.

Supported operations:

- create post and note drafts;
- edit Markdown with preview;
- save a file;
- delete a file;
- recover from SHA conflicts;
- upload PNG or other Markdown-referenced assets;
- publish one or more drafts in an atomic commit.

Saving or publishing from the editor creates Git commits in the site repository. A successful commit does not instantly update the public reader: the Azure Static Web Apps deployment workflow must rebuild and publish the site.

Until that deployment completes:

- `/editor` shows the live GitHub state;
- the public reader shows the last deployed build-time index.

## 7. Content Layout

Generated sites use this root layout after content is written or settings are saved:

```text
posts/
  your-post.md
notes/
  your-note.md
drafts/
  your-draft.md
assets/
  uploaded-image.png
public/
  markazor.settings.json
  styles/
    site.css
  scripts/
    site.js
```

Public content:

- `posts/**`
- `notes/**`

Private drafts:

- `drafts/**`

Repository assets under `assets/**` are published to `/assets/**`. Public Markdown is staged internally under `/_markazor/content/**`, so reader routes like `/posts/{slug}` and `/notes/{slug}` do not collide with raw Markdown files.

`public/**` is the user-owned web root overlay. Use `public/styles/site.css` and `public/scripts/site.js` for ordinary customization without replacing the shell. Advanced users can fully replace `index.html` or override files such as `staticwebapp.config.json`.

These `public/**` paths are reserved and rejected:

- `public/_framework/**`
- `public/_content/**`
- `public/_markazor/**`
- `public/assets/**`
- `public/service-worker-assets.js`

Generated Markazor sites keep the Web project under `src/{SiteName}.Web`; the build derives the repository root from that layout and uses fixed root folders: `posts/`, `notes/`, `drafts/`, `assets/`, and `public/`.

Drafts are not copied into publish output and do not appear in service worker assets.

## 8. PWA Updates

The public site is a PWA. When a new deployment installs a waiting service worker, Markazor shows a reload prompt.

Reload deliberately stays manual so editing sessions are not interrupted.

## 9. Package Upgrades

For package-only upgrades, update the `Markazor` version in the generated repository's `Directory.Packages.props`, then deploy again.

For template structure changes, compare the target version of `cloris-chan/Markazor.Template` with your site repository and merge the relevant changes manually.

Template repository tags are expected to align with package versions.

## Troubleshooting

### `/setup` Is Not Available

Make sure the first Static Web Apps deployment has completed successfully. `/setup` is part of the deployed Blazor app.

### Authorization Fails

Check:

- `GITHUB_APP_CLIENT_SECRET` is configured in Azure Static Web Apps;
- the GitHub App Callback URL points to `/setup/github-callback`;
- the Client ID pasted into `/setup` belongs to the same GitHub App;
- the GitHub App is installed on the site repository.

### `/editor` Cannot Save

Check:

- `/manage` repository diagnostics pass;
- the GitHub App has Contents read/write permission;
- the file path is under `posts/`, `notes/`, `drafts/`, `assets/`, or `public/`;
- the target branch still exists.

### Public Reader Still Shows Old Content

Check the Azure Static Web Apps workflow run. The editor sees GitHub immediately; the public reader updates after deployment.
