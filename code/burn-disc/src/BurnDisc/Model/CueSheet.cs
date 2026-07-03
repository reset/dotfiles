using System.Text;

namespace BurnDisc.Model;

//
// A parsed table of contents: one backing image file plus its ordered tracks.
// Renders to a cdrdao-compatible CUE sheet.
//
internal sealed class CueSheet {
    public CueSheet(string imageFileName, IReadOnlyList<DiscTrack> tracks) {
        ImageFileName = imageFileName;
        Tracks = tracks;
    }

    public string ImageFileName { get; }
    public IReadOnlyList<DiscTrack> Tracks { get; }

    public int DataTrackCount => Tracks.Count(static t => t.IsData);
    public int AudioTrackCount => Tracks.Count(static t => !t.IsData);
    public bool HasAudio => AudioTrackCount > 0;

    //
    // Render a CUE sheet. Audio tracks after track 1 get a standard 2-second
    // pregap (INDEX 00) reconstructed ahead of their INDEX 01, matching the
    // layout CloneCD writes into the .img.
    //
    public string Render() {
        StringBuilder sb = new();
        _ = sb.Append("FILE \"").Append(ImageFileName).Append("\" BINARY\n");

        foreach (DiscTrack track in Tracks.OrderBy(static t => t.Number)) {
            string type = track.IsData ? "MODE1/2352" : "AUDIO";
            _ = sb.Append("  TRACK ").Append(track.Number.ToString("D2")).Append(' ').Append(type).Append('\n');

            if (track.Number > 1 && !track.IsData) {
                int pregapLba = track.Plba - Msf.StandardPregapFrames;
                if (pregapLba >= 0) {
                    _ = sb.Append("    INDEX 00 ").Append(Msf.LbaToMsf(pregapLba)).Append('\n');
                }
            }

            _ = sb.Append("    INDEX 01 ").Append(Msf.LbaToMsf(track.Plba)).Append('\n');
        }

        return sb.ToString();
    }
}
