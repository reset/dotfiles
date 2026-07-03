using System.Text.Json;
using BurnDisc.Model;

namespace BurnDisc.Infrastructure;

internal interface IBurnHistory {
    string? Lookup(DiscFingerprint fingerprint);
    void Record(DiscFingerprint fingerprint, string title);
}

//
// Remembers what was burned, keyed by disc fingerprint, in a small JSON file
// (~/.config/burn-disc/history.json). Lets a disc we've burned show its real
// title on re-insert — even across restarts — without touching the burn itself.
// Loaded once into memory; Lookup is in-memory, Record writes through.
//
internal sealed class BurnHistory : IBurnHistory {
    private const int MibTolerance = 1; // rounding slack between recorded and re-scanned size

    private readonly string m_path;
    private readonly List<Entry> m_entries;

    public BurnHistory(string path) {
        m_path = path;
        m_entries = Load(path);
    }

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "burn-disc", "history.json");

    public string? Lookup(DiscFingerprint fingerprint) {
        // Newest wins if a fingerprint was ever reused for different titles.
        for (int i = m_entries.Count - 1; i >= 0; i--) {
            Entry e = m_entries[i];
            if (e.Tracks == fingerprint.Tracks && Math.Abs(e.Mib - fingerprint.Mib) <= MibTolerance) {
                return e.Title;
            }
        }
        return null;
    }

    public void Record(DiscFingerprint fingerprint, string title) {
        _ = m_entries.RemoveAll(e => e.Tracks == fingerprint.Tracks && Math.Abs(e.Mib - fingerprint.Mib) <= MibTolerance);
        m_entries.Add(new Entry { Tracks = fingerprint.Tracks, Mib = fingerprint.Mib, Title = title });
        Save();
    }

    private static List<Entry> Load(string path) {
        try {
            if (File.Exists(path)) {
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [];
            }
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
            // Corrupt or unreadable history is non-fatal — start fresh.
        }
        return [];
    }

    private void Save() {
        try {
            string? dir = Path.GetDirectoryName(m_path);
            if (dir is not null) {
                _ = Directory.CreateDirectory(dir);
            }
            File.WriteAllText(m_path, JsonSerializer.Serialize(m_entries));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Best-effort persistence; a write failure just means no cross-session memory.
        }
    }

    internal sealed class Entry {
        public int Tracks { get; set; }
        public int Mib { get; set; }
        public string Title { get; set; } = "";
    }
}
