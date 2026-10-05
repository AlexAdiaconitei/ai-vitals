using AIVitals.Infrastructure;

namespace AIVitals.IntegrationTests;

public sealed class AppInstanceLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.Instance", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Only_one_instance_can_own_preferences_and_resume_reservations()
    {
        using (var first = AppInstanceLease.TryAcquire(_root))
        {
            Assert.NotNull(first);
            Assert.Null(AppInstanceLease.TryAcquire(_root));
        }
        using var restarted = AppInstanceLease.TryAcquire(_root);
        Assert.NotNull(restarted);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
