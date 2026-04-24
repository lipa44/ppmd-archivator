using static Archivator.PPMd.RangeCoderConstants;

namespace Archivator.PPMd;

/// <summary>
/// Range Coder - целочисленный арифметический кодер.
/// Кодирует последовательность событий <c>(cumFreq, freq, totalFreq)</c> в битовый поток.
/// </summary>
/// <param name="stream">Поток, в который выводятся байты сжатого сообщения.</param>
public sealed class RangeEncoder(Stream stream)
{
    /// <summary>
    /// Счётчик количества записанных байт для подсчёта эффективности модели.
    /// </summary>
    public ulong WrittenBytes;

    /// <summary>
    /// Стартовое значение <see cref="_cacheSize"/>: один "технический" байт-заглушка,
    /// который декодер проигнорирует при чтении <see cref="RangeCoderConstants.StartupBytes"/> инициализационных байт.
    /// </summary>
    private const uint InitialCacheSize = 1;

    /// <summary>
    /// Левый край текущего интервала. Тип <c>ulong</c> (64 бита) выбран, чтобы поймать перенос из 32-го разряда после
    /// <c>_low += cumFreq * _range</c>.
    /// </summary>
    private ulong _low;

    /// <summary>
    /// Ширина текущего интервала. После ренормализации всегда в диапазоне <c>[Top, 2^32 − 1]</c>.
    /// </summary>
    private uint _range = InitialRange;

    /// <summary>
    /// Кеш "последнего почти-готового" байта: он пока не выплюнут в поток, потому что за ним может прийти перенос.
    /// Как только появится определённость - байт уходит в поток.
    /// </summary>
    private byte _cache;

    /// <summary>
    /// Сколько байт (включая <see cref="_cache"/> и <c>0xFF</c> за ним) сейчас висит в ожидании переноса.
    /// </summary>
    private uint _cacheSize = InitialCacheSize;

    /// <summary>
    /// Закодировать одно событие.
    /// </summary>
    /// <param name="cumFreq">Кумулятивная частота - сумма частот всех событий, предшествующих текущему.</param>
    /// <param name="freq">Частота текущего события.</param>
    /// <param name="totalFreq">Суммарная частота всех событий в распределении.</param>
    /// <param name="writeToFile">Нужно ли кодеру записывать в файл.</param>
    public void Encode(uint cumFreq, uint freq, uint totalFreq, bool writeToFile)
    {
        _range /= totalFreq;
        _low += cumFreq * (ulong) _range;
        _range *= freq;

        while (_range < Top)
        {
            ShiftLow(writeToFile);
            _range <<= BitsPerByte;
        }
    }

    /// <summary>
    /// Выплюнуть (или отложить) старший байт <see cref="_low"/> с учётом возможного переноса из 32-го разряда.
    /// </summary>
    /// <param name="writeToFile">Нужно ли кодеру записывать в файл.</param>
    private void ShiftLow(bool writeToFile)
    {
        var carry = _low >> UintBitWidth != 0 ? (byte) 1 : (byte) 0;
        var highByteBlocksCarry = (uint) _low < HighByteFilledMask;

        if (highByteBlocksCarry || carry != 0)
        {
            var temp = _cache;

            do
            {
                if (writeToFile)
                {
                    stream.WriteByte((byte) (temp + carry));
                }
                else
                {
                    WrittenBytes++;
                }

                temp = AllOnesByte;
            } while (--_cacheSize != 0);

            _cache = (byte) (_low >> HighByteBitPos);
        }

        _cacheSize++;
        _low = (uint) _low << BitsPerByte;
    }

    /// <summary>
    /// Закрыть поток: вытолкнуть всё, что ещё сидит в <see cref="_low"/> и <see cref="_cache"/>.
    /// </summary>
    /// <param name="writeToFile">Нужно ли кодеру записывать в файл.</param>
    public void Flush(bool writeToFile)
    {
        for (var i = 0; i < StartupBytes; i++)
            ShiftLow(writeToFile);
    }
}

/// <summary>
/// Range Decoder - симметричный декодер для <see cref="RangeEncoder"/>.
/// </summary>
public sealed class RangeDecoder
{
    /// <summary>Источник сжатого битового потока.</summary>
    private readonly Stream _stream;

    /// <summary>
    /// Текущий "указатель" внутри интервала.
    /// </summary>
    private uint _code;

    /// <summary> Ширина интервала.</summary>
    private uint _range = InitialRange;

    /// <summary>
    /// Инициализирует декодер чтением <see cref="RangeCoderConstants.StartupBytes"/> стартовых байт из <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">Поток сжатого сообщения.</param>
    public RangeDecoder(Stream stream)
    {
        _stream = stream;

        for (var i = 0; i < StartupBytes; i++)
        {
            var nextByte = ReadByte();

            _code <<= BitsPerByte;
            _code += nextByte;
        }
    }

    /// <summary>
    /// Возвращает, сколько "единиц частоты" из <c>[0, totalFreq)</c> соответствует текущему
    /// <see cref="_code"/>. Модель по этому числу находит, в какой поддиапазон попало событие.
    /// </summary>
    /// <param name="totalFreq">Суммарная частота всех событий в распределении.</param>
    /// <returns>
    /// Индекс в диапазоне <c>[0, totalFreq)</c>.
    /// </returns>
    public uint GetThreshold(uint totalFreq)
    {
        _range /= totalFreq;

        // Сколько целых единиц частоты "уместилось" между началом интервала и _code
        return _code / _range;
    }

    /// <summary>
    /// После того как модель определила, что текущий <see cref="_code"/> попал в поддиапазон
    /// <c>[cumFreq, cumFreq + freq)</c>, "зеркалит" то же сужение интервала, что сделал кодер.
    /// </summary>
    /// <param name="cumFreq">Кумулятивная частота найденного события.</param>
    /// <param name="freq">Частота найденного события.</param>
    public void Decode(uint cumFreq, uint freq)
    {
        _code -= cumFreq * _range;
        _range *= freq;

        while (_range < Top)
        {
            var nextByte = ReadByte();

            _code <<= BitsPerByte;
            _code += nextByte;
            _range <<= BitsPerByte;
        }
    }

    /// <summary>Чтение байта из потока.</summary>
    private byte ReadByte() => (byte) _stream.ReadByte();
}