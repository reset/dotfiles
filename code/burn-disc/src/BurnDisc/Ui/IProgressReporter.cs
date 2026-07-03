namespace BurnDisc.Ui;

//
// The pipeline talks to the UI only through these abstractions, so the same
// prepare/burn code drives either the live Spectre dashboard or the plain
// text fallback used for dry-runs and non-interactive output.
//
internal interface IProgressReporter {
    void Header(string title, IReadOnlyList<(string Label, string Value)> rows);
    void Info(string message);
    void Warn(string message);
    void Success(string message);

    // Runs body within a live progress region; tasks created via the scope
    // render as progress bars for the duration.
    Task RunAsync(Func<IProgressScope, Task> body);
}

internal interface IProgressScope {
    IProgressTask AddTask(string description, double maxValue);
    void Log(string message);
}

internal interface IProgressTask {
    double MaxValue { get; set; }
    double Value { get; set; }
    void Complete();
}
