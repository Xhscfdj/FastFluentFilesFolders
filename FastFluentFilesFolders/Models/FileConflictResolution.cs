namespace FastFluentFilesFolders.Models
{
    /// <summary>
    /// 剪切/移动遇到同名项时用户选择的处理方式（应用于本次粘贴的全部冲突项）。
    /// </summary>
    public enum FileConflictResolution
    {
        /// <summary>替换：同名文件覆盖，同名文件夹合并。</summary>
        Replace,
        /// <summary>跳过：不处理冲突项，源文件保持不动。</summary>
        Skip,
        /// <summary>保留两者：自动生成 name (1).ext 之类的唯一名。</summary>
        KeepBoth
    }
}
