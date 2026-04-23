namespace Archivator.PPMd;

public static class RangeCoderConstants
{
    /// <summary>Значение all-ones 8-битного байта (<c>255</c>).</summary>
    public const byte AllOnesByte = 0xFF;

    /// <summary>Ширина одного байта в битах.</summary>
    public const int BitsPerByte = 8;

    /// <summary>
    /// Количество "стартовых" байт: 4 байта = 32-битное состояние <c>_low</c>, плюс 1 запасной - чтобы последний
    /// возможный перенос успел протолкнуться.
    /// </summary>
    public const int StartupBytes = 5;

    /// <summary>
    /// Ширина <c>uint</c> в битах. Используется как величина сдвига для выделения флага переноса из старшего разряда
    /// <c>ulong _low</c>.
    /// </summary>
    public const int UintBitWidth = 32;

    /// <summary>
    /// Начальная ширина интервала: все 32 бита в единицах, т. е. <c>2^32 − 1</c>.
    /// </summary>
    public const uint InitialRange = uint.MaxValue;

    /// <summary>
    /// Маска "старший байт заполнен единицами" для <c>uint</c> (<c>0xFF000000</c>).
    /// </summary>
    public const uint HighByteFilledMask = (uint) AllOnesByte << HighByteBitPos;

    /// <summary>Позиция старшего байта в <c>uint</c>: <c>24</c>.</summary>
    public const int HighByteBitPos = UintBitWidth - BitsPerByte;

    /// <summary>
    /// Порог ренормализации.
    /// </summary>
    public const uint Top = 1u << HighByteBitPos;
}