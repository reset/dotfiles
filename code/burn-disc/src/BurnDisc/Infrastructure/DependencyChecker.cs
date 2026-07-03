namespace BurnDisc.Infrastructure;

internal interface IDependencyChecker {
    void EnsureAvailable(string command, string installHint);
}

//
// Verifies that an external tool is on PATH before we try to use it, failing
// with the same "Install with: brew install X" hint the shell version gave.
//
internal sealed class DependencyChecker : IDependencyChecker {
    public void EnsureAvailable(string command, string installHint) {
        if (!IsOnPath(command)) {
            throw new ProcessException($"'{command}' not found. Install with: {installHint}");
        }
    }

    private static bool IsOnPath(string command) {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) {
            return false;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            string candidate = Path.Combine(dir, command);
            if (File.Exists(candidate)) {
                return true;
            }
        }
        return false;
    }
}
