# Markazor

**Markazor is a browser-first, self-owned Blazor WebAssembly site framework for articles and static publishing on GitHub and Azure Static Web Apps.**

Markazor generates complete public HTML at build time, with a Blazor WebAssembly Studio for setup, settings, and writing, and an Azure Functions setup/auth API. `Markazor.SourceGen` indexes public Markdown; the packaged static builder renders the shared reader components with article content and search/share metadata. After the first Azure Static Web Apps deployment, the rest of the setup and daily writing flow can happen from the browser.

All durable state stays in accounts you control: source code, posts, drafts, media, public settings, the GitHub App, and the deployment workflow live in your GitHub and Azure resources. Browser edits become ordinary Git commits; public content is indexed at build time, while `drafts/**` stays out of publish output and service worker assets.

User-owned content stays outside generated `src/**`: Markdown lives in repository-root `posts/` and `drafts/`; media lives in `assets/`; public overrides and settings live in `public/`. Advanced users still get standard .NET Blazor and Functions projects for local customization, without adopting a hosted CMS backend or control plane.

## Quick Start

### 1. Create a Site Repository

Use the [Markazor.Template](https://github.com/cloris-chan/Markazor.Template) GitHub template.

Or create a site locally:

```powershell
dotnet new install Markazor.Templates
dotnet new markazor-site -n MarkazorSite
cd MarkazorSite
```

If you created the site locally, push it to GitHub before creating the Static Web App.

### 2. Create Azure Static Web Apps

Create an Azure Static Web Apps resource for the site repository. Choose the **Blazor** build preset. For a site named `MarkazorSite`, use:

```text
app_location: src/MarkazorSite.Web
api_location: src/MarkazorSite.Functions
output_location: wwwroot
```

For a differently named site, replace `MarkazorSite` with your site name.

Wait for the first deployment to complete.

### 3. Finish Setup in the Browser

Open `/studio/setup` on the deployed site, or choose **Setup** in Studio. Follow the four numbered steps:

1. **Create a GitHub App.** The Create App button fills in the required settings. Paste the app's public Client ID into Setup.
2. **Add the secret to Azure.** In your GitHub App, open **Credentials → Client secrets → New secret** and create a secret. Save its value as `GITHUB_APP_CLIENT_SECRET` under the Static Web App's **Settings → Environment variables**, then choose **Check again** in Setup.
3. **Connect your repository.** Install the app on the site repository and choose **Authorize GitHub**.
4. **Save settings and start writing.** Open Settings, enter your site details and repository, then Save Settings. Wait for deployment, return to Setup, and Check again to enable Write.

The Client ID carries over to Settings in the same browser. A separate `MARKAZOR_AUTH_COOKIE_SECRET` is optional. Keep secrets in Azure environment variables.

For field examples and help with each step, see the [User Guide](https://github.com/cloris-chan/Markazor/blob/main/docs/user-guide.md#3-finish-setup-in-studio).

## How It Works

### Site Creation and First Setup

Both creation paths produce a user-owned site repository. Azure deploys that repository first, then Studio Setup guides GitHub App authorization before Studio Settings and Write handle public settings and writing.

```mermaid
flowchart LR
    T["Use template<br/>Markazor.Template"] --> R["User-owned<br/>site repository"]
    N["dotnet new<br/>markazor-site"] --> R
    R --> A["Azure Static Web Apps<br/>first deployment<br/>src/*.Web + src/*.Functions"]
    A --> S["/studio/setup<br/>GitHub App + SWA secret"]
    S --> M["/studio/settings<br/>save public settings"]
    M --> E["/studio/write<br/>write content"]
```

The Static Web Apps secret step is intentionally explicit. Markazor does not ask for broad Azure permissions and does not mutate the Azure management plane from the site.

### Build-Time Site Assembly

Generated Web projects opt into `MarkazorEnableSiteBuild`. During build, Markazor assembles the final web root under `src/{SiteName}.Web/obj/<Configuration>/<TargetFramework>/markazor/site/`, then lets the Blazor Static Web Assets pipeline publish that tree as `wwwroot`.

```mermaid
flowchart LR
    P["public/**<br/>user overlay"] --> G["generated site root<br/>obj/.../markazor/site"]
    A["assets/**<br/>repository media"] --> G
    C["posts/**<br/>public Markdown"] --> SG["Markazor.SourceGen<br/>content index"]
    C --> G
    D["Markazor defaults<br/>index, manifest, service worker,<br/>staticwebapp.config.json"] --> G
    S["public/markazor.settings.json"] --> G
    S --> F["Functions publish output<br/>settings copy"]
    SG --> W["Blazor WebAssembly app"]
    G --> O["Azure SWA output<br/>wwwroot"]
```

`public/**` is copied first, then Markazor fills missing shell files from package defaults. Repository assets are published under `/assets/**`, public Markdown is staged under `/_markazor/content/**`, and drafts are excluded from both publish output and service worker assets. Public routes receive physical `index.html` files containing the full reader page. Studio and Setup retain the application shell.

### Daily Writing and Publishing

Studio Write uses the live GitHub repository tree as its source of truth. Public pages use the last deployed build-time index, so a successful browser commit becomes visible after Azure Static Web Apps rebuilds and deploys the site.

```mermaid
sequenceDiagram
    participant B as Browser /studio/write
    participant GH as GitHub API
    participant R as User repo
    participant W as SWA workflow
    participant SG as Markazor.SourceGen
    participant S as Azure Static Web Apps

    B->>GH: Save Markdown or publish drafts
    GH->>R: Create ordinary Git commit
    R->>W: Push triggers deployment workflow
    W->>SG: Build Web project and generate content index
    SG-->>W: Public articles index
    W->>S: Deploy static app and Functions API
    S-->>B: Public reader updates after deployment
```

Single-file edits use GitHub's Contents API. Multi-draft publishing uses Git data operations so selected drafts can move into public content in one atomic commit.

### Ownership and Secret Boundaries

Markazor keeps secrets in Azure Static Web Apps environment variables, public settings in the repository, and browser authorization scoped to the selected GitHub App installation.

| Boundary | Contains | Write path |
|---|---|---|
| User browser | Temporary GitHub App Client ID entry, short-lived access token, protected refresh cookie | Calls the Functions API for setup/auth and GitHub REST API for content operations. |
| Azure Static Web Apps | Functions API, `GITHUB_APP_CLIENT_SECRET`, `MARKAZOR_AUTH_COOKIE_SECRET` | Exchanges OAuth codes and issues/verifies protected cookies. |
| GitHub App | selected-repository installation, Contents read/write permission | Grants the browser session scoped repository access after authorization. |
| GitHub repository | posts, drafts, assets, `public/markazor.settings.json`, public overlays, source code, workflow | Receives ordinary commits for content, assets, and public settings changes. |

The GitHub App Client ID is not a secret and can be stored in `public/markazor.settings.json`. The Client Secret belongs in Static Web Apps environment variables. `MARKAZOR_AUTH_COOKIE_SECRET` is recommended as a separate long random value for OAuth cookie protection.

## Repository Model

Generated sites use fixed repository-root folders:

```text
src/{SiteName}.Web/        Blazor WebAssembly reader and Studio UI
src/{SiteName}.Functions/  Azure Functions API for setup status and GitHub OAuth
posts/                     Public posts, created when content is written
drafts/                    Private drafts, created when drafts are written
assets/                    Markdown-referenced media assets
public/                    Public web root overlay and markazor.settings.json
```

The starter template begins with the source skeleton only. Content roots, assets, and `public/markazor.settings.json` are created later by Studio Write and Studio Settings, which keeps template updates away from user-owned content.

Generated Web projects do not keep a source-controlled `wwwroot`. The final web root is built from `public/**`, repository assets, public Markdown, and Markazor package defaults during build.

## Packages

| Package | Purpose |
|---|---|
| `Markazor` | Main runtime package, UI, client services, build targets, and analyzer packaging. |
| `Markazor.Core` | Shared contracts, setup settings, GitHub client, and content path policy. |
| `Markazor.Api` | Functions-side GitHub OAuth, setup status, and endpoint implementation. |
| `Markazor.SourceGen` | Incremental source generator for public Markdown metadata. |
| `Markazor.Themes` | Built-in theme registry and build-time theme output. |
| `Markazor.Templates` | `dotnet new` template package. |

## Local Verification

Source projects, tests, and the MinimalSite sample use `ProjectReference` to build against this checkout. The root `Directory.Packages.props` manages third-party package versions. Generated site repositories use NuGet references to `Markazor`, `Markazor.Api`, and `Markazor.Themes`; `Markazor.Core` is supplied through their package dependencies.

```powershell
dotnet restore Markazor.slnx
dotnet build Markazor.slnx -c Release --no-restore
dotnet test Markazor.slnx -c Release --no-build
./build/Pack-Markazor.ps1 -Version <version>
./build/Test-MarkazorTemplate.ps1 -Version <version> -PackageSource artifacts/packages
```
