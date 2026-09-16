# LightGBM runtime repair — 16 September 2026

The supplied worker log shows native LightGBM loading failing, followed by
FastTreeTweedie fits in every walk-forward fold. Training continued with a
different algorithm; this was not evidence that the expected LightGBM model
was being trained, accepted, or served.

## Root cause verified locally

The installed `Microsoft.ML.LightGbm` 5.0.0 package depends on the native
`LightGBM` 4.6.0 package. Its Linux binary is `linux-x64/lib_lightgbm.so`.
Inspection of this exact binary's ELF dependencies showed `libgomp.so.1`.
Running `ldd` against it inside the unmodified `mcr.microsoft.com/dotnet/aspnet:10.0`
Linux x64 runtime reproduced:

```text
libgomp.so.1 => not found
```

The other listed native dependencies resolved. Both repository Dockerfiles
previously installed Kerberos support but omitted GNU OpenMP. LightGBM's own
[installation guide](https://lightgbm.readthedocs.io/en/v4.5.0/Installation-Guide.html)
also documents that an OpenMP runtime can require a separate installation.

## Changes

- API and worker images now install `libgomp1` alongside the existing Kerberos package.
- Both final images execute `dotnet <app>.dll --check-lightgbm` during the build.
  This fits and scores a tiny deterministic synthetic dataset with native
  LightGBM. It runs before host construction, migrations, key validation or
  background services; it never contacts a database or football/AI provider.
  A native loading or training failure fails the image build.
- Production goal-rate training stops if its native dependency is unavailable.
  The existing sync pipeline catches a training failure and continues with a
  compatible saved model, or Dixon–Coles if none is available.
- Offline fallback requires `HybridModel:AllowOfflineTrainerFallback=true` or
  `audit-goal-rate --allow-trainer-fallback`. Publishing runs ignore this opt-in.
  An offline fallback warns once and does not keep trying the broken native
  loader on every fold. Its evaluation cannot pass the publication gate.
- Artifact compatibility requires a recorded `LightGbm` trainer. Shared model
  transfer also checks that the evaluation and manifest name the same trainer.
  Previously accepted fallback or unidentified artifacts are rejected rather
  than relabeled as LightGBM; they remain on disk/in the database unmodified.
- A recent fallback evaluation no longer suppresses a native retraining attempt
  through the 24-hour evaluation-age check after the dependency is repaired.

## Release and verification

Rebuild and redeploy **both** API and worker using their updated Dockerfiles.
Restarting an old image cannot install the missing runtime. Each image build
must contain a successful `LightGBM native fit/score OK` line.

For local Linux x64 image verification:

```sh
docker build --platform linux/amd64 -f Dockerfile.worker -t soccer-ai-worker:lightgbm-check .
docker run --rm --platform linux/amd64 soccer-ai-worker:lightgbm-check --check-lightgbm
docker build --platform linux/amd64 -f Dockerfile -t soccer-ai-api:lightgbm-check .
docker run --rm --platform linux/amd64 soccer-ai-api:lightgbm-check --check-lightgbm
```

The NuGet native package inspected here contains Linux x64, Windows x64 and
macOS x64 libraries, not an ARM64 build. The native build check therefore also
catches an unsupported runtime architecture. The local Apple Silicon machine
uses Linux x64 container emulation for these checks.

The synthetic check tests runtime compatibility, not football accuracy. A real
training run must still pass the existing chronological evaluation gates before
publishing. No production model, accuracy figure or deployment is changed by
the local runtime check.

## Local validation

- Backend suite: 676 passed, 1 optional PostgreSQL test skipped.
- Negative control: the new worker runtime check exits with code 1 in the
  original Linux x64 ASP.NET runtime without OpenMP.
- Positive control: the API and worker published binaries both fit/score
  successfully in that Linux runtime after installing `libgomp1`.
- Both complete Linux x64 Dockerfile builds succeed, including the native
  fit/score check inside each final image.

No Render deployment or production retraining was performed.
