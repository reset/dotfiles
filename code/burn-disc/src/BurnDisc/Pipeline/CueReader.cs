using System.Text.RegularExpressions;
using BurnDisc.Model;

namespace BurnDisc.Pipeline;

//
// Minimal CUE reader used only to summarize tracks for the dashboard (data vs
// audio counts) when the source is a .cue or a chdman-produced bin/cue. It does
// not drive burning — cdrdao reads the CUE directly — so INDEX/PLBA are ignored.
//
internal static partial class CueReader {
    public static IReadOnlyList<DiscTrack> Parse(string cueText) {
        List<DiscTrack> tracks = [];
        foreach (Match m in TrackLine().Matches(cueText)) {
            int number = int.Parse(m.Groups[1].Value);
            ETrackType type = m.Groups[2].Value.StartsWith("AUDIO", StringComparison.OrdinalIgnoreCase)
                ? ETrackType.Audio
                : ETrackType.Data;
            tracks.Add(new DiscTrack(number, type, plba: 0));
        }
        return tracks;
    }

    [GeneratedRegex(@"^\s*TRACK\s+(\d+)\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TrackLine();
}
