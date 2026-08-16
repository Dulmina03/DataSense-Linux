namespace DataSense.Helpers;

public static class ByteFormatter
{
    private static readonly string[] SpeedUnits = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
    private static readonly string[] ByteUnits  = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 0) bytesPerSecond = 0;
        int    unitIndex = 0;
        double speed     = bytesPerSecond;

        while (speed >= 1024 && unitIndex < SpeedUnits.Length - 1)
        {
            speed /= 1024;
            unitIndex++;
        }

        return $"{speed:F1} {SpeedUnits[unitIndex]}";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        int    unitIndex = 0;
        double value     = bytes;

        while (value >= 1024 && unitIndex < ByteUnits.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        // B → integer ("512 B"), KB/MB/GB/TB → 1 decimal ("1.5 KB", "3.2 GB")
        string fmt = unitIndex == 0 ? "F0" : "F1";
        return $"{value.ToString(fmt)} {ByteUnits[unitIndex]}";
    }
}
