namespace BurnDisc.Infrastructure;

//
// Switches the terminal to its alternate screen buffer (and hides the cursor)
// for the duration of a full-screen TUI, restoring both on dispose so the
// user's scrollback is left untouched. A no-op when not attached to a terminal.
//
internal sealed class AlternateScreen : IDisposable {
    private const string EnterAlt = "\x1b[?1049h";
    private const string LeaveAlt = "\x1b[?1049l";
    private const string HideCursor = "\x1b[?25l";
    private const string ShowCursor = "\x1b[?25h";

    private readonly bool m_enabled;

    private AlternateScreen(bool enabled) {
        m_enabled = enabled;
        if (m_enabled) {
            Console.Out.Write(EnterAlt + HideCursor);
            Console.Out.Flush();
        }
    }

    public static AlternateScreen Enter(bool enabled) => new(enabled);

    public void Dispose() {
        if (m_enabled) {
            Console.Out.Write(ShowCursor + LeaveAlt);
            Console.Out.Flush();
        }
    }
}
