using BurnDisc.Infrastructure;
using BurnDisc.Model;

namespace BurnDisc.Tests;

public sealed class DriveScannerTests {
    // Captured from `drutil status` with a blank CD-R loaded.
    private const string DrutilBlankCdr = """
         Vendor   Product           Rev
         ASUS     SDRW-08U9M-U      A114

                   Type: CD-R                 Name: /dev/disk4
           Write Speeds: 10x, 16x, 24x
             Space Free:   79:57:69         blocks:   359844 / 736.96MB / 702.82MiB
             Space Used:   00:00:00         blocks:        0 /   0.00MB /   0.00MiB
            Writability: appendable, blank, overwritable
        """;

    // A CD-R that already has data written to it.
    private const string DrutilUsedCdr = """
         Vendor   Product           Rev
         ASUS     SDRW-08U9M-U      A114

                   Type: CD-R                 Name: /dev/disk4
           Write Speeds: 10x, 16x, 24x
             Space Used:   35:41:22         blocks:   160597 / 328.90MB / 313.66MiB
            Writability: readable
        """;

    [Fact]
    public void ParseDrutilStatus_ReadsVendorProductMediaAndSpeeds() {
        OpticalDrive? drive = DriveScanner.ParseDrutilStatus(DrutilBlankCdr);

        Assert.NotNull(drive);
        Assert.Equal("ASUS", drive.Vendor);
        Assert.Equal("SDRW-08U9M-U", drive.Product);
        Assert.Equal("CD-R", drive.MediaType);
        Assert.Equal([10, 16, 24], drive.WriteSpeeds);
        Assert.Equal(10, drive.MinWriteSpeed);
    }

    [Fact]
    public void ParseDrutilStatus_DetectsBlankMedia() {
        OpticalDrive drive = DriveScanner.ParseDrutilStatus(DrutilBlankCdr)!;
        Assert.True(drive.IsBlank);
        Assert.Equal(0, drive.UsedBytes);
        Assert.Equal("CD-R · blank", drive.MediaSummary);
    }

    [Fact]
    public void ParseDrutilStatus_DetectsUsedMedia() {
        OpticalDrive drive = DriveScanner.ParseDrutilStatus(DrutilUsedCdr)!;
        Assert.False(drive.IsBlank);
        Assert.Equal(160597L * 2048, drive.UsedBytes);
        Assert.StartsWith("CD-R · has data", drive.MediaSummary);
    }

    [Fact]
    public void ParseDrutilStatus_ReturnsNull_WhenNoDriveReported() {
        Assert.Null(DriveScanner.ParseDrutilStatus("Warning: No drives found\n"));
    }
}
