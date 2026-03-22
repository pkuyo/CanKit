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

`nuget-pipeline.yml` performs four stages:

1. Detect version bumps and impacted packages from git history.
2. Restore, pack, and validate all NuGet artifacts.
3. Verify release metadata when a version bump is present.
4. Publish only the packages whose versions changed.

Required repository secrets and variables:

- Secret `NUGET_API_KEY`
- Variable `NUGET_SOURCE` such as `https://api.nuget.org/v3/index.json`

Recommended repository settings:

- Protect the default branch.
- Require CI to pass before merge.
- Treat `eng/package-versions.props` and `CHANGELOG.md` as code-owner protected files.

## Normal Release Flow

1. Update one or more version properties in `eng/package-versions.props`.
2. Append the release entry to `CHANGELOG.md`.
3. Add package note files under `eng/release-notes/`.
4. Push to the default branch.
5. GitHub Actions packs, validates, and publishes the bumped packages.
