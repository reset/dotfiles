namespace BurnDisc.Model;

//
// A burnable title discovered in the library. For a local item, Path is an
// absolute filesystem path; for a server item, Path is relative to the media
// root and resolved against the SSH host at download time.
//
internal sealed class LibraryItem {
    public LibraryItem(string displayName, ELibrarySource source, string path, long sizeBytes) {
        DisplayName = displayName;
        Source = source;
        Path = path;
        SizeBytes = sizeBytes;
    }

    public string DisplayName { get; }
    public ELibrarySource Source { get; }
    public string Path { get; }
    public long SizeBytes { get; }

    public string SizeDisplay => Sizes.Human(SizeBytes);
    public string SourceLabel => Source == ELibrarySource.Local ? "local" : "server";
}
