namespace TrailQuest.Api.Infrastructure.Security;

public sealed class SecurityStorage
{
    public string KeyDirectory { get; }
    public string InstallationId { get; }

    public SecurityStorage(string directory)
    {
        KeyDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(KeyDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(KeyDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // This identifier is public metadata, never a cryptographic key.
        var idPath = Path.Combine(KeyDirectory, "installation.id");
        if (!File.Exists(idPath))
        {
            try
            {
                using var stream = new FileStream(idPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(Guid.NewGuid().ToString("N"));
            }
            catch (IOException) when (File.Exists(idPath)) { }
        }
        var id = File.ReadAllText(idPath).Trim();
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("The installation identifier is invalid.");
        InstallationId = id;
    }
}
