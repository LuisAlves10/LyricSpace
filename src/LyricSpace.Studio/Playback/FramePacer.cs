using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LyricSpace.Studio.Playback;

/// <summary>Waits for the next frame or a Windows message on the UI thread.</summary>
internal sealed partial class FramePacer : IDisposable
{
    private const double IntervalSeconds = 1.0 / 60;
    private const uint HighResolution = 0x2;
    private const uint ModifyStateAndSynchronize = 0x100002;
    private const uint AllInput = 0x04ff;
    private const uint InputAvailable = 0x0004;
    private readonly SafeWaitHandle _timer = CreateWaitableTimerEx(0, null, HighResolution, ModifyStateAndSynchronize);
    private double _deadline;

    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public static bool HasMessages => PeekMessage(out _, 0, 0, 0, 0);

    public void Reset(double now) => _deadline = now;

    public void Advance(double now)
    {
        double elapsedIntervals = Math.Floor(Math.Max(0, now - _deadline) / IntervalSeconds);
        _deadline += (1 + elapsedIntervals) * IntervalSeconds;
    }

    /// <returns>True if the next frame is due.</returns>
    public bool IsDueOrWait(double now)
    {
        double seconds = _deadline - now;
        if (seconds <= 0)
        {
            return true;
        }

        long dueTime = -(long)Math.Max(1, Math.Ceiling(seconds * 10_000_000));
        if (!_timer.IsInvalid && SetWaitableTimer(_timer, in dueTime, 0, 0, 0, false))
        {
            nint handle = _timer.DangerousGetHandle();
            _ = MsgWaitForMultipleObjectsEx(1, ref handle, (uint)Math.Ceiling(seconds * 1000) + 10, AllInput, InputAvailable);
        }
        else
        {
            nint unused = 0;
            _ = MsgWaitForMultipleObjectsEx(0, ref unused, (uint)Math.Ceiling(seconds * 1000), AllInput, InputAvailable);
        }

        return false;
    }

    public void Dispose() => _timer.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period,
        nint completion, nint context, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, ref nint handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
}
