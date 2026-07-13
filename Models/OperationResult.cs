namespace ServerControlCenter.Models;

public record OperationResult(
    bool Succeeded,
    string Message,
    string? Output = null,
    int? ExitCode = null)
{
    public static OperationResult Success(string message, string? output = null, int? exitCode = 0) =>
        new(true, message, output, exitCode);

    public static OperationResult Failure(string message, string? output = null, int? exitCode = null) =>
        new(false, message, output, exitCode);
}

public sealed record OperationResult<T>(
    bool Succeeded,
    string Message,
    T? Value = default,
    string? Output = null,
    int? ExitCode = null) : OperationResult(Succeeded, Message, Output, ExitCode)
{
    public static OperationResult<T> Success(T value, string message, string? output = null, int? exitCode = 0) =>
        new(true, message, value, output, exitCode);

    public new static OperationResult<T> Failure(string message, string? output = null, int? exitCode = null) =>
        new(false, message, default, output, exitCode);
}
