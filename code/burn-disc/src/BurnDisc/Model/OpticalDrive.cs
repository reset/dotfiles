namespace BurnDisc.Model;

//
// A writable optical drive as reported by `drutil status`, including the
// media type currently loaded and the write speeds the drive advertises.
//
internal sealed class OpticalDrive {
    public OpticalDrive(string vendor, string product, string? mediaType, IReadOnlyList<int> writeSpeeds) {
        Vendor = vendor;
        Product = product;
        MediaType = mediaType;
        WriteSpeeds = writeSpeeds;
    }

    public string Vendor { get; }
    public string Product { get; }
    public string? MediaType { get; }          // e.g. "CD-R", or null if no media / unknown
    public IReadOnlyList<int> WriteSpeeds { get; }

    public string DisplayName => $"{Vendor} {Product}".Trim();

    // The slowest advertised speed — safest for aging retro hardware.
    public int? MinWriteSpeed => WriteSpeeds.Count > 0 ? WriteSpeeds.Min() : null;
}
