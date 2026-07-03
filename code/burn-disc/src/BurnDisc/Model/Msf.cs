namespace BurnDisc.Model;

//
// CD sector-address arithmetic. A Red Book CD runs at 75 sectors per second,
// so a Logical Block Address (LBA) maps directly to Minutes:Seconds:Frames.
//
internal static class Msf {
    public const int FramesPerSecond = 75;
    public const int SecondsPerMinute = 60;

    // Standard pregap ahead of an audio track: 2 seconds == 150 frames.
    public const int StandardPregapFrames = 2 * FramesPerSecond;

    public static string LbaToMsf(int lba) {
        int minutes = lba / FramesPerSecond / SecondsPerMinute;
        int seconds = lba / FramesPerSecond % SecondsPerMinute;
        int frames = lba % FramesPerSecond;
        return $"{minutes:D2}:{seconds:D2}:{frames:D2}";
    }
}
