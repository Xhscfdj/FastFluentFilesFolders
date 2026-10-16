namespace WinUI.TableView;

/// <summary>
/// Diagnostics hook for the "content replacement" entrance animation (see
/// <see cref="TableView.AnimateContentReplacement"/> and <see cref="TableView.BeginContentReplacement"/>).
/// <para>
/// The library never writes anything on its own: it only reports through <see cref="Sink"/>, and only when the host
/// has installed one. That keeps normal consumers free of file I/O while letting the LRS app dump the whole
/// trigger/realize/animate sequence to a log file when the animation isn't showing up.
/// </para>
/// </summary>
public static class TableViewTransitionTrace
{
    /// <summary>Receives one line per traced step. Null (the default) disables tracing entirely.</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>Whether a <see cref="Sink"/> is installed.</summary>
    public static bool Enabled => Sink is not null;

    /// <summary>Writes one traced line; never throws into the control.</summary>
    public static void Write(string message)
    {
        var sink = Sink;
        if (sink is null) return;

        try
        {
            sink($"[t{Environment.CurrentManagedThreadId}] {message}");
        }
        catch
        {
            // 诊断日志失败不能影响界面
        }
    }
}
