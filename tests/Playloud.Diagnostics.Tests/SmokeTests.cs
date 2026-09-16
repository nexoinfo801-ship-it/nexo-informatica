namespace Playloud.Diagnostics.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Project_Assembly_Is_Loadable() => Assert.NotNull(typeof(Playloud.Diagnostics.ModuleMarker).Assembly);
}
