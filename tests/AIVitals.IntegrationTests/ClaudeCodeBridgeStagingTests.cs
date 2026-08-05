using AIVitals.Adapters.ClaudeCode;

namespace AIVitals.IntegrationTests;

public sealed class ClaudeCodeBridgeStagingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-vitals-staging-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Staging_copies_the_helper_to_a_path_that_does_not_change_between_versions()
    {
        var source = CreateSource("0.1.0");
        var staging = new ClaudeCodeBridgeStaging(source, Path.Combine(_root, "bridge"));

        Assert.True(staging.TryStage("0.1.0"));
        var firstPath = staging.HelperPath;
        Assert.Equal("0.1.0", File.ReadAllText(firstPath));

        WriteHelper(source, "0.1.1");
        Assert.True(staging.TryStage("0.1.1"));

        Assert.Equal(firstPath, staging.HelperPath);
        Assert.Equal("0.1.1", File.ReadAllText(staging.HelperPath));
    }

    [Fact]
    public void Restaging_the_same_version_leaves_the_existing_copy_alone()
    {
        var source = CreateSource("0.1.0");
        var staging = new ClaudeCodeBridgeStaging(source, Path.Combine(_root, "bridge"));
        Assert.True(staging.TryStage("0.1.0"));
        var stagedAt = File.GetLastWriteTimeUtc(staging.HelperPath);

        // A newer source with the same stamp must not be copied again: the stamp is the contract.
        WriteHelper(source, "tampered");
        Assert.True(staging.TryStage("0.1.0"));

        Assert.Equal(stagedAt, File.GetLastWriteTimeUtc(staging.HelperPath));
        Assert.Equal("0.1.0", File.ReadAllText(staging.HelperPath));
    }

    [Fact]
    public void Removing_the_staged_copy_leaves_nothing_behind()
    {
        var source = CreateSource("0.1.0");
        var staging = new ClaudeCodeBridgeStaging(source, Path.Combine(_root, "bridge"));
        staging.TryStage("0.1.0");

        staging.Remove();

        Assert.False(staging.IsStaged);
        Assert.False(Directory.Exists(Path.Combine(_root, "bridge")));
    }

    [Fact]
    public void A_missing_source_never_reports_a_staged_bridge()
    {
        var staging = new ClaudeCodeBridgeStaging(Path.Combine(_root, "absent"), Path.Combine(_root, "bridge"));

        Assert.False(staging.TryStage("0.1.0"));
        Assert.False(staging.IsStaged);
    }

    private string CreateSource(string content)
    {
        var source = Path.Combine(_root, "statusline");
        Directory.CreateDirectory(source);
        WriteHelper(source, content);
        return source;
    }

    private static void WriteHelper(string source, string content) =>
        File.WriteAllText(Path.Combine(source, ClaudeCodeBridgeStaging.HelperFileName), content);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
