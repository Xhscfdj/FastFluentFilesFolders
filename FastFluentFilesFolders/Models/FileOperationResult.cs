namespace FastFluentFilesFolders.Models
{
    /// <summary>
    /// 单个路径的文件操作结果（用于批量操作逐项上报成功/失败，
    /// 保证操作岛与 UI 只依据真实磁盘结果推进状态）。
    /// </summary>
    public sealed record FileOperationResult(string FullPath, bool Success, string? ErrorMessage);
}
