# Chart

Dimension-aware world map for [Manifold](https://github.com/Pixnop/Manifold) custom dimensions.

Chart is a client-side Vintage Story mod that renders a separate world map per dimension,
so custom dimensions created with Manifold each get their own map instead of bleeding into
the overworld map. Alpha-stage: expect visual rough edges (chunk-edge seams, softened
hillshade versus vanilla).

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
the game.

## Layout

```
src/Chart/              the mod (ChartModSystem + Internal/)
tests/Chart.Pure.Tests/ unit tests
```

## License

See [LICENSE](LICENSE).
