namespace Playloud.Security.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Project_Assembly_Is_Loadable() => Assert.NotNull(typeof(Playloud.Security.ModuleMarker).Assembly);
}
