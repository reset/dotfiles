using BurnDisc.Model;

namespace BurnDisc.Infrastructure;

internal interface ILibraryScanner {
    IReadOnlyList<LibraryItem> ScanLocal();
    Task<IReadOnlyList<LibraryItem>> ScanServerAsync(CancellationToken cancellationToken = default);
}

//
// Discovers burnable titles locally (filesystem walk) and on the media server
// (one SSH `find`). The server call uses GNU find's -printf so a whole library
// comes back in a single round trip with sizes attached.
//
internal sealed class LibraryScanner : ILibraryScanner {
    // Formats worth listing as a title. Bare .bin/.img are excluded — they are
    // only burnable via a sibling cue/ccd, which is listed instead.
    private static readonly string[] s_extensions = ["7z", "zip", "rar", "chd", "iso", "cue", "ccd"];
    private static readonly HashSet<string> s_extensionSet = new(s_extensions, StringComparer.OrdinalIgnoreCase);

    private readonly LibraryConfig m_config;
    private readonly IProcessRunner m_processRunner;

    public LibraryScanner(LibraryConfig config, IProcessRunner processRunner) {
        m_config = config;
        m_processRunner = processRunner;
    }

    public IReadOnlyList<LibraryItem> ScanLocal() {
        if (!Directory.Exists(m_config.LocalPath)) {
            return [];
        }

        List<LibraryItem> items = [];
        foreach (string file in Directory.EnumerateFiles(m_config.LocalPath, "*", SearchOption.AllDirectories)) {
            string ext = Path.GetExtension(file).TrimStart('.');
            if (!s_extensionSet.Contains(ext)) {
                continue;
            }
            long size;
            try {
                size = new FileInfo(file).Length;
            } catch (IOException) {
                continue;
            }
            items.Add(new LibraryItem(Path.GetFileNameWithoutExtension(file), ELibrarySource.Local, file, size));
        }
        return items;
    }

    public async Task<IReadOnlyList<LibraryItem>> ScanServerAsync(CancellationToken cancellationToken = default) {
        // Build:  find <path> -type f \( -iname '*.7z' -o -iname '*.zip' ... \) -printf '%s\t%P\n'
        string nameTests = string.Join(" -o ", s_extensions.Select(e => $"-iname '*.{e}'"));
        string remoteCommand = $"find {ShellQuote(m_config.MediaPath)} -type f \\( {nameTests} \\) -printf '%s\\t%P\\n' 2>/dev/null";

        ProcessResult result;
        try {
            result = await m_processRunner.RunAsync("ssh", [m_config.MediaHost, remoteCommand], cancellationToken: cancellationToken).ConfigureAwait(false);
        } catch (ProcessException) {
            return []; // ssh unavailable — server is optional
        }
        if (!result.Succeeded) {
            return [];
        }

        List<LibraryItem> items = [];
        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            int tab = line.IndexOf('\t');
            if (tab <= 0) {
                continue;
            }
            if (!long.TryParse(line[..tab], out long size)) {
                continue;
            }
            string relative = line[(tab + 1)..];
            items.Add(new LibraryItem(Path.GetFileNameWithoutExtension(relative), ELibrarySource.Server, relative, size));
        }
        return items;
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";
}
