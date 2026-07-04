# Chart

[![ci](https://github.com/Pixnop/Chart/actions/workflows/ci.yml/badge.svg)](https://github.com/Pixnop/Chart/actions/workflows/ci.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Chart&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Pixnop_Chart)

Dimension-aware world map for [Manifold](https://github.com/Pixnop/Manifold) custom dimensions.

Chart is a client-side Vintage Story mod that renders a separate world map per dimension,
so custom dimensions created with Manifold each get their own map instead of bleeding into
the overworld map. Alpha-stage: expect visual rough edges (chunk-edge seams, softened
hillshade versus vanilla).

Players install it from the [Mod DB page](https://mods.vintagestory.at/chart); source and
issues live at [github.com/Pixnop/Chart](https://github.com/Pixnop/Chart).

## Relationship to Manifold

Chart is a companion to Manifold, not part of its core. It consumes Manifold through the
published NuGet package `Pixnop.Manifold` at build time, and declares a runtime dependency
on the `manifold` mod. This keeps Chart standalone and exercises Manifold's public API
surface the same way any third-party mod would.

| Layer            | Value     |
|------------------|-----------|
| Repo             | Chart     |
| C# ns / assembly | Chart     |
| modid (runtime)  | chart     |
| Mod DB display   | Chart     |

## Build

Requires the .NET 10 SDK and a Vintage Story install. Set the `VINTAGE_STORY` environment
variable to the directory containing `VintagestoryAPI.dll`.

```
dotnet build Chart.slnx -c Release
```

The Manifold reference is restored from NuGet automatically. `Manifold.dll` is excluded from
Chart's build output (`ExcludeAssets=runtime`): at runtime the Manifold mod provides it.

## Test

```
dotnet test Chart.slnx
```

Tests are pure unit tests against fakes/mocks of the Vintage Story API; they do not launch
the game. CI (GitHub Actions) builds, runs the tests with coverage and feeds the analysis
to [SonarCloud](https://sonarcloud.io/summary/new_code?id=Pixnop_Chart) on every push and
pull request.

## E2E scenarios

```
dotnet test tests/Chart.Scenarios
```

Boots a headless Vintage Story server through [Atlas](https://github.com/Pixnop/Atlas),
with the published Manifold release zip staged as a real mod, and pins the engine and
Manifold contracts Chart's renderer is built on (RainHeightMap fallback, dim-encoded
chunk slice reads, dimension destroy events). Chart itself is a client-only mod and does
not load in that server; rendering is validated visually in game.

## Package

```
dotnet build src/Chart/Chart.csproj -c Release -t:PackMod
```

Produces the Mod DB release zip at `release/Chart-<version>.zip` (mod dll, `modinfo.json`
and `modicon.png` only; the game and Manifold dlls are provided at runtime).

## Layout

```
src/Chart/              the mod (ChartModSystem + Internal/)
tests/Chart.Pure.Tests/ unit tests
```

## License

See [LICENSE](LICENSE).
