using Humanizer;

namespace Archivator.PPMd;

public class PpmdEncoder(
    int modelOrder = PpmModel.DefaultModelOrder,
    int rescaleThreshold = PpmModel.DefaultRescaleThreshold) : IEncoder
{
    public async Task Encode(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Файл '{inputPath}' не удалось найти для кодирования");
            return;
        }

        var inputData = await File.ReadAllBytesAsync(inputPath);
        var mostEfficientOrder = GetMostEfficientOrder(inputData);

        using var outStream = File.Create(outputPath);
        using var writer = new BinaryWriter(outStream);

        writer.Write(inputData.Length);
        writer.Write((byte) mostEfficientOrder);
        writer.Write((ushort) rescaleThreshold);
        writer.Flush();

        var model = new PpmModel(mostEfficientOrder, rescaleThreshold);
        var encoder = new RangeEncoder(outStream);

        for (var i = 0; i < inputData.Length; i++)
        {
            model.EncodeSymbol(inputData[i], encoder, writeToFile: true);
        }

        encoder.Flush(writeToFile: true);

        var compressedSize = outStream.Position;
        var compressionPercent = 100.0 - compressedSize / (double) inputData.Length * 100.0;

        Console.WriteLine($"\nFile: {inputPath}");
        Console.WriteLine($"  Initial size:    {inputData.Length.Bytes()}");
        Console.WriteLine($"  Compressed size: {((long) compressedSize).Bytes()}");
        Console.WriteLine($"  Compressed (%):  {compressionPercent:F3}");
        Console.WriteLine($"  Compressed file size: {compressedSize} bytes");
        Console.WriteLine($"  Model order D={mostEfficientOrder}");
    }

    private int GetMostEfficientOrder(byte[] inputData)
    {
        using var outStream = Stream.Null;
        var orderToEfficiencyMap = new Dictionary<int, ulong>();

        for (int modelOrder = 1; modelOrder < 8; modelOrder++)
        {
            var model = new PpmModel(modelOrder, rescaleThreshold);
            var encoder = new RangeEncoder(outStream);

            for (var i = 0; i < inputData.Length; i++)
            {
                model.EncodeSymbol(inputData[i], encoder, writeToFile: false);
            }

            orderToEfficiencyMap.Add(modelOrder, encoder.WrittenBytes);
        }

        var mostEfficientModel = orderToEfficiencyMap.MinBy(x => x.Value);

        return mostEfficientModel.Key;
    }
}