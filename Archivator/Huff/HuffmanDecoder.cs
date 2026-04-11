namespace Archivator.Huff;

public class HuffmanDecoder : IDecoder
{
    private const int BitsInByte = 8;

    public async Task Decode(string inputPath, string outputPath)
    {
        var fileExists = File.Exists(inputPath);

        if (fileExists is false)
        {
            Console.WriteLine($"Файл '{inputPath}' не удалось найти для декодирования");
            return;
        }

        using var reader = new BinaryReader(File.OpenRead(inputPath));

        var metadata = ReadMetadata(reader);
        var compressedData = reader.ReadBytes(metadata.CompressedDataLength);

        var (codes, codeLengths) = CanonicalHuffman.GenerateCanonicalCodes(metadata.LengthTable);
        var root = CanonicalHuffman.BuildDecodeTree(codes, codeLengths);

        var mtfData = DecodeHuffman(compressedData, root, metadata.OriginalByteLength, codeLengths.Count);
        var bwtData = MoveToFront.Inverse(mtfData);
        var decoded = BurrowsWheelerTransform.Inverse(bwtData, metadata.BwtIndex);

        await WriteDecodedFile(outputPath, decoded);
    }

    private static async Task WriteDecodedFile(string outputPath, byte[] decoded)
    {
        await File.WriteAllBytesAsync(outputPath, decoded);
        Console.WriteLine($"Decoded file written to: {outputPath}");
    }

    private ArchiveMetadata ReadMetadata(BinaryReader reader)
    {
        var originalByteLength = reader.ReadInt32();
        var bwtIndex = reader.ReadInt32();
        var lengthTable = reader.ReadBytes(256);

        var compressedDataLength = reader.ReadInt32();

        return new ArchiveMetadata(lengthTable, originalByteLength, bwtIndex, compressedDataLength);
    }

    private byte[] DecodeHuffman(
        byte[] compressedData,
        CanonicalHuffman.HuffmanNode root,
        int originalByteLength,
        int uniqueSymbolCount)
    {
        var output = new byte[originalByteLength];
        var bytesWritten = 0;

        if (uniqueSymbolCount == 1 && root.Left?.Symbol != null)
        {
            var symbol = root.Left.Symbol.Value;
            for (var i = 0; i < originalByteLength; i++)
                output[i] = symbol;
            return output;
        }

        var current = root;

        foreach (var b in compressedData)
        {
            for (var i = BitsInByte - 1; i >= 0; i--)
            {
                var bit = (b & (1 << i)) != 0;
                current = bit ? current?.Right : current?.Left;

                if (current?.Symbol is not { } symbol) continue;

                output[bytesWritten++] = symbol;
                current = root;

                if (bytesWritten >= originalByteLength) return output;
            }
        }

        return output;
    }

    private record ArchiveMetadata(
        byte[] LengthTable,
        int OriginalByteLength,
        int BwtIndex,
        int CompressedDataLength
    );
}
