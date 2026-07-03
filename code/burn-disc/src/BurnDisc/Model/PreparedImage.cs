namespace BurnDisc.Model;

//
// The result of unwrapping and converting the user's input into something
// burnable: either a CUE sheet on disk (bin/cue path) or a bare ISO. Exactly
// one of CueFilePath / IsoFilePath is set.
//
internal sealed class PreparedImage {
    private PreparedImage(
        string sourceLabel,
        EImageFormat sourceFormat,
        string? cueFilePath,
        string? isoFilePath,
        bool needsSwap,
        IReadOnlyList<DiscTrack> tracks) {
        SourceLabel = sourceLabel;
        SourceFormat = sourceFormat;
        CueFilePath = cueFilePath;
        IsoFilePath = isoFilePath;
        NeedsSwap = needsSwap;
        Tracks = tracks;
    }

    public string SourceLabel { get; }         // human description of where this came from
    public EImageFormat SourceFormat { get; }
    public string? CueFilePath { get; }
    public string? IsoFilePath { get; }
    public bool NeedsSwap { get; }             // true for CCD/img: audio is big-endian
    public IReadOnlyList<DiscTrack> Tracks { get; }

    public bool IsIso => IsoFilePath is not null;

    public static PreparedImage FromCue(
        string cueFilePath, EImageFormat sourceFormat, string sourceLabel,
        bool needsSwap, IReadOnlyList<DiscTrack> tracks) =>
        new(sourceLabel, sourceFormat, cueFilePath, isoFilePath: null, needsSwap, tracks);

    public static PreparedImage FromIso(string isoFilePath, string sourceLabel) =>
        new(sourceLabel, EImageFormat.Iso, cueFilePath: null, isoFilePath, needsSwap: false, tracks: []);
}
