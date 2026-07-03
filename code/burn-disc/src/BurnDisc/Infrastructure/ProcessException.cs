namespace BurnDisc.Infrastructure;

//
// Thrown when an external tool cannot be launched or exits non-zero. Carries a
// message suitable for showing the user directly.
//
internal sealed class ProcessException : Exception {
    public ProcessException(string message) : base(message) {
    }

    public ProcessException(string message, Exception innerException) : base(message, innerException) {
    }
}
