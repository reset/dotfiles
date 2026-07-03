namespace BurnDisc.Model;

//
// A writable optical drive as reported by `drutil status`, including the loaded
// media's type, blank/used state, and the write speeds the drive advertises.
//
internal sealed class OpticalDrive {
    public OpticalDrive(string vendor, string product, string? mediaType, IReadOnlyList<int> writeSpeeds, bool? isBlank, long usedBytes, string? volumeLabel = null) {
        Vendor = vendor;
        Product = product;
        MediaType = mediaType;
        WriteSpeeds = writeSpeeds;
        IsBlank = isBlank;
        UsedBytes = usedBytes;
        VolumeLabel = volumeLabel;
    }

    public string Vendor { get; }
    public string Product { get; }
    public string? MediaType { get; }          // e.g. "CD-R", or null if no media / unknown
    public IReadOnlyList<int> WriteSpeeds { get; }
    public bool? IsBlank { get; }              // null when unknown / no media
    public long UsedBytes { get; }
    public string? VolumeLabel { get; }        // mounted volume name of a written disc, if any

    public string DisplayName => $"{Vendor} {Product}".Trim();

    // The slowest advertised speed — safest for aging retro hardware.
    public int? MinWriteSpeed => WriteSpeeds.Count > 0 ? WriteSpeeds.Min() : null;

    // Media type plus blank/used state, e.g. "CD-R · blank" or
    // "CD-ROM · has data (320M)". The disc's identity (volume label or the title
    // we just burned) is composed separately by the caller.
    public string MediaSummary {
        get {
            if (MediaType is null) {
                return "no media";
            }
            return IsBlank switch {
                true => $"{MediaType} · blank",
                false => UsedBytes > 0 ? $"{MediaType} · has data ({Sizes.Human(UsedBytes)})" : $"{MediaType} · has data",
                null => MediaType
            };
        }
    }
}
