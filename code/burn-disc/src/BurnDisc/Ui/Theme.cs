using Spectre.Console;

namespace BurnDisc.Ui;

//
// Visual tokens for the dashboard, matching OMG's live terminal dashboards
// (client-scheduler / steam-stats): a bold dark-orange title on a subtle
// grey-bordered frame with dim footer hints. Replicated standalone rather than
// depending on the game repo's chrome library.
//
internal static class Theme {
    public const string Header = "darkorange"; // OMG brand orange
    public const string HintRest = "dim";
    public static readonly Color Border = Color.Grey23;
}
