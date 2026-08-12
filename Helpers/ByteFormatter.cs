namespace DataSense.Helpers;

public static class ByteFormatter
{
    private static readonly string[] SpeedUnits = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
    private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 0) bytesPerSecond = 0;
        int unitIndex = 0;
        double speed = bytesPerSecond;

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
        int unitIndex = 0;
        double value = bytes;

        while (value >= 1024 && unitIndex < ByteUnits.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:F2} {ByteUnits[unitIndex]}";
    }
}
