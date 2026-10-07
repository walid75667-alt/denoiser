# Publishing and signing Windows packages

The `Native audio checks and Windows packages` workflow runs on every push/PR.
It rebuilds pinned native engines, runs xUnit (including actual RNNoise DLL
execution on Windows), runs the Linux real-speech benchmark, and builds setup
and portable packages with SHA-256 checksums. A `v*` tag additionally publishes
a GitHub prerelease only after both jobs succeed. CI has `contents: write`
permission only in the publishing and result-recording jobs. App and installer versions currently
are 2.6.0; use a matching beta tag such as `v2.6.0-beta.1`.

Inspect the run linked from the commit/Actions tab. A local Linux pass does
not prove that the Windows job is green. When API access is available:

```sh
gh run list --branch codex/micdenoiser-2.4-experience
gh release view v2.6.0-beta.1
```

Releases contain `MicDenoiser-Setup-x64.exe`, `MicDenoiser-Windows-x64.zip`
and `SHA256SUMS.txt`. Download the assets, check both hashes, and check that the
portable ZIP contains `MicDenoiser.exe`, the model and both native DLLs before
replacing README download links and removing `downloads/` from tracking.
The 2.5 beta assets were downloaded and SHA-256 verified, and the old tracked
2.4 packages were removed in the 2.6 change. New
binaries are generated only under ignored `artifacts/`, not committed to Git.

Removing downloads in a new commit prevents future package growth but does
not remove old binaries from Git history or shrink a full historical clone.
History rewriting is a separate coordinated migration; it is not performed
by this change. A shallow clone avoids most historical download costs.

Signing is optional until the project owner obtains a trusted code-signing
certificate. Set these repository Actions secrets securely, not in source or
chat: `WINDOWS_SIGN_CERT_PFX_BASE64`, `WINDOWS_SIGN_CERT_PASSWORD`.
The signing script imports the key into the ephemeral runner's current-user
certificate store, signs the application before packaging and then the setup
with SHA-256 and a timestamp, verifies Authenticode, and removes its temporary
PFX and imported certificate. With no certificate it explicitly reports an
unsigned build; configuring a broken certificate fails the job.

A self-signed certificate does not solve SmartScreen reputation. Modern
hardware/cloud signing services need a separate integration instead of this
exportable-PFX route. The app does not install VB-Cable automatically and
contains no custom virtual driver.

Environment networking for direct `gh` operations needs `api.github.com` and
`uploads.github.com` in addition to existing GitHub/release-asset hosts. Git
push access alone does not prove API access. The environment draft records
these additions; saving the draft does not activate networking or publish a
release. Preserve injected Git authentication; do not dump or replace tokens.

CI also records job outcomes and a run URL in `refs/notes/micdenoiser-ci` for
pushes. This small Git note attaches to the source commit and changes no
source branch. It allows read-only verification through authorized Git access
when the environment cannot reach GitHub API:

```sh
git fetch origin refs/notes/micdenoiser-ci:refs/notes/micdenoiser-ci
git notes --ref=micdenoiser-ci show HEAD
```

Windows packaging explicitly checks that at least all 55 tests executed and
passed, including the two named native DLL checks; skipped native tests do
not permit publishing. The release job verifies all three uploaded assets
exist with nonzero sizes before recording success.

The Windows job also runs `--ui-smoke` and uploads 21 actual WPF renders. Git notes retain both branch and tag outcomes for the same source commit, preferring the tag outcome for the summary.
