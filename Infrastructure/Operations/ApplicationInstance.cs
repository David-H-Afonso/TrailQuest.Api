namespace TrailQuest.Api.Infrastructure.Operations;

// SQLite and the in-process scheduler have a single-writer application lifetime.
public static class ApplicationInstance
{
    public static FileStream Acquire(string directory)
    {
        var path = Path.Combine(directory, ".trailquest.lock");
        try
        {
            var handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return handle;
        }
        catch (IOException)
        {
            throw new InvalidOperationException("Another TrailQuest process is using this data directory. Stop it before maintenance or recovery.");
        }
    }
}
