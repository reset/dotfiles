using BurnDisc;
using BurnDisc.Cli;
using BurnDisc.Infrastructure;
using BurnDisc.Pipeline;
using BurnDisc.Ui;
using Microsoft.Extensions.DependencyInjection;

if (CliParser.IsHelpRequested(args)) {
    Console.Error.WriteLine(CliParser.Usage);
    return 0;
}

CliOptions options;
try {
    options = CliParser.Parse(args);
} catch (CliUsageException ex) {
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliParser.Usage);
    return 1;
}

if (options.InputFile is null && options.DryRun) {
    Console.Error.WriteLine("Error: --dry-run needs a file. Run with no arguments to open the browser.");
    return 1;
}

// The live dashboard needs a real terminal; dry-runs and piped output use the
// plain reporter so stdout stays clean and scriptable.
bool interactive = !options.DryRun && !Console.IsOutputRedirected;

ServiceCollection services = new();
_ = services.AddSingleton<IProcessRunner, ProcessRunner>();
_ = services.AddSingleton<IDependencyChecker, DependencyChecker>();
_ = services.AddSingleton<IDriveScanner, DriveScanner>();
_ = services.AddSingleton<IImagePreparer, ImagePreparer>();
_ = services.AddSingleton<IBurner, Burner>();
_ = services.AddSingleton<LibraryConfig>();
_ = services.AddSingleton<ILibraryScanner, LibraryScanner>();
_ = services.AddSingleton<LibraryDashboard>();
_ = services.AddSingleton<IProgressReporter>(_ => interactive ? new DashboardReporter() : new PlainReporter());
_ = services.AddSingleton<BurnService>();

using ServiceProvider provider = services.BuildServiceProvider();

try {
    // No file → open the interactive library browser; otherwise burn the file.
    if (options.InputFile is null) {
        return await provider.GetRequiredService<LibraryDashboard>().RunAsync(CancellationToken.None);
    }
    return await provider.GetRequiredService<BurnService>().RunAsync(options);
} catch (ProcessException ex) {
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
