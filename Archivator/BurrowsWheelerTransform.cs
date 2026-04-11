namespace Archivator;

public static class BurrowsWheelerTransform
{
    public static (byte[] Transformed, int OriginalIndex) Forward(byte[] data)
    {
        if (data.Length == 0)
            return (Array.Empty<byte>(), 0);

        var n = data.Length;
        var sa = BuildRotationArray(data);

        var transformed = new byte[n];
        var originalIndex = 0;

        for (var i = 0; i < n; i++)
        {
            if (sa[i] == 0)
            {
                originalIndex = i;
                transformed[i] = data[n - 1];
            }
            else
            {
                transformed[i] = data[sa[i] - 1];
            }
        }

        return (transformed, originalIndex);
    }

    public static byte[] Inverse(byte[] lastColumn, int originalIndex)
    {
        if (lastColumn.Length == 0)
            return Array.Empty<byte>();

        var n = lastColumn.Length;

        var count = new int[256];
        foreach (var b in lastColumn)
            count[b]++;

        var cumCount = new int[256];
        var sum = 0;
        for (var i = 0; i < 256; i++)
        {
            cumCount[i] = sum;
            sum += count[i];
        }

        var lf = new int[n];
        var tempCount = new int[256];
        Array.Copy(cumCount, tempCount, 256);

        for (var i = 0; i < n; i++)
        {
            var c = lastColumn[i];
            lf[i] = tempCount[c];
            tempCount[c]++;
        }

        var result = new byte[n];
        var idx = originalIndex;

        for (var i = n - 1; i >= 0; i--)
        {
            result[i] = lastColumn[idx];
            idx = lf[idx];
        }

        return result;
    }

    private static int[] BuildRotationArray(byte[] data)
    {
        var n = data.Length;
        var sa = new int[n];
        var rank = new int[n];
        var newRank = new int[n];

        for (var i = 0; i < n; i++)
        {
            sa[i] = i;
            rank[i] = data[i];
        }

        for (var k = 1; k < n; k <<= 1)
        {
            var kCapture = k;
            var rankCapture = rank;

            Array.Sort(sa, (a, b) =>
            {
                if (rankCapture[a] != rankCapture[b])
                    return rankCapture[a].CompareTo(rankCapture[b]);

                var ra = rankCapture[(a + kCapture) % n];
                var rb = rankCapture[(b + kCapture) % n];

                return ra.CompareTo(rb);
            });

            newRank[sa[0]] = 0;

            for (var i = 1; i < n; i++)
            {
                newRank[sa[i]] = newRank[sa[i - 1]];

                if (rankCapture[sa[i]] != rankCapture[sa[i - 1]] ||
                    rankCapture[(sa[i] + kCapture) % n] !=
                    rankCapture[(sa[i - 1] + kCapture) % n])
                {
                    newRank[sa[i]]++;
                }
            }

            Array.Copy(newRank, rank, n);

            if (rank[sa[n - 1]] == n - 1)
                break;
        }

        return sa;
    }
}