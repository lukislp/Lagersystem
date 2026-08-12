namespace LagersystemLVHome;

/// <summary>
/// Lightweight result type for operations that can fail with an expected,
/// recoverable error. Prefer this over throwing exceptions for validation
/// or business-rule failures.
/// </summary>
/// <typeparam name="T">Success payload type.</typeparam>
public readonly record struct Result<T>
{
    private Result(bool isSuccess, T? value, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T? Value { get; }

    /// <summary>Stable machine-readable error identifier (e.g. "user.notfound").</summary>
    public string? ErrorCode { get; }

    /// <summary>Optional human-readable detail. Not localised.</summary>
    public string? ErrorMessage { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string errorCode, string? errorMessage = null)
        => new(false, default, errorCode, errorMessage);

    /// <summary>Fluent helper for mapping the success value.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        => IsSuccess
            ? Result<TOut>.Success(map(Value!))
            : Result<TOut>.Failure(ErrorCode!, ErrorMessage);

    public T ValueOr(T fallback) => IsSuccess ? Value! : fallback;
}

/// <summary>
/// Non-generic result for operations that only signal success or failure.
/// </summary>
public readonly record struct Result
{
    private Result(bool isSuccess, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string errorCode, string? errorMessage = null)
        => new(false, errorCode, errorMessage);
}
