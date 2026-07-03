using BurnDisc.Infrastructure;
using BurnDisc.Model;
using BurnDisc.Ui;
using Spectre.Console;

namespace BurnDisc.Tests;

public sealed class LibraryDashboardRenderTests {
    // BuildFrame touches only render state (never the injected services), so
    // null deps are safe here — this exercises frame assembly and, crucially,
    // markup escaping of titles containing '[' , '(' and other Spectre markup.
    private static LibraryDashboard NewDashboard() =>
        new(scanner: null!, driveScanner: null!, preparer: null!, burner: null!,
            processRunner: null!, dependencies: null!, platformDetector: null!, config: new LibraryConfig());

    private static string Render(LibraryDashboard dashboard) {
        StringWriter sink = new();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(sink)
        });
        console.Profile.Width = 100;
        console.Profile.Height = 40;
        console.Write(dashboard.BuildFrame());
        return sink.ToString();
    }

    [Fact]
    public void BuildFrame_RendersTitlesWithMarkupCharacters_WithoutThrowing() {
        LibraryDashboard dashboard = NewDashboard();
        dashboard.SeedForTest([
            new LibraryItem("Sonic CD (USA)", ELibrarySource.Local, "/roms/Sonic CD (USA).7z", 415_875_370),
            new LibraryItem("Snatcher [proto]", ELibrarySource.Server, "Snatcher [proto].7z", 463_654_039),
        ]);

        string output = Render(dashboard);

        Assert.Contains("burn-disc", output);
        Assert.Contains("Sonic CD (USA)", output);
        Assert.Contains("Snatcher [proto]", output);
        Assert.Contains("local", output);
    }

    [Fact]
    public void BuildFrame_EmptyLibrary_RendersPlaceholder() {
        LibraryDashboard dashboard = NewDashboard();
        dashboard.SeedForTest([]);
        Assert.Contains("no matching titles", Render(dashboard));
    }
}
