# Release Process

## Goals

- Keep one continuous repository changelog in `CHANGELOG.md`.
- Allow a unified release train or independent package releases.
- Validate generated `.nupkg` artifacts before publication.
- Publish from GitHub Actions only when a package version actually changes.

## Versioning Model

Shared package versions live in `eng/package-versions.props`.

- `CanKit.Abstractions` and `CanKit.Core` can still move together by bumping both values in one commit.
- Adapters and transports can move independently by bumping only their own version property.
- Dependency versions are kept separate from package versions so leaf packages can release without forcing a full repo version bump.

## Release Notes Model

Repository-level notes:

- Update `CHANGELOG.md` for every release commit.
- Keep the newest release at the top.

Package-level notes:

- Add `eng/release-notes/<PackageId>/<Version>.md` for each bumped package.
- These files are read by MSBuild and embedded into `PackageReleaseNotes`.

## GitHub Automation

`nuget-pipeline.yml` performs five stages:

1. Detect version bumps and impacted packages from git history.
2. Restore, pack, and validate all NuGet artifacts.
3. Verify release metadata when a version bump is present.
4. Publish only the packages whose versions changed.
5. Create a GitHub Release containing only the published packages and their symbol packages.

Required GitHub repository secret:

- `NUGET_USER`: the nuget.org profile username (not an email address).

Configure a nuget.org Trusted Publishing policy with:

- Repository owner: `pkuyo`
- Repository: `CanKit`
- Workflow file: `nuget-pipeline.yml`
- Environment: leave empty

The publish job uses GitHub OIDC to exchange its identity for a short-lived NuGet API key. A long-lived `NUGET_API_KEY` secret and a `NUGET_SOURCE` variable are not required.

GitHub Release notes are assembled from the package note files for the current version bump matrix. Packages marked with `"publish": false` in `eng/packages.json` are excluded from the NuGet push, GitHub Release description, and GitHub Release assets.

Recommended repository settings:

- Protect the default branch.
- Require CI to pass before merge.
- Treat `eng/package-versions.props` and `CHANGELOG.md` as code-owner protected files.

## Normal Release Flow

1. Update one or more version properties in `eng/package-versions.props`.
2. Append the release entry to `CHANGELOG.md`.
3. Add package note files under `eng/release-notes/`.
4. Merge the release changes into the default branch.
5. Create and push a `v*` tag, such as `v0.5.6`.
6. GitHub Actions packs, validates, publishes the bumped packages, and creates the GitHub Release.
