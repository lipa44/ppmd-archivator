namespace Archivator.PPMd;

public class PpmdDecoder : IDecoder
{
    public async Task Decode(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Файл '{inputPath}' не удалось найти для декодирования");
            return;
        }

        using var inStream = File.OpenRead(inputPath);
        using var reader = new BinaryReader(inStream);

        var originalLength = reader.ReadInt32();
        var modelOrder = reader.ReadByte();
        var rescaleThreshold = reader.ReadUInt16();

        var model = new PpmModel(modelOrder, rescaleThreshold);
        var decoder = new RangeDecoder(inStream);

        var output = new byte[originalLength];
        for (var i = 0; i < originalLength; i++)
            output[i] = model.DecodeSymbol(decoder);

        await File.WriteAllBytesAsync(outputPath, output);
        Console.WriteLine($"Decoded file written to: {outputPath}");
    }
}