using Vortice.Direct3D11;

namespace Wallpepper.Rendering;

/// <summary>One disjoint and two timestamp queries around a piece of GPU work; read back later without ever blocking.</summary>
internal sealed class GpuTimer : IDisposable
{
    private const int Ok = 0;

    private readonly ID3D11Query _disjoint;
    private readonly ID3D11Query _start;
    private readonly ID3D11Query _end;

    public GpuTimer(ID3D11Device device)
    {
        _disjoint = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint, QueryFlags.None));
        _start = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
        _end = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
    }

    /// <summary>A measurement was started and has not been read yet.</summary>
    public bool Pending { get; private set; }

    public void Begin(ID3D11DeviceContext context)
    {
        context.Begin(_disjoint);
        context.End(_start);
    }

    public void End(ID3D11DeviceContext context)
    {
        context.End(_end);
        context.End(_disjoint);
        Pending = true;
    }

    /// <returns>true when the result is available; <paramref name="ms"/> is -1 for a disjoint (unreliable) sample.</returns>
    public unsafe bool TryRead(ID3D11DeviceContext context, AsyncGetDataFlags flags, out double ms)
    {
        ms = -1;
        DisjointData disjoint;
        ulong start;
        ulong end;
        if (context.GetData(_disjoint, (IntPtr)(&disjoint), (uint)sizeof(DisjointData), flags).Code != Ok)
        {
            return false;
        }
        if (context.GetData(_start, (IntPtr)(&start), sizeof(ulong), flags).Code != Ok)
        {
            return false;
        }
        if (context.GetData(_end, (IntPtr)(&end), sizeof(ulong), flags).Code != Ok)
        {
            return false;
        }
        Pending = false;
        if (disjoint.Disjoint == 0 && disjoint.Frequency > 0 && end >= start)
        {
            ms = (end - start) * 1000.0 / disjoint.Frequency;
        }
        return true;
    }

    public void Dispose()
    {
        _disjoint.Dispose();
        _start.Dispose();
        _end.Dispose();
        Pending = false;
    }
}
