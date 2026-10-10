using System;
using System.IO;

namespace GolemFactory.Tutorial
{
    /// <summary>
    /// Where a finished session's playtest report is kept when the next session starts: beside
    /// it, named for when it was last written ("playtest-report-20261012-1930.md"), with a
    /// counter if two sessions ended in the same minute. Pure, so a test can pin the name.
    /// </summary>
    public static class PlaytestReportArchive
    {
        public static string NameFor(string reportPath, DateTime lastWritten, Func<string, bool> exists)
        {
            string folder = Path.GetDirectoryName(reportPath) ?? "";
            string stem = Path.GetFileNameWithoutExtension(reportPath);
            string extension = Path.GetExtension(reportPath);
            string name = Path.Combine(folder, $"{stem}-{lastWritten:yyyyMMdd-HHmm}{extension}");
            for (int n = 2; exists != null && exists(name); n++)
            {
                name = Path.Combine(folder, $"{stem}-{lastWritten:yyyyMMdd-HHmm}-{n}{extension}");
            }
            return name;
        }
    }
}
