namespace Archivator.PPMd;

public sealed class RangeEncoder(Stream stream)
{
    private const uint Top = 1u << 24;

    private ulong _low;
    private uint _range = 0xFFFFFFFF;
    private byte _cache;
    private uint _cacheSize = 1;

    public void Encode(uint cumFreq, uint freq, uint totalFreq)
    {
        _range /= totalFreq;
        _low += cumFreq * (ulong) _range;
        _range *= freq;

        while (_range < Top)
        {
            ShiftLow();
            _range <<= 8;
        }
    }

    private void ShiftLow()
    {
        var carry = (byte) (_low >> 32);
        if ((uint) _low < 0xFF000000u || carry != 0)
        {
            var temp = _cache;
            do
            {
                stream.WriteByte((byte) (temp + carry));
                temp = 0xFF;
            } while (--_cacheSize != 0);

            _cache = (byte) (_low >> 24);
        }

        _cacheSize++;
        _low = (uint) _low << 8;
    }

    public void Flush()
    {
        for (var i = 0; i < 5; i++)
            ShiftLow();
    }
}

public sealed class RangeDecoder
{
    private const uint Top = 1u << 24;

    private readonly Stream _stream;
    private uint _code;
    private uint _range = 0xFFFFFFFF;

    public RangeDecoder(Stream stream)
    {
        _stream = stream;
        for (var i = 0; i < 5; i++)
            _code = (_code << 8) | ReadByte();
    }

    public uint GetThreshold(uint totalFreq)
    {
        _range /= totalFreq;
        var t = _code / _range;
        return t < totalFreq ? t : totalFreq - 1;
    }

    public void Decode(uint cumFreq, uint freq)
    {
        _code -= cumFreq * _range;
        _range *= freq;

        while (_range < Top)
        {
            _code = (_code << 8) | ReadByte();
            _range <<= 8;
        }
    }

    private byte ReadByte()
    {
        var b = _stream.ReadByte();
        return (byte) (b >= 0 ? b : 0);
    }
}