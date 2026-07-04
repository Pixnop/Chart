using Atlas.XUnit;
using Xunit;

// Atlas boots one embedded Vintage Story server per process; concurrent scenario
// classes would race on it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// The published Manifold release zip, staged by the StageManifoldReleaseZip target.
// The chartfixture folder mod is appended through atlas-mods.generated.txt (the
// AtlasMod ProjectReference sugar); both resolve relative to the test assembly.
[assembly: AtlasMods("atlas-mods/manifold.zip")]
