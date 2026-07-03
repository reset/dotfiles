using Spectre.Console;

namespace BurnDisc.Ui;

//
// The live terminal dashboard: a header panel of source/drive facts followed
// by a Spectre progress region with a bar per phase (extract, convert, burn).
//
internal sealed class DashboardReporter : IProgressReporter {
    public void Header(string title, IReadOnlyList<(string Label, string Value)> rows) {
        Grid grid = new();
        _ = grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        _ = grid.AddColumn();
        foreach ((string label, string value) in rows) {
            _ = grid.AddRow($"[grey]{Markup.Escape(label)}[/]", Markup.Escape(value));
        }

        Panel panel = new(grid) {
            Header = new PanelHeader($"[bold]{Markup.Escape(title)}[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        };
        AnsiConsole.Write(panel);
    }

    public void Info(string message) => AnsiConsole.MarkupLine($"[grey]•[/] {Markup.Escape(message)}");
    public void Warn(string message) => AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(message)}");
    public void Success(string message) => AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");

    public Task RunAsync(Func<IProgressScope, Task> body) {
        return AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(ctx => body(new SpectreScope(ctx)));
    }

    private sealed class SpectreScope : IProgressScope {
        private readonly ProgressContext m_context;

        public SpectreScope(ProgressContext context) {
            m_context = context;
        }

        public IProgressTask AddTask(string description, double maxValue) =>
            new SpectreTask(m_context.AddTask(Markup.Escape(description), maxValue: Math.Max(maxValue, 1)));

        public void Log(string message) => AnsiConsole.MarkupLine($"[grey]•[/] {Markup.Escape(message)}");
    }

    private sealed class SpectreTask : IProgressTask {
        private readonly ProgressTask m_task;

        public SpectreTask(ProgressTask task) {
            m_task = task;
        }

        public double MaxValue {
            get => m_task.MaxValue;
            set => m_task.MaxValue = Math.Max(value, 1);
        }

        public double Value {
            get => m_task.Value;
            set => m_task.Value = value;
        }

        public void Complete() {
            m_task.Value = m_task.MaxValue;
            m_task.StopTask();
        }
    }
}
