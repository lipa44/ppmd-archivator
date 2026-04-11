namespace Archivator;

public static class CanonicalHuffman
{
    public static (Dictionary<byte, uint> Codes, Dictionary<byte, int> CodeLengths, byte[] LengthTable)
        BuildFromFrequencies(Dictionary<byte, int> freq)
    {
        if (freq.Count == 0)
            return (new Dictionary<byte, uint>(), new Dictionary<byte, int>(), new byte[256]);

        if (freq.Count == 1)
        {
            var symbol = freq.Keys.First();
            var lengthTable = new byte[256];
            lengthTable[symbol] = 1;
            var (codes, codeLengths) = GenerateCanonicalCodes(lengthTable);
            return (codes, codeLengths, lengthTable);
        }

        var tree = BuildHuffmanTree(freq);
        var rawLengths = new Dictionary<byte, int>();
        ComputeCodeLengths(tree, 0, rawLengths);

        var lengthTableResult = new byte[256];
        foreach (var (symbol, len) in rawLengths)
            lengthTableResult[symbol] = (byte)len;

        var (canonCodes, canonLengths) = GenerateCanonicalCodes(lengthTableResult);
        return (canonCodes, canonLengths, lengthTableResult);
    }

    public static (Dictionary<byte, uint> Codes, Dictionary<byte, int> CodeLengths)
        GenerateCanonicalCodes(byte[] lengthTable)
    {
        var codes = new Dictionary<byte, uint>();
        var codeLengths = new Dictionary<byte, int>();

        var symbols = new List<(byte Symbol, int Length)>();
        for (var i = 0; i < 256; i++)
        {
            if (lengthTable[i] > 0)
                symbols.Add(((byte)i, lengthTable[i]));
        }

        if (symbols.Count == 0)
            return (codes, codeLengths);

        symbols.Sort((a, b) =>
        {
            var cmp = a.Length.CompareTo(b.Length);
            return cmp != 0 ? cmp : a.Symbol.CompareTo(b.Symbol);
        });

        uint code = 0;
        var prevLength = symbols[0].Length;

        foreach (var (symbol, length) in symbols)
        {
            if (length > prevLength)
            {
                code <<= (length - prevLength);
                prevLength = length;
            }

            codes[symbol] = code;
            codeLengths[symbol] = length;
            code++;
        }

        return (codes, codeLengths);
    }

    public static HuffmanNode BuildDecodeTree(Dictionary<byte, uint> codes, Dictionary<byte, int> codeLengths)
    {
        var root = new HuffmanNode(null);

        foreach (var (symbol, codeValue) in codes)
        {
            var length = codeLengths[symbol];
            var current = root;

            for (var i = length - 1; i >= 0; i--)
            {
                var bit = (codeValue >> i) & 1;

                if (bit == 0)
                {
                    current.Left ??= new HuffmanNode(null);
                    current = current.Left;
                }
                else
                {
                    current.Right ??= new HuffmanNode(null);
                    current = current.Right;
                }
            }

            current.Symbol = symbol;
        }

        return root;
    }

    private static HuffmanNode BuildHuffmanTree(Dictionary<byte, int> freq)
    {
        var nodeId = 0;
        var pq = new PriorityQueue<HuffmanNode, (int Frequency, int Id)>();

        foreach (var (symbol, frequency) in freq.OrderBy(kv => kv.Key))
            pq.Enqueue(new HuffmanNode(symbol, frequency), (frequency, nodeId++));

        while (pq.Count > 1)
        {
            var left = pq.Dequeue();
            var right = pq.Dequeue();
            var parent = new HuffmanNode(null, left.Frequency + right.Frequency, left, right);
            pq.Enqueue(parent, (parent.Frequency, nodeId++));
        }

        return pq.Dequeue();
    }

    private static void ComputeCodeLengths(HuffmanNode node, int depth, Dictionary<byte, int> lengths)
    {
        if (node.Symbol.HasValue)
        {
            lengths[node.Symbol.Value] = Math.Max(depth, 1);
            return;
        }

        if (node.Left != null) ComputeCodeLengths(node.Left, depth + 1, lengths);
        if (node.Right != null) ComputeCodeLengths(node.Right, depth + 1, lengths);
    }

    public class HuffmanNode(byte? symbol, int frequency = 0, HuffmanNode? left = null, HuffmanNode? right = null)
    {
        public byte? Symbol { get; set; } = symbol;
        public int Frequency { get; } = frequency;
        public HuffmanNode? Left { get; set; } = left;
        public HuffmanNode? Right { get; set; } = right;
    }
}
