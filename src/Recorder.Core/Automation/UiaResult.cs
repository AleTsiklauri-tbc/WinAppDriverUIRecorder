using System.Runtime.InteropServices;
using FlaUI.Core.Exceptions;

namespace Recorder.Core.Automation;

public sealed record UiaOptions(TimeSpan CallTimeout)
{
    public static UiaOptions Default { get; } = new(TimeSpan.FromSeconds(2));

    /// <summary>
    /// Every caller gets an answer within this time. One snapshot can need several cross-process steps, each with its
    /// own <see cref="CallTimeout"/> (a frozen target took 4 s to fail), so a request still running by then is reported
    /// <see cref="UiaStatus.Busy"/> and its late result is dropped.
    /// </summary>
    public TimeSpan AnswerWithin => CallTimeout + TimeSpan.FromMilliseconds(500);

    /// <summary>A request still running after this long means the worker thread is stuck.</summary>
    public TimeSpan StuckAfter => CallTimeout * 4;
}

public enum UiaStatus
{
    Ok,

    /// <summary>No element at that point, or the request found nothing.</summary>
    NothingThere,

    /// <summary>The point is over one of the recorder's own windows.</summary>
    OwnWindow,

    /// <summary>The target did not answer in time (busy or hung, for example blocked by a modal dialog).</summary>
    Busy,

    /// <summary>Windows refused access: typically the target runs as administrator and the recorder does not.</summary>
    AccessDenied,

    /// <summary>The element no longer exists.</summary>
    Gone,

    /// <summary>A newer request replaced this one.</summary>
    Cancelled,
}

public readonly record struct UiaResult<T>(UiaStatus Status, T? Value)
    where T : class
{
    public bool IsOk => Status == UiaStatus.Ok && Value is not null;
}

public static class UiaResult
{
    public static UiaResult<T> Ok<T>(T value)
        where T : class => new(UiaStatus.Ok, value);

    public static UiaResult<T> Fail<T>(UiaStatus status)
        where T : class => new(status, null);
}

/// <summary>Turns UI Automation failures into statuses the UI can explain. Unknown failures return null.</summary>
internal static class UiaErrors
{
    private const int UiaElementNotAvailable = unchecked((int)0x80040201);
    private const int UiaTimeout = unchecked((int)0x80131505);
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int RpcServerUnavailable = unchecked((int)0x800706BA);
    private const int RpcDisconnected = unchecked((int)0x80010108);
    private const int ObjectNotConnected = unchecked((int)0x800401FD);
    private const int RpcCallRejected = unchecked((int)0x80010001);
    private const int RpcServerCallRetryLater = unchecked((int)0x8001010A);

    public static UiaStatus? Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case OperationCanceledException:
                    return UiaStatus.Cancelled;
                case TimeoutException:
                    return UiaStatus.Busy;
                case UnauthorizedAccessException:
                    return UiaStatus.AccessDenied;
                case ElementNotAvailableException:
                    return UiaStatus.Gone;
                case ExternalException external:
                    var status = FromHResult(external.HResult);
                    if (status is not null)
                    {
                        return status;
                    }

                    break;
            }
        }

        return null;
    }

    private static UiaStatus? FromHResult(int hresult) => hresult switch
    {
        UiaTimeout or RpcCallRejected or RpcServerCallRetryLater => UiaStatus.Busy,
        AccessDenied => UiaStatus.AccessDenied,
        UiaElementNotAvailable or RpcServerUnavailable or RpcDisconnected or ObjectNotConnected => UiaStatus.Gone,
        _ => null,
    };
}
