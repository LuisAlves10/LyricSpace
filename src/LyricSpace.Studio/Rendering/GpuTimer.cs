using Vortice.Direct3D11;

namespace LyricSpace.Studio.Rendering;

internal readonly record struct GpuSample(long Frame, double Milliseconds);

internal sealed class GpuTimer : IDisposable
{
    private const int RingSize = 8;
    private readonly ID3D11DeviceContext _context;
    private readonly Slot[] _slots;
    private int _head;
    private int _pending;

    public GpuTimer(ID3D11Device device, ID3D11DeviceContext context)
    {
        _context = context;
        _slots = Enumerable.Range(0, RingSize).Select(_ => new Slot(
            device.CreateQuery(QueryType.TimestampDisjoint),
            device.CreateQuery(QueryType.Timestamp),
            device.CreateQuery(QueryType.Timestamp))).ToArray();
    }

    public void ReadCompleted(Action<GpuSample> receive)
    {
        const AsyncGetDataFlags flags = AsyncGetDataFlags.DoNotFlush;
        while (_pending > 0)
        {
            Slot slot = _slots[_head];
            if (!_context.GetData(slot.Interval, flags, out QueryDataTimestampDisjoint interval)
                || !_context.GetData(slot.Start, flags, out ulong start)
                || !_context.GetData(slot.Finish, flags, out ulong finish))
            {
                break;
            }

            _head = (_head + 1) % RingSize;
            _pending--;
            if (!interval.Disjoint && interval.Frequency != 0 && finish >= start)
            {
                receive(new GpuSample(slot.Frame, (finish - start) * 1000.0 / interval.Frequency));
            }
        }
    }

    public void Begin(long frame)
    {
        if (_pending == RingSize)
        {
            return;
        }

        Slot slot = _slots[(_head + _pending) % RingSize];
        slot.Frame = frame;
        _context.Begin(slot.Interval);
        _context.End(slot.Start);
    }

    public void End()
    {
        if (_pending == RingSize)
        {
            return;
        }

        Slot slot = _slots[(_head + _pending) % RingSize];
        _context.End(slot.Finish);
        _context.End(slot.Interval);
        _pending++;
    }

    public void Dispose()
    {
        foreach (Slot slot in _slots)
        {
            slot.Finish.Dispose();
            slot.Start.Dispose();
            slot.Interval.Dispose();
        }
    }

    private sealed record Slot(ID3D11Query Interval, ID3D11Query Start, ID3D11Query Finish)
    {
        public long Frame { get; set; }
    }
}
