namespace Archivator.PPMd;

/// <summary>
/// Контекстная статистическая модель PPMD (Prediction by Partial Matching, вариант D).
/// </summary>
/// <remarks>
/// <para>
/// На каждой позиции файла модель пытается предсказать следующий байт, используя контекст фиксированной длины
/// (последние <c>maxOrder</c> байт). Если контекст уже встречал текущий байт - он кодируется по собственному
/// распределению контекста. Если нет - кодируется специальное событие "эскейп", и модель переходит к контексту на
/// один байт короче. И так до самого короткого. Если и корень не знает символа - включается <c>order −1</c>
/// (равномерное распределение по неисключённым байтам).
/// </para>
/// </remarks>
public sealed class PpmModel
{
    /// <summary>
    /// Мощность байтового алфавита: <c>2^8 = 256</c> возможных значений <c>byte</c>.
    /// </summary>
    private const int AlphabetSize = 256;

    /// <summary>
    /// Множитель в формуле виртуальных частот PPMD: <c>f(c) = 2*c − 1</c>.
    /// </summary>
    private const int VirtualFreqScale = 2;

    /// <summary>
    /// Константный вычет в формуле виртуальных частот PPMD: <c>f(c) = 2*c − 1</c>.
    /// </summary>
    private const int VirtualFreqOffset = 1;

    /// <summary>
    /// Величина сдвига при <see cref="Rescale"/>.
    /// </summary>
    private const int RescaleShift = 1;

    /// <summary>
    /// Порог rescale по умолчанию (<c>2^14 = 16384</c>): при <c>TotalCount &gt;= 16384</c> все счётчики контекста
    /// делятся пополам.
    /// </summary>
    public const int DefaultRescaleThreshold = 16384;

    /// <summary>
    /// Порядок модели по умолчанию. Эмпирически для текстов значения 4–6 дают лучшее соотношение сжатия и памяти.
    /// </summary>
    public const int DefaultModelOrder = 5;

    /// <summary>
    /// Виртуальная частота PPMD для символа со счётчиком <paramref name="count"/>: <c>f = 2*c − 1</c>.
    /// </summary>
    /// <param name="count">Реальный счётчик появлений символа в контексте.</param>
    /// <returns>Виртуальная частота для формулы PPMD.</returns>
    private static uint VirtualFrequency(int count) =>
        (uint) (VirtualFreqScale * count - VirtualFreqOffset);

    /// <summary>
    /// Максимальный порядок модели - длина учитываемого контекста в байтах.
    /// </summary>
    private readonly int _maxOrder;

    /// <summary>
    /// Корневой узел дерева - контекст порядка 0 ("пустой" контекст, общие частоты байт).
    /// </summary>
    private readonly ContextNode _root = new();

    /// <summary>
    /// Текущий путь: <c>_path[i]</c> - контекст длины <c>i</c>, заканчивающийся в ТЕКУЩЕЙ позиции файла.
    /// </summary>
    private readonly ContextNode?[] _path;

    /// <summary>
    /// Вспомогательный массив для построения пути на СЛЕДУЮЩУЮ позицию.
    /// </summary>
    private readonly ContextNode?[] _newPath;

    /// <summary>
    /// Маркер исключённых символов: <c>_excluded[b] == true</c> означает, что байт <c>b</c> уже встречался в одном
    /// из более длинных контекстов и на текущем шаге кодирования его не учитываем.
    /// </summary>
    private readonly bool[] _excluded = new bool[AlphabetSize];

    /// <summary>
    /// Порог, при достижении которого в контексте запускается <see cref="Rescale"/>
    /// </summary>
    private readonly int _rescaleThreshold;

    /// <summary>
    /// Создаёт новую модель PPMD.
    /// </summary>
    /// <param name="maxOrder">Максимальный порядок модели.</param>
    /// <param name="rescaleThreshold">Порог <c>TotalCount</c>, после которого счётчики контекста делятся на 2. По умолчанию <see cref="DefaultRescaleThreshold"/>.</param>
    public PpmModel(int maxOrder, int rescaleThreshold = DefaultRescaleThreshold)
    {
        _maxOrder = maxOrder;
        _rescaleThreshold = rescaleThreshold;
        _path = new ContextNode?[maxOrder + 1];
        _newPath = new ContextNode?[maxOrder + 1];
        _path[0] = _root;
    }

    /// <summary>
    /// Кодирует один символ.
    /// </summary>
    /// <param name="symbol">Байт, который нужно закодировать.</param>
    /// <param name="encoder">Арифметический кодер, в который отправляется результат.</param>
    /// <param name="writeToFile">Нужно ли кодеру записывать в файл.</param>
    public void EncodeSymbol(byte symbol, RangeEncoder encoder, bool writeToFile)
    {
        Array.Clear(_excluded, 0, AlphabetSize);

        for (var order = _maxOrder; order >= 0; order--)
        {
            var ctx = _path[order];

            if (ctx == null || ctx.TotalCount == 0)
                continue;

            uint activeTotal = 0, activeDistinct = 0;
            foreach (var (b, count) in ctx.Frequencies)
            {
                if (_excluded[b]) continue;

                activeTotal += (uint) count;
                activeDistinct++;
            }

            if (activeDistinct == 0) continue;

            var totalFreq = (uint) VirtualFreqScale * activeTotal;

            if (ctx.Frequencies.TryGetValue(symbol, out var symCount) && !_excluded[symbol])
            {
                uint cumFreq = 0;
                for (var b = 0; b < symbol; b++)
                {
                    if (ctx.Frequencies.TryGetValue((byte) b, out var c) && !_excluded[b])
                        cumFreq += VirtualFrequency(c);
                }

                encoder.Encode(cumFreq, VirtualFrequency(symCount), totalFreq, writeToFile);

                Update(symbol);
                return;
            }

            var symPart = (uint) VirtualFreqScale * activeTotal - activeDistinct;
            encoder.Encode(symPart, activeDistinct, totalFreq, writeToFile);

            foreach (var (b, _) in ctx.Frequencies)
                _excluded[b] = true;
        }

        // Равномерное распределение по всем неисключённым байтам.
        EncodeUniform(symbol, encoder, writeToFile);
        Update(symbol);
    }

    /// <summary>
    /// Декодирует один символ - зеркало <see cref="EncodeSymbol"/>.
    /// </summary>
    /// <param name="decoder">Арифметический декодер, из которого извлекается событие.</param>
    /// <returns>Декодированный байт.</returns>
    public byte DecodeSymbol(RangeDecoder decoder)
    {
        Array.Clear(_excluded, 0, AlphabetSize);

        for (var order = _maxOrder; order >= 0; order--)
        {
            var ctx = _path[order];
            if (ctx == null || ctx.TotalCount == 0)
                continue;

            uint activeTotal = 0, activeDistinct = 0;
            foreach (var (b, count) in ctx.Frequencies)
            {
                if (_excluded[b]) continue;

                activeTotal += (uint) count;
                activeDistinct++;
            }

            if (activeDistinct == 0) continue;

            var totalFreq = (uint) VirtualFreqScale * activeTotal;

            // В какую "единицу частоты" попал _code.
            var threshold = decoder.GetThreshold(totalFreq);

            var symPart = (uint) VirtualFreqScale * activeTotal - activeDistinct;

            if (threshold >= symPart)
            {
                decoder.Decode(symPart, activeDistinct);

                foreach (var (b, _) in ctx.Frequencies)
                    _excluded[b] = true;

                continue;
            }

            uint cumFreq = 0;
            for (var b = 0; b < AlphabetSize; b++)
            {
                if (!ctx.Frequencies.TryGetValue((byte) b, out var c) || _excluded[b])
                    continue;

                var freq = VirtualFrequency(c);

                if (cumFreq + freq > threshold)
                {
                    decoder.Decode(cumFreq, freq);
                    var sym = (byte) b;
                    Update(sym);

                    return sym;
                }

                cumFreq += freq;
            }

            throw new InvalidOperationException("PPM decode: символ не найден в контексте");
        }

        var symbol = DecodeUniform(decoder);

        Update(symbol);
        return symbol;
    }

    /// <summary>
    /// Кодирует <paramref name="symbol"/> в <c>order −1</c>: равномерное распределение по всем <c>256 −
    /// (число исключённых)</c> байтам.
    /// </summary>
    /// <param name="symbol">Байт, который нужно закодировать.</param>
    /// <param name="encoder">Арифметический кодер.</param>
    /// <param name="writeToFile">Нужно ли кодеру записывать в файл.</param>
    private void EncodeUniform(byte symbol, RangeEncoder encoder, bool writeToFile)
    {
        uint activeSymbolCount = 0, pos = 0;
        for (var b = 0; b < AlphabetSize; b++)
        {
            if (_excluded[b]) continue;
            if (b < symbol) pos++;
            activeSymbolCount++;
        }

        // Кодируем symbol с частотой 1 из remaining - равномерное распределение
        encoder.Encode(pos, 1, activeSymbolCount, writeToFile);
    }

    /// <summary>
    /// Декодирует символ в <c>order −1</c> - зеркало <see cref="EncodeUniform"/>.
    /// </summary>
    /// <param name="decoder">Арифметический декодер.</param>
    /// <returns>Декодированный байт.</returns>
    private byte DecodeUniform(RangeDecoder decoder)
    {
        uint activeSymbolCount = 0;
        for (var b = 0; b < AlphabetSize; b++)
        {
            if (!_excluded[b]) activeSymbolCount++;
        }

        var threshold = decoder.GetThreshold(activeSymbolCount);

        uint pos = 0;
        for (var b = 0; b < AlphabetSize; b++)
        {
            if (_excluded[b]) continue;
            if (pos == threshold)
            {
                decoder.Decode(pos, 1);
                return (byte) b;
            }

            pos++;
        }

        throw new InvalidOperationException("Порядок -1: декодирование не удалось");
    }

    /// <summary>
    /// Обновляет модель после обработки одного символа.
    /// </summary>
    /// <param name="symbol">Только что обработанный байт.</param>
    private void Update(byte symbol)
    {
        // Фаза 1: инкрементируем счётчик symbol в каждом контексте текущего пути.
        for (var order = 0; order <= _maxOrder; order++)
        {
            var ctx = _path[order];
            if (ctx == null) break;

            ctx.Frequencies.TryGetValue(symbol, out var count);
            ctx.Frequencies[symbol] = count + 1;
            ctx.TotalCount++;

            if (ctx.TotalCount >= _rescaleThreshold)
                Rescale(ctx);
        }

        // Фаза 2: строим путь для следующей позиции.
        _newPath[0] = _root;
        for (var order = 1; order <= _maxOrder; order++)
        {
            var parent = _path[order - 1];
            if (parent == null)
            {
                _newPath[order] = null;
                continue;
            }

            if (!parent.Children.TryGetValue(symbol, out var child))
            {
                child = new ContextNode();
                parent.Children[symbol] = child;
            }

            _newPath[order] = child;
        }

        Array.Copy(_newPath, _path, _maxOrder + 1);
    }

    /// <summary>
    /// Делит все счётчики контекста на 2.
    /// </summary>
    /// <param name="ctx">Контекст, в котором надо уполовинить счётчики.</param>
    private static void Rescale(ContextNode ctx)
    {
        var keys = new byte[ctx.Frequencies.Count];
        ctx.Frequencies.Keys.CopyTo(keys, 0);

        ctx.TotalCount = 0;
        foreach (var b in keys)
        {
            var newCount = ctx.Frequencies[b] >> RescaleShift;
            if (newCount == 0)
                ctx.Frequencies.Remove(b);
            else
            {
                ctx.Frequencies[b] = newCount;
                ctx.TotalCount += newCount;
            }
        }
    }
}

/// <summary>
/// Узел контекстного дерева PPMD: хранит частоты символов, виденных после данного контекста, и ссылки на дочерние контексты.
/// </summary>
public sealed class ContextNode
{
    /// <summary>
    /// Сумма значений <see cref="Frequencies"/>, кэшируется, чтобы не пересчитывать каждый раз.
    /// </summary>
    public int TotalCount;

    /// <summary>
    /// Счётчики: <c>Frequencies[b]</c> - сколько раз байт <c>b</c> встречался после этого контекста.
    /// </summary>
    public readonly Dictionary<byte, int> Frequencies = new();

    /// <summary>
    /// Дочерние узлы - более длинные контексты: <c>Children[b]</c> соответствует контексту,
    /// продолженному байтом <c>b</c>.
    /// </summary>
    public readonly Dictionary<byte, ContextNode> Children = new();
}