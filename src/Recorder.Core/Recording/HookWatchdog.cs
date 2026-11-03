using Recorder.Core.Automation;

namespace Recorder.Core.Recording;

/// <summary>
/// Windows removes a low-level hook that is too slow without saying so (spec 9.1). The cursor moving over the target in two
/// checks in a row while the hook has seen nothing means it is gone (D-56). A cursor moved by a program, not by input, would
/// not keep moving between two checks without any click.
/// </summary>
internal sealed class HookWatchdog(IMouseHook mouse, ICursor cursor, IRecordingTarget target, RecordingOptions options, TimeProvider time, Func<bool> isRecording, Action reinstall)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(options.HookCheckInterval, time);
        var last = cursor.Position;
        var movedBefore = false;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var position = cursor.Position;
                var moved = position != last && target.IsOverTarget(position);
                last = position;
                if (isRecording() && moved && movedBefore && mouse.IdleFor >= options.HookLostAfter)
                {
                    reinstall();
                    moved = false;
                }

                movedBefore = moved;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
    }
}
