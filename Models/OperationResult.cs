/// <summary>
/// Generic result type for controller operations.
/// </summary>
public sealed class OperationResult<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public string? Error { get; }

    OperationResult(bool success, T? value, string? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    public static OperationResult<T> Ok(T value) => new(true, value, null);
    public static OperationResult<T> Fail(string error) => new(false, default, error);
}

/// <summary>Non-generic version for void operations.</summary>
public sealed class OperationResult
{
    public bool Success { get; }
    public string? Error { get; }

    OperationResult(bool success, string? error)
    {
        Success = success;
        Error = error;
    }

    public static OperationResult Ok() => new(true, null);
    public static OperationResult Fail(string error) => new(false, error);
}
