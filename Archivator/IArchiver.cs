namespace Archivator;

public interface IEncoder
{
    Task Encode(string inputPath, string outputPath);
}

public interface IDecoder
{
    Task Decode(string inputPath, string outputPath);
}