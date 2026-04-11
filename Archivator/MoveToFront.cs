namespace Archivator;

public static class MoveToFront
{
    private const int AlphabetSize = 256;

    public static byte[] Forward(byte[] data)
    {
        var list = InitAlphabet();
        var result = new byte[data.Length];

        for (var i = 0; i < data.Length; i++)
            result[i] = (byte)FindAndMoveToFront(list, data[i]);

        return result;
    }

    public static byte[] Inverse(byte[] indices)
    {
        var list = InitAlphabet();
        var result = new byte[indices.Length];

        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            var symbol = list[index];
            result[i] = symbol;
            ShiftToFront(list, index, symbol);
        }

        return result;
    }

    private static byte[] InitAlphabet()
    {
        var list = new byte[AlphabetSize];
        for (var i = 0; i < AlphabetSize; i++)
            list[i] = (byte)i;
        return list;
    }

    private static void ShiftToFront(byte[] list, int index, byte symbol)
    {
        if (index > 0)
        {
            Array.Copy(list, 0, list, 1, index);
            list[0] = symbol;
        }
    }

    private static int FindAndMoveToFront(byte[] list, byte symbol)
    {
        var index = 0;
        while (list[index] != symbol)
            index++;

        ShiftToFront(list, index, symbol);
        return index;
    }
}
