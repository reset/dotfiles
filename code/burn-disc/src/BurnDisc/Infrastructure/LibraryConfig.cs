namespace BurnDisc.Infrastructure;

//
// Where the library browser looks for burnable images. Defaults match the
// bash server-search / burn-from-server tools, and the same environment
// variables override them, so all three stay consistent.
//
internal sealed class LibraryConfig {
    public string LocalPath { get; init; } = DefaultLocalPath();
    public string MediaHost { get; init; } = Environment.GetEnvironmentVariable("MEDIA_HOST") ?? "reset@192.168.1.28";
    public string MediaPath { get; init; } = Environment.GetEnvironmentVariable("MEDIA_PATH") ?? "/var/lib/transmission-daemon/downloads";

    private static string DefaultLocalPath() {
        string? overridden = Environment.GetEnvironmentVariable("LIBRARY_PATH");
        if (!string.IsNullOrEmpty(overridden)) {
            return overridden;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "roms");
    }
}
