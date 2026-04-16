using System.Diagnostics;
using Archivator;
using Archivator.PPMd;

if (args.Length < 3 &&
    !(args.Length == 1 && args[0].Equals("TestData", StringComparison.InvariantCultureIgnoreCase))
   )
{
    Console.WriteLine("Usage: [encoder|decoder] [inputPath] [outputPath]");

    return;
}

const int modelOrder = 6;
var command = args[0];

var sw = new Stopwatch();
sw.Start();

switch (command.ToLower())
{
    case "encoder":
    {
        string inputPath = args[1], outputPath = args[2];
        IEncoder encoder = new PpmdEncoder(modelOrder);
        await encoder.Encode(inputPath, outputPath);

        break;
    }
    case "decoder":
    {
        string inputPath = args[1], outputPath = args[2];
        IDecoder decoder = new PpmdDecoder();
        await decoder.Decode(inputPath, outputPath);

        break;
    }
    case "testdata":
    {
        const string basePath = "./../../../TestData/";
        var files = Directory.EnumerateFiles(basePath);
        long totalCompressedSize = 0;

        foreach (var file in files.Where(x => x.Contains(".decoded") is false && x.Contains(".encoded") is false))
        {
            var huffmanFile = file + ".encoded";

            IEncoder encoder = new PpmdEncoder(modelOrder);
            await encoder.Encode(file, huffmanFile);

            var compressedSize = new FileInfo(huffmanFile).Length;
            totalCompressedSize += compressedSize;

            IDecoder decoder = new PpmdDecoder();
            await decoder.Decode(huffmanFile, file + ".decoded");

            var original = await File.ReadAllBytesAsync(file);
            var decoded = await File.ReadAllBytesAsync(file + ".decoded");
            var match = original.Length == decoded.Length && original.SequenceEqual(decoded);
            Console.WriteLine($"Bit-perfect decode: {(match ? "OK" : "MISMATCH")}");

            File.Delete(huffmanFile);
        }

        Console.WriteLine($"\nTotal compressed size: {totalCompressedSize} bytes");

        break;
    }
    default:
        Console.WriteLine($"Unknown command: {command}");
        Console.WriteLine("Usage: [encoder|decoder] [inputPath] [outputPath]");

        return;
}

var elapsed = sw.Elapsed;
Console.WriteLine($"Total elapsed time: {elapsed.TotalMilliseconds}ms");