using BurnDisc.Infrastructure;
using BurnDisc.Model;

namespace BurnDisc.Tests;

public sealed class DriveScannerTests {
    // Captured from `drutil status` with a blank CD-R loaded.
    private const string DrutilCdr = """
         Vendor   Product           Rev
         ASUS     SDRW-08U9M-U      A114

                   Type: CD-R                 Name: /dev/disk4
           Write Speeds: 10x, 16x, 24x
             Space Free:   79:57:69         blocks:   359844 / 736.96MB / 702.82MiB
        """;

    [Fact]
    public void ParseDrutilStatus_ReadsVendorProductMediaAndSpeeds() {
        OpticalDrive? drive = DriveScanner.ParseDrutilStatus(DrutilCdr);

        Assert.NotNull(drive);
        Assert.Equal("ASUS", drive.Vendor);
        Assert.Equal("SDRW-08U9M-U", drive.Product);
        Assert.Equal("CD-R", drive.MediaType);
        Assert.Equal([10, 16, 24], drive.WriteSpeeds);
        Assert.Equal(10, drive.MinWriteSpeed);
    }

    [Fact]
    public void ParseDrutilStatus_ReturnsNull_WhenNoDriveReported() {
        Assert.Null(DriveScanner.ParseDrutilStatus("Warning: No drives found\n"));
    }
}
