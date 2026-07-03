namespace BurnDisc.Model;

//
// A single track on the disc, as recovered from a CCD table of contents.
// m_plba is the absolute sector offset (Logical Block Address) into the .img
// where the track's INDEX 01 begins.
//
internal sealed class DiscTrack {
    public DiscTrack(int number, ETrackType type, int plba) {
        Number = number;
        Type = type;
        Plba = plba;
    }

    public int Number { get; }
    public ETrackType Type { get; }
    public int Plba { get; }

    public bool IsData => Type == ETrackType.Data;
}
