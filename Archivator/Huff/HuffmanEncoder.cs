using System.Text;
using Humanizer;

namespace Archivator.Huff;

public class HuffmanEncoder : IEncoder
{
    private const int BitsInByte = 8;

    public async Task Encode(string inputPath, string outputPath)
    {
        var fileExists = File.Exists(inputPath);

        if (fileExists is false)
        {
            Console.WriteLine($"Файл '{inputPath}' не удалось найти для кодирования");
            return;
        }

        var inputData = await File.ReadAllBytesAsync(inputPath);

        var (bwtData, bwtIndex) = BurrowsWheelerTransform.Forward(inputData);
        var mtfData = MoveToFront.Forward(bwtData);

        var frequencyTable = BuildFrequencyTable(mtfData);
        var (canonCodes, canonLengths, lengthTable) = CanonicalHuffman.BuildFromFrequencies(frequencyTable);

        var encodedBits = EncodeData(mtfData, canonCodes, canonLengths);
        var compressedData = ConvertBitStringToByteArray(encodedBits);

        WriteEncodedFile(outputPath, lengthTable, inputData.Length, bwtIndex, compressedData);

        var metrics = CalculateMetrics(inputData, encodedBits.Length);
        PrintMetrics(inputPath, metrics);
    }

    private static Dictionary<byte, int> BuildFrequencyTable(byte[] data)
    {
        var dict = new Dictionary<byte, int>();

        foreach (var b in data)
        {
            dict.TryAdd(b, 0);
            dict[b]++;
        }

        return dict;
    }

    private static string EncodeData(byte[] data, Dictionary<byte, uint> codes, Dictionary<byte, int> codeLengths)
    {
        var sb = new StringBuilder();

        foreach (var b in data)
        {
            var code = codes[b];
            var length = codeLengths[b];

            for (var i = length - 1; i >= 0; i--)
            {
                sb.Append((code >> i) & 1);
            }
        }

        return sb.ToString();
    }

    private static byte[] ConvertBitStringToByteArray(string bits)
    {
        var numBytes = (bits.Length + 7) / BitsInByte;
        var result = new byte[numBytes];

        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                result[i / BitsInByte] |= (byte) (1 << (BitsInByte - 1 - (i % BitsInByte)));
            }
        }

        return result;
    }

    private static void WriteEncodedFile(
        string outputPath,
        byte[] lengthTable,
        int originalLength,
        int bwtIndex,
        byte[] compressedData
    )
    {
        using var writer = new BinaryWriter(File.Open(outputPath, FileMode.Create));

        writer.Write(originalLength);
        writer.Write(bwtIndex);
        writer.Write(lengthTable);

        writer.Write(compressedData.Length);
        writer.Write(compressedData);

        Console.WriteLine($"Encoded file written to: {outputPath}");
    }

    private static CompressionMetrics CalculateMetrics(byte[] data, int totalEncodedBits)
    {
        const int BitsInByte = 8;

        var singleByteFreq = new Dictionary<byte, int>();
        var pairFreq = new Dictionary<byte, Dictionary<byte, int>>();
        var tripleFreq = new Dictionary<(byte, byte), Dictionary<byte, int>>();

        for (var i = 0; i < data.Length; i++)
        {
            var current = data[i];
            singleByteFreq.TryAdd(current, 0);
            singleByteFreq[current]++;

            if (i >= 1)
            {
                var prev = data[i - 1];
                if (!pairFreq.ContainsKey(prev))
                    pairFreq[prev] = new Dictionary<byte, int>();

                var dict = pairFreq[prev];
                dict.TryAdd(current, 0);
                dict[current]++;
            }

            if (i >= 2)
            {
                var prev1 = data[i - 2];
                var prev2 = data[i - 1];
                var key = (prev1, prev2);

                if (!tripleFreq.ContainsKey(key))
                    tripleFreq[key] = new Dictionary<byte, int>();

                var dict = tripleFreq[key];
                dict.TryAdd(current, 0);
                dict[current]++;
            }
        }

        var totalBytes = data.Length;
        var totalPairs = totalBytes - 1;
        var totalTriples = totalBytes - 2;

        // HX
        var h1 = -singleByteFreq.Values.Sum(
            v =>
            {
                var p = (double) v / totalBytes;

                return p * Math.Log2(p);
            }
        );

        // H(X|X)
        double hXgivenX = 0;

        foreach (var kvp in pairFreq)
        {
            var px = (double) kvp.Value.Values.Sum() / totalPairs;
            double h = 0;

            foreach (var count in kvp.Value.Values)
            {
                var p = (double) count / kvp.Value.Values.Sum();
                h += -p * Math.Log2(p);
            }

            hXgivenX += px * h;
        }

        // H(X|XX)
        double hXgivenXX = 0;

        foreach (var kvp in tripleFreq)
        {
            var pxx = (double) kvp.Value.Values.Sum() / totalTriples;
            double h = 0;

            foreach (var count in kvp.Value.Values)
            {
                var p = (double) count / kvp.Value.Values.Sum();
                h += -p * Math.Log2(p);
            }

            hXgivenXX += pxx * h;
        }

        return new CompressionMetrics
        {
            HX = h1,
            HX_X = hXgivenX,
            HX_XX = hXgivenXX,
            AvgBitsPerSymbol = (double) totalEncodedBits / totalBytes,
            InitialSizeBytes = totalBytes,
            CompressedSizeBytes = (totalEncodedBits + 7) / BitsInByte
        };
    }

    private static void PrintMetrics(string inputFile, CompressionMetrics metrics)
    {
        const int leftOffset = 16;
        const int rightOffset = 11;
        const string floatFormat = "F3";

        string Line(char left, char mid, char right, char fill) =>
            $"{left}{new string(fill, leftOffset + 1)}{mid}{new string(fill, rightOffset + 1)}{right}";

        string Row(string key, string value) => $"│ {key,-leftOffset}│{value,rightOffset} │";

        Console.WriteLine($"\nFile: {inputFile}");
        Console.WriteLine(Line('┌', '┬', '┐', '─'));
        Console.WriteLine(Row("Entropy H(X)", metrics.HX.ToString(floatFormat)));
        Console.WriteLine(Row("Entropy H(X|X)", metrics.HX_X.ToString(floatFormat)));
        Console.WriteLine(Row("Entropy H(X|XX)", metrics.HX_XX.ToString(floatFormat)));
        Console.WriteLine(Row("Avg bits/symbol", metrics.AvgBitsPerSymbol.ToString(floatFormat)));
        Console.WriteLine(Line('├', '┼', '┤', '─'));
        Console.WriteLine(Row("Initial size", metrics.InitialSizeBytes.Bytes().ToString()));
        Console.WriteLine(Row("Compressed size", metrics.CompressedSizeBytes.Bytes().ToString()));
        Console.WriteLine(Line('├', '┼', '┤', '─'));
        Console.WriteLine(Row("Compressed (%)", metrics.CompressionRatioPercent.ToString(floatFormat)));
        Console.WriteLine(Line('└', '┴', '┘', '─'));
    }

    private record CompressionMetrics
    {
        public double HX { get; init; }
        public double HX_X { get; init; }
        public double HX_XX { get; init; }
        public double AvgBitsPerSymbol { get; init; }
        public int InitialSizeBytes { get; init; }
        public int CompressedSizeBytes { get; init; }
        public double CompressionRatioPercent => 100 - (CompressedSizeBytes / (float) InitialSizeBytes * 100);
    }
}
