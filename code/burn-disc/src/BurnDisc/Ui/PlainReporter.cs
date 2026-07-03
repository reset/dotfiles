namespace BurnDisc.Ui;

//
// Fallback reporter for dry-runs and non-interactive output (piped, no TTY).
// No live bars — just status lines, so logs stay readable and scriptable.
// Progress writes go to stderr; program output (the CUE on dry-run) stays on
// stdout and uncluttered.
//
internal sealed class PlainReporter : IProgressReporter {
    public void Header(string title, IReadOnlyList<(string Label, string Value)> rows) {
        Console.Error.WriteLine($"== {title} ==");
        foreach ((string label, string value) in rows) {
            Console.Error.WriteLine($"  {label,-8} {value}");
        }
    }

    public void Info(string message) => Console.Error.WriteLine(message);
    public void Warn(string message) => Console.Error.WriteLine($"Warning: {message}");
    public void Success(string message) => Console.Error.WriteLine(message);

    public Task RunAsync(Func<IProgressScope, Task> body) => body(new PlainScope());

    private sealed class PlainScope : IProgressScope {
        public IProgressTask AddTask(string description, double maxValue) {
            Console.Error.WriteLine($"{description}...");
            return new PlainTask();
        }

        public void Log(string message) => Console.Error.WriteLine(message);
    }

    private sealed class PlainTask : IProgressTask {
        public double MaxValue { get; set; } = 1;
        public double Value { get; set; }
        public void Complete() => Value = MaxValue;
    }
}
