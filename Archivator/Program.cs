using System.Diagnostics;
using Archivator;
using Archivator.Huff;

if (args.Length < 3 ||
    (args.Length == 1 && args[0].Equals("TestData", StringComparison.InvariantCultureIgnoreCase) is false)
   )
{
    Console.WriteLine("Usage: [encoder|decoder] [inputPath] [outputPath]");

    return;
}

var command = args[0];

var sw = new Stopwatch();
sw.Start();

switch (command.ToLower())
{
    case "encoder":
        {
            string inputPath = args[1], outputPath = args[2];
            var encoder = new HuffmanEncoder();
            await encoder.Encode(inputPath, outputPath);

            break;
        }
    case "decoder":
        {
            string inputPath = args[1], outputPath = args[2];
            var decoder = new HuffmanDecoder();
            await decoder.Decode(inputPath, outputPath);

            break;
        }
    case "testdata":
        {
            const string basePath = "./../../../TestData/";
            var files = Directory.EnumerateFiles(basePath);

            foreach (var file in files.Where(x => x.Contains(".decoded") is false && x.Contains(".encoded") is false))
            {
                var huffmanFile = file + ".encoded";

                var encoder = new HuffmanEncoder();
                await encoder.Encode(file, huffmanFile);

                var decoder = new HuffmanDecoder();
                await decoder.Decode(huffmanFile, file + ".decoded");

                File.Delete(huffmanFile);
            }

            break;
        }
    default:
        Console.WriteLine($"Unknown command: {command}");
        Console.WriteLine("Usage: [encoder|decoder] [inputPath] [outputPath]");

        return;
}

var elapsed = sw.Elapsed;
Console.WriteLine($"Total elapsed time: {elapsed.TotalMilliseconds}ms");
