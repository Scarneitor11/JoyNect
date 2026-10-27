namespace JoyNect.Core.Diagnostics;

/// <summary>
/// Fixed size ring buffer of durations that can report percentiles without allocating.
/// </summary>
/// <remarks>
/// Used for two different questions that both need percentiles rather than averages: "how often do
/// controller reports arrive" and "how long does a frame take to render". Averages hide exactly the
/// stalls that users notice.
/// </remarks>
public sealed class IntervalHistogram
{
    private readonly double[] _samplesMs;
    private readonly double[] _scratch;
    private int _next;
    private int _count;

    public IntervalHistogram(int capacity = 256)
    {
        if (capacity < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "A histogram needs at least eight slots.");
        }

        _samplesMs = new double[capacity];
        _scratch = new double[capacity];
    }

    public int Capacity => _samplesMs.Length;

    public int Count => _count;

    public void Add(TimeSpan duration)
    {
        _samplesMs[_next] = duration.TotalMilliseconds;
        _next = (_next + 1) % _samplesMs.Length;
        if (_count < _samplesMs.Length)
        {
            _count++;
        }
    }

    public void Reset()
    {
        Array.Clear(_samplesMs);
        _next = 0;
        _count = 0;
    }

    /// <summary>Returns a percentile in milliseconds, or null when there is nothing to report.</summary>
    public double? Percentile(double percentile)
    {
        if (_count == 0)
        {
            return null;
        }

        Array.Copy(_samplesMs, _scratch, _count);
        var slice = _scratch.AsSpan(0, _count);
        slice.Sort();

        var index = (int)Math.Round(percentile / 100.0 * (_count - 1));
        return slice[Math.Clamp(index, 0, _count - 1)];
    }

    public double? Average
    {
        get
        {
            if (_count == 0)
            {
                return null;
            }

            double total = 0;
            for (var i = 0; i < _count; i++)
            {
                total += _samplesMs[i];
            }

            return total / _count;
        }
    }

    public double? Maximum
    {
        get
        {
            if (_count == 0)
            {
                return null;
            }

            double max = 0;
            for (var i = 0; i < _count; i++)
            {
                if (_samplesMs[i] > max)
                {
                    max = _samplesMs[i];
                }
            }

            return max;
        }
    }

    /// <summary>Human readable summary such as "8.1 ms median, 9.2 ms p99, 31 ms worst".</summary>
    public string Describe(string unit = "ms")
    {
        if (_count == 0)
        {
            return "no samples yet";
        }

        var median = Percentile(50) ?? 0;
        var p99 = Percentile(99) ?? 0;
        var worst = Maximum ?? 0;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{median:0.0} {unit} median, {p99:0.0} {unit} p99, {worst:0.0} {unit} worst");
    }
}
