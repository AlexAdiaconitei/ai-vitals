namespace AIVitals.Infrastructure;

public sealed class AppInstanceLease : IDisposable
{
    private readonly FileStream _stream;
    private AppInstanceLease(FileStream stream) => _stream = stream;

    public static AppInstanceLease? TryAcquire(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        try
        {
            return new(new FileStream(Path.Combine(dataDirectory, "instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
