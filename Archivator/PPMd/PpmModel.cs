namespace Archivator.PPMd;

public sealed class PpmModel
{
    private readonly int _maxOrder;
    private readonly ContextNode _root = new();
    private readonly ContextNode?[] _path;
    private readonly ContextNode?[] _newPath;
    private readonly bool[] _excluded = new bool[256];

    private const int RescaleThreshold = 8192;

    public PpmModel(int maxOrder)
    {
        _maxOrder = maxOrder;
        _path = new ContextNode?[maxOrder + 1];
        _newPath = new ContextNode?[maxOrder + 1];
        _path[0] = _root;
    }

    public void EncodeSymbol(byte symbol, RangeEncoder encoder)
    {
        Array.Clear(_excluded, 0, 256);

        for (var order = _maxOrder; order >= 0; order--)
        {
            var ctx = _path[order];
            if (ctx == null || ctx.TotalCount == 0)
                continue;

            uint activeTotal = 0, activeDistinct = 0;
            foreach (var (b, count) in ctx.Frequencies)
            {
                if (_excluded[b]) continue;
                activeTotal += (uint) count;
                activeDistinct++;
            }

            if (activeDistinct == 0) continue;

            var total = 2 * activeTotal;

            if (ctx.Frequencies.TryGetValue(symbol, out var symCount) && !_excluded[symbol])
            {
                uint cumFreq = 0;
                for (var b = 0; b < symbol; b++)
                {
                    if (ctx.Frequencies.TryGetValue((byte) b, out var c) && !_excluded[b])
                        cumFreq += (uint) (2 * c - 1);
                }

                encoder.Encode(cumFreq, (uint) (2 * symCount - 1), total);
                Update(symbol);
                return;
            }

            var symPart = 2 * activeTotal - activeDistinct;
            encoder.Encode(symPart, activeDistinct, total);

            foreach (var (b, _) in ctx.Frequencies)
                _excluded[b] = true;
        }

        EncodeUniform(symbol, encoder);
        Update(symbol);
    }

    public byte DecodeSymbol(RangeDecoder decoder)
    {
        Array.Clear(_excluded, 0, 256);

        for (var order = _maxOrder; order >= 0; order--)
        {
            var ctx = _path[order];
            if (ctx == null || ctx.TotalCount == 0)
                continue;

            uint activeTotal = 0, activeDistinct = 0;
            foreach (var (b, count) in ctx.Frequencies)
            {
                if (_excluded[b]) continue;
                activeTotal += (uint) count;
                activeDistinct++;
            }

            if (activeDistinct == 0) continue;

            var total = 2 * activeTotal;
            var threshold = decoder.GetThreshold(total);
            var symPart = 2 * activeTotal - activeDistinct;

            if (threshold >= symPart)
            {
                decoder.Decode(symPart, activeDistinct);
                foreach (var (b, _) in ctx.Frequencies)
                    _excluded[b] = true;
                continue;
            }

            uint cumFreq = 0;
            for (var b = 0; b < 256; b++)
            {
                if (!ctx.Frequencies.TryGetValue((byte) b, out var c) || _excluded[b])
                    continue;

                var freq = (uint) (2 * c - 1);
                if (cumFreq + freq > threshold)
                {
                    decoder.Decode(cumFreq, freq);
                    var sym = (byte) b;
                    Update(sym);
                    return sym;
                }

                cumFreq += freq;
            }

            throw new InvalidOperationException("PPM decode: символ не найден в контексте");
        }

        var symbol = DecodeUniform(decoder);
        Update(symbol);
        return symbol;
    }

    private void EncodeUniform(byte symbol, RangeEncoder encoder)
    {
        uint remaining = 0, pos = 0;
        for (var b = 0; b < 256; b++)
        {
            if (_excluded[b]) continue;
            if (b < symbol) pos++;
            remaining++;
        }

        encoder.Encode(pos, 1, remaining);
    }

    private byte DecodeUniform(RangeDecoder decoder)
    {
        uint remaining = 0;
        for (var b = 0; b < 256; b++)
            if (!_excluded[b])
                remaining++;

        var threshold = decoder.GetThreshold(remaining);

        uint pos = 0;
        for (var b = 0; b < 256; b++)
        {
            if (_excluded[b]) continue;
            if (pos == threshold)
            {
                decoder.Decode(pos, 1);
                return (byte) b;
            }

            pos++;
        }

        throw new InvalidOperationException("Порядок -1: декодирование не удалось");
    }

    private void Update(byte symbol)
    {
        for (var order = 0; order <= _maxOrder; order++)
        {
            var ctx = _path[order];
            if (ctx == null) break;

            ctx.Frequencies.TryGetValue(symbol, out var count);
            ctx.Frequencies[symbol] = count + 1;
            ctx.TotalCount++;

            if (ctx.TotalCount >= RescaleThreshold)
                Rescale(ctx);
        }

        _newPath[0] = _root;
        for (var order = 1; order <= _maxOrder; order++)
        {
            var parent = _path[order - 1];
            if (parent == null)
            {
                _newPath[order] = null;
                continue;
            }

            if (!parent.Children.TryGetValue(symbol, out var child))
            {
                child = new ContextNode();
                parent.Children[symbol] = child;
            }

            _newPath[order] = child;
        }

        Array.Copy(_newPath, _path, _maxOrder + 1);
    }

    private static void Rescale(ContextNode ctx)
    {
        var keys = new byte[ctx.Frequencies.Count];
        ctx.Frequencies.Keys.CopyTo(keys, 0);

        ctx.TotalCount = 0;
        foreach (var b in keys)
        {
            var newCount = ctx.Frequencies[b] >> 1;
            if (newCount == 0)
                ctx.Frequencies.Remove(b);
            else
            {
                ctx.Frequencies[b] = newCount;
                ctx.TotalCount += newCount;
            }
        }
    }
}

public sealed class ContextNode
{
    public readonly Dictionary<byte, int> Frequencies = new();
    public int TotalCount;
    public readonly Dictionary<byte, ContextNode> Children = new();
}