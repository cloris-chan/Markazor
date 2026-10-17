# User Guide

Create a repository, deploy it to Azure, then finish setup in Studio. You need access to the GitHub account or organization that owns the repository and to your Azure Static Web App.

## 1. Create a Site Repository

Open [Markazor.Template](https://github.com/cloris-chan/Markazor.Template), choose **Use this template → Create a new repository**, and give your site repository a name.

If you prefer working locally:

```powershell
dotnet new install Markazor.Templates
dotnet new markazor-site -n MarkazorSite
cd MarkazorSite
```

Push the generated site to GitHub before continuing. In Visual Studio, choose **Markazor Site**, set the solution name, and keep **Create in new folder** enabled.

The starter has no posts or settings file yet. Studio creates these when you save settings and write your first draft.

## 2. Deploy to Azure

In the [Azure portal](https://portal.azure.com/), create a **Static Web App**. Choose **GitHub** as the source and select your site repository and deployment branch, usually `main`.

Under **Build Details**, choose the **Blazor** preset and replace its paths with:

| Azure field | Value for a site named `MarkazorSite` |
|---|---|
| App location | `src/MarkazorSite.Web` |
| API location | `src/MarkazorSite.Functions` |
| Output location | `wwwroot` |

Use the actual folder names from your repository if your site has a different name. See Microsoft's [Blazor deployment guide](https://learn.microsoft.com/en-us/azure/static-web-apps/deploy-blazor) for the portal steps.

Create the resource, then open your GitHub repository's **Actions** tab. Wait for the Azure Static Web Apps workflow to succeed. In Azure, open the site's URL from **Overview**.

You can now open `https://your-site.azurestaticapps.net/studio/setup`, or choose **Setup** in Studio. No GitHub sign-in is needed to view Setup.

## 3. Finish Setup in Studio

Follow the four numbered steps on the Setup page. Its header shows your next action, and **Check again** reloads the connection checks.

### Step 1. Create a GitHub App

A GitHub App gives Studio permission to save files to your site repository.

1. In Setup, choose **Create App** for a repository owned by your personal account. The link fills in the site URLs and permissions.
2. On GitHub, choose a unique app name and select **Create GitHub App**. If GitHub says the name is taken, add your account name.
3. On the app's **General** page, copy **Client ID** and paste it into Setup's **GitHub App Client ID** field. **App ID** is a separate value.

For an organization-owned repository, create the app in that organization. Use **Create Org App** when Setup shows it, or go to the organization's **Settings → Developer settings → GitHub Apps → New GitHub App** and enter the values below. GitHub's [registration guide](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app) explains account and organization registration.

Expand **App settings and organization setup** in Setup to copy your site's exact URLs. For manual registration:

| GitHub field | Value |
|---|---|
| Homepage URL | Your deployed site URL |
| Callback URL | Your site URL followed by `/studio/setup/github-callback` |
| Setup URL | Your site URL followed by `/studio/setup` |
| Redirect on update | Enabled |
| Request user authorization during installation | Unchecked; authorize from Setup in step 3 |
| Webhook / Active | Unchecked |
| Repository permissions / Contents | Read and write |
| Where can this app be installed? | Only on this account |

Leave the other options at their defaults. Setup remembers the public Client ID in this browser and carries it into Settings. It becomes part of your site's public settings when you save.

### Step 2. Add the Secret to Azure

1. In your GitHub App, open **Credentials → Client secrets** and select **New secret**. Create the secret, then copy its value.
2. In Azure, open your **Static Web App → Settings → Environment variables**. Choose **Production** for the live site, or the matching environment if you are setting up a preview.
3. Choose **+ Add** and enter:

   | Field | Value |
   |---|---|
   | Name | `GITHUB_APP_CLIENT_SECRET` |
   | Value | The Client secret copied from GitHub |

4. Select **Apply**, then **Apply** again to save the change.
5. Return to Setup and choose **Check again**. Step 2 should show **Secret found**.

The Client ID is public; the Client secret stays in Azure. Do not paste the secret into Studio Settings or commit it to GitHub. See [Azure environment variables](https://learn.microsoft.com/en-us/azure/static-web-apps/application-settings) for the current portal workflow.

Optional: add `MARKAZOR_AUTH_COOKIE_SECRET` in the same environment with a separate long, random value. Markazor uses it to protect sign-in cookies. If omitted, the Client secret is used instead; it does not block setup.

### Step 3. Connect Your Repository

1. In your GitHub App's settings, open **Install App** and install it on the account that owns your site repository.
2. Choose **Only select repositories**, select the repository you deployed to Azure, and finish installing. GitHub's [installation guide](https://docs.github.com/en/apps/using-github-apps/installing-your-own-github-app) covers this step.
3. Return to Setup and choose **Authorize GitHub**. Sign in with a GitHub account that has write access to the repository and approve the request.

Installing the app gives it repository access. Authorizing GitHub signs this browser in to Studio. After GitHub returns you to Setup, step 3 should show **Signed in** and **Open Settings** should be enabled.

### Step 4. Save Settings and Start Writing

Choose **Open Settings** and fill in your site's details. Setup carries over your Client ID, and Settings suggests the deployed site URL and branch.

| Studio field | What to enter |
|---|---|
| Site Base URLs | Your deployed site URL; put the primary URL first |
| Site Title | The name readers will see |
| Site Description | A short description of your site |
| GitHub App Client ID | The public Client ID from step 1 |
| Repository Owner | For `github.com/alex/journal`, enter `alex` |
| Repository Name | For `github.com/alex/journal`, enter `journal` |
| Default Branch | The branch Azure deploys, usually `main` |
| Theme | Keep **Fieldnotes** to use the built-in theme |

A PNG **Site Icon** is optional (up to 512 KB). It is saved to `assets/site-icon.png`.

Choose **Save Settings**. This saves `public/markazor.settings.json` to your GitHub repository and triggers another Azure deployment. If the file already matches, no new commit is needed.

Wait for the deployment to succeed in GitHub **Actions**, then choose **Check setup** or return to `/studio/setup` and select **Check again**. When repository checks pass, the page shows **Ready to write** and enables **Open Write**. Expand **Connection details** only if you need to investigate a failed check.

## 4. Write Your First Post

1. Open **Write** and expand **New draft**.
2. Enter a title, keep the suggested slug, and select **New Draft**. The slug becomes part of the published URL.
3. Write directly in the styled article editor. Headings, lists, tables, links, and code appear as you edit. **Document details** lets you change the title, slug, and other metadata.
4. Choose **Save** to keep the draft, or **Publish** to make it public.
5. Wait for the Azure deployment to finish, then open the public site.

Studio reads GitHub directly, so saved files appear there immediately. The public site updates after deployment. Drafts stay in the repository and are excluded from the published site.

Use the toolbar or Markdown shortcuts to format text: type `## ` for a section heading, `- ` for a list, or use **Ctrl/Cmd+B** and **Ctrl/Cmd+I** for bold and italic. **Find** searches within the article. Use **Image** to insert a URL or upload a local PNG, JPEG, WebP, or GIF; you can also paste or drop an image into the editor. Images upload to the repository when you save or publish. Each file can be up to 8 MB.

The editor keeps articles as Markdown files. Opening an article alone does not change it. After editing, Markdown spacing and table formatting may be normalized while preserving the content.

## 5. Content Layout

Generated sites use this root layout after content is written or settings are saved:

```text
posts/
  your-post.md
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

Unpublished drafts:

- `drafts/**`

Repository assets under `assets/**` are published to `/assets/**`. Public Markdown is staged internally under `/_markazor/content/**`, so reader routes like `/posts/{slug}` do not collide with raw Markdown files. The build also renders complete HTML for public pages, including the article body, title, contents links, and metadata. Taxonomy and pagination use path-based routes such as `/tags/design` and `/posts/page/2`.

Set the article title in **Document details** and start body sections with `##`. The editor's article typography follows the reading theme; published articles use the site's safe Markdown renderer. Diagram and math fences remain code blocks. The current UI and date formatting are English; there is no site-language setting.

`public/**` is the user-owned web root overlay. Use `public/styles/site.css` and `public/scripts/site.js` for ordinary customization without replacing the shell. A replacement `index.html` must retain `#app` and the resources needed by the application. The builder derives public pages and the Studio/Setup shell from it, adds the reader enhancement module, and preserves deployment headers and route rules while applying the static route contract. Public assets cannot occupy a generated page's `index.html` path.

These `public/**` paths are reserved and rejected:

- `public/_framework/**`
- `public/_content/**`
- `public/_markazor/**`
- `public/assets/**`
- `public/service-worker-assets.js`

Generated Markazor sites keep the Web project under `src/{SiteName}.Web`; the build derives the repository root from that layout and uses fixed root folders: `posts/`, `drafts/`, `assets/`, and `public/`.

Drafts are not copied into publish output and do not appear in service worker assets.

## 6. PWA Updates

The public site is a PWA. When a new deployment installs a waiting service worker, Markazor shows a reload prompt.

Reload deliberately stays manual so editing sessions are not interrupted.

## 7. Package Upgrades

For package-only upgrades, update the `Markazor` version in the generated repository's `Directory.Packages.props`, then deploy again.

For an existing GitHub App, set its Callback URL to `/studio/setup/github-callback` and its Setup URL to `/studio/setup` on your deployed site origin. These registration values must match the URLs shown in Studio Setup.

For template structure changes, compare the target version of `cloris-chan/Markazor.Template` with your site repository and merge the relevant changes manually.

Stable template releases update the template repository's `main` branch. Prerelease templates update the separate `preview` branch, which is rooted at the repository's initial commit and does not inherit a previous stable skeleton. The `main` branch therefore remains on the latest stable skeleton, and template repository tags align with their package versions.

The versionless NuGet commands in this guide resolve stable packages. To test a prerelease, select its exact version explicitly, for example `Markazor.Templates::0.2.0-preview.1`.

## Troubleshooting

| What you see | What to do |
|---|---|
| Setup cannot load, or says it could not reach the API | Check the latest Azure workflow in GitHub Actions. Confirm both the Web and Functions projects deployed with the paths in section 2, then choose Check again. Error details contains the technical response. |
| GitHub rejects the app name | Choose a unique name, for example by adding your GitHub account name. |
| Step 2 still says Secret needed | Check the environment you selected in Azure and the exact name `GITHUB_APP_CLIENT_SECRET`. Apply the change, then Check again. |
| GitHub rejects the callback URL | Use the full Callback URL shown in Setup, including `/studio/setup/github-callback`, on the same site origin. Update the app registration if the site's URL changed. |
| Authorization fails or was cancelled | Return to Setup and try again. Confirm that the Client ID and Azure Client secret belong to the same GitHub App. Use Client ID, not App ID. |
| Open Settings is disabled | Complete steps 1–3. If authorization expired, authorize GitHub again. |
| Settings saved, but Write is still unavailable | Wait for the settings deployment to succeed, then return to Setup and Check again. |
| Repository access fails | Check owner/name in Settings and install the app on that repository in step 3. |
| Write permission fails | Set the app's Contents permission to Read and write, approve any updated installation permissions, then authorize with an account that can write to the repository. Branch protection may also require changes through a pull request. |
| Branch access fails | Use an existing branch and the same branch Azure deploys. |
| Public site still shows old content | Check the latest Azure workflow in GitHub Actions. Studio sees GitHub changes immediately; readers see the last successful deployment. |

If the browser cannot remember the Client ID, paste it into Settings once more before saving. Your saved public settings will supply it after deployment.
