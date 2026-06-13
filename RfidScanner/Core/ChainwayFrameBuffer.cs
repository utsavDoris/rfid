namespace RfidScanner.Core;

public class ChainwayFrameBuffer
{
    private readonly List<byte> _buffer = new();

    public IEnumerable<byte[]> ExtractFrames()
    {
        var frames = new List<byte[]>();

        while (_buffer.Count >= 5)
        {
            var len = _buffer[0];
            if (len < 4)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            var total = len + 1;
            if (_buffer.Count < total)
                break;

            frames.Add(_buffer.Take(total).ToArray());
            _buffer.RemoveRange(0, total);
        }

        return frames;
    }

    public void Append(ReadOnlySpan<byte> chunk)
    {
        foreach (var b in chunk)
            _buffer.Add(b);

        // Prevent unbounded growth on garbage data
        if (_buffer.Count > 8192)
            _buffer.RemoveRange(0, _buffer.Count - 4096);
    }

    public void Clear() => _buffer.Clear();
}
