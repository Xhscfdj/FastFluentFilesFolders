using System;
using System.IO;

namespace FastFluentFilesFolders.Helpers
{
    /// <summary>
    /// 临时诊断日志：把“整体换内容时那批行的入场动画”关键节点写到
    /// %LOCALAPPDATA%\FastFluentFilesFolders\transition.log（超过 512 KB 自动重建）。
    /// 排查完可以连同 LrsTableView 里安装 Sink 的那段一起删掉。
    /// </summary>
    internal static class TransitionLog
    {
        private static readonly object Gate = new();
        private static StreamWriter? _writer;

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastFluentFilesFolders",
            "transition.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    if (_writer is null)
                    {
                        var dir = Path.GetDirectoryName(FilePath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                            File.Delete(FilePath);

                        // 保持句柄常开：之前每行 AppendAllText 都要开关一次文件，行数一多就会明显拖慢 UI 线程、
                        // 连带的布局/实体化节奏都会被影响
                        _writer = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                        {
                            AutoFlush = true,
                        };
                    }

                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
                }
            }
            catch
            {
                // 诊断日志失败不影响主流程
            }
        }
    }
}
