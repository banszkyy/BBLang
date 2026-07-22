namespace LanguageCore;

public class ConsoleLogger : ILogger
{
    public bool LogDebugs { get; init; }
    public bool LogInfos { get; init; }
    public bool LogWarnings { get; init; }
    public bool EnableProgress { get; init; }

    public static readonly ConsoleLogger Default = new()
    {
        LogDebugs = false,
        LogInfos = true,
        LogWarnings = true,
        EnableProgress = false,
    };

    const ConsoleColor InfoColor = ConsoleColor.Blue;
    const ConsoleColor WarningColor = ConsoleColor.DarkYellow;
    const ConsoleColor ErrorColor = ConsoleColor.Red;
    const ConsoleColor DebugColor = ConsoleColor.DarkGray;

    bool IsOn(LogType level) => level switch
    {
        LogType.Normal => LogInfos,
        LogType.Warning => LogWarnings,
        LogType.Error => true,
        LogType.Debug => LogDebugs,
        _ => false,
    };

    public void Log(LogType level, string message)
    {
        switch (level)
        {
            case LogType.Normal: LogInfo(message); break;
            case LogType.Warning: LogWarning(message); break;
            case LogType.Error: LogError(message); break;
            case LogType.Debug: LogDebug(message); break;
        }
    }

    public void LogInfo(string message)
    {
        if (LogInfos) Log(message, InfoColor);
    }

    public void LogError(string message) => Log(message, ErrorColor);

    public void LogError(LanguageExceptionAt exception, IEnumerable<ISourceProvider>? sourceProviders = null)
    {
        Console.ForegroundColor = ErrorColor;
        Console.WriteLine(exception.ToString());

        (string SourceCode, string Arrows)? arrows = exception.GetArrows(sourceProviders);
        if (arrows.HasValue)
        {
            Console.WriteLine(arrows.Value.SourceCode);
            Console.WriteLine(arrows.Value.Arrows);
        }
        Console.ResetColor();
    }

    public void LogError(Exception exception)
    {
        Console.ForegroundColor = ErrorColor;
        Console.WriteLine(exception.ToString());
        Console.ResetColor();
    }

    public void LogWarning(string message)
    {
        if (LogWarnings) Log(message, WarningColor);
    }

    public void LogDebug(string message)
    {
        if (LogDebugs) Log(message, DebugColor);
    }

    static void Log(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void LogDiagnostic(DiagnosticAt diagnostic, IEnumerable<ISourceProvider>? sourceProviders = null)
        => LogDiagnostic(diagnostic, 0, sourceProviders);

    public void LogDiagnostic(Diagnostic diagnostic, IEnumerable<ISourceProvider>? sourceProviders = null)
        => LogDiagnostic(diagnostic, 0, sourceProviders);

    void LogDiagnostic(Diagnostic diagnostic, int depth, IEnumerable<ISourceProvider>? sourceProviders = null, Diagnostic? parent = null)
    {
        DiagnosticsLevel level = diagnostic.Level;

        if (parent is not null && parent.Level > level)
        {
            level = parent.Level;
        }

        if (!(level switch
        {
            DiagnosticsLevel.Error => true,
            DiagnosticsLevel.Warning => LogWarnings,
            DiagnosticsLevel.Information => LogInfos,
            DiagnosticsLevel.Hint => LogInfos,
            DiagnosticsLevel.OptimizationNotice => false,
            DiagnosticsLevel.FailedOptimization => false,
            _ => false,
        }))
        { return; }

        Console.Write(new string(' ', depth * 2));

        Console.ForegroundColor = level switch
        {
            DiagnosticsLevel.Error => ErrorColor,
            DiagnosticsLevel.Warning => WarningColor,
            DiagnosticsLevel.Information => InfoColor,
            DiagnosticsLevel.Hint => InfoColor,
            DiagnosticsLevel.OptimizationNotice => DebugColor,
            DiagnosticsLevel.FailedOptimization => WarningColor,
            _ => DebugColor,
        };

        Console.WriteLine(diagnostic.ToString());

        if (diagnostic is DiagnosticAt diagnosticAt)
        {
            (string SourceCode, string Arrows)? arrows = diagnosticAt.GetArrows(sourceProviders);
            if (arrows.HasValue)
            {
                Console.Write(new string(' ', depth * 2));
                Console.WriteLine(arrows.Value.SourceCode);
                Console.Write(new string(' ', depth * 2));
                Console.WriteLine(arrows.Value.Arrows);
            }
        }

        if (diagnostic.SubErrors.Length > 0)
        {
            Console.Write(new string(' ', depth * 2));
            Console.WriteLine("Caused by:");
        }

        Console.ResetColor();

        foreach (Diagnostic subdiagnostic in diagnostic.SubErrors)
        { LogDiagnostic(subdiagnostic, depth + 1, sourceProviders, diagnostic); }
    }

    public IDisposableProgress<float> Progress(LogType level) => EnableProgress && IsOn(level) ? new ConsoleProgressBar(level switch
    {
        LogType.Error => ErrorColor,
        LogType.Warning => WarningColor,
        LogType.Normal => ConsoleColor.White,
        LogType.Debug => DebugColor,
        _ => ConsoleColor.White
    }) : VoidProgress<float>.Instance;

    public IDisposableProgress<string> Label(LogType level) => EnableProgress && IsOn(level) ? new ConsoleProgressLabel(string.Empty, level switch
    {
        LogType.Error => ErrorColor,
        LogType.Warning => WarningColor,
        LogType.Normal => ConsoleColor.White,
        LogType.Debug => DebugColor,
        _ => ConsoleColor.White
    }, true) : VoidProgress<string>.Instance;

    [ExcludeFromCodeCoverage]
    readonly struct ConsoleSpinner
    {
        const double Speed = 5d;

        readonly ImmutableArray<char> _characters;
        readonly double _time;

        public char Current => _characters[(int)((DateTime.UtcNow.TimeOfDay.TotalSeconds - _time) * Speed) % _characters.Length];

        public ConsoleSpinner(ImmutableArray<char> characters)
        {
            _characters = characters;
            _time = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        }
    }

    [ExcludeFromCodeCoverage]
    class ConsoleProgressBar : IDisposableProgress<float>
    {
        readonly int _line;
        readonly ConsoleColor _color;
        readonly double _time;

        bool _isFast;
        float _progress;
        float _printedProgress;

        public ConsoleProgressBar(ConsoleColor color)
        {
            _line = 0;
            _color = color;
            _progress = 0f;
            _printedProgress = 0f;
            _time = DateTime.UtcNow.TimeOfDay.TotalSeconds;
            _isFast = true;
            _line = Console.CursorTop;

            Console.WriteLine();
        }

        public void Print()
        {
            if (_isFast)
            {
                if (DateTime.UtcNow.TimeOfDay.TotalSeconds - _time > .2f)
                { _isFast = false; }
                else
                { return; }
            }

            if ((int)(_printedProgress * Console.WindowWidth) == (int)(_progress * Console.WindowWidth))
            { return; }

            (int prevLeft, int prevTop) = (Console.CursorLeft, Console.CursorTop);

            Console.SetCursorPosition(0, _line);

            int width = Console.WindowWidth;
            Console.ForegroundColor = _color;
            for (int i = 0; i < width; i++)
            {
                float v = (float)(i + 1) / (float)width;
                if (v <= _progress)
                { Console.Write('═'); }
                else
                { Console.Write(' '); }
            }
            Console.ResetColor();

            Console.SetCursorPosition(prevLeft, prevTop);

            _printedProgress = _progress;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            (int prevLeft, int prevTop) = (Console.CursorLeft, Console.CursorTop);

            Console.SetCursorPosition(0, _line);

            int width = Console.WindowWidth;
            for (int i = 0; i < width; i++)
            { Console.Write(' '); }

            Console.SetCursorPosition(prevLeft, prevTop - 1);
        }

        public void Report(float value)
        {
            _progress = value;
            Print();
        }
    }

    [ExcludeFromCodeCoverage]
    class ConsoleProgressLabel : IDisposableProgress<string>
    {
        public string Label { get; set; }

        readonly int _line;
        readonly ConsoleColor _color;
        readonly ConsoleSpinner _spinner;
        readonly bool _showSpinner;
        readonly double _time;

        bool _isNotFirst;
        bool _isFast;

        static ImmutableArray<char> SpinnerCharacters { get; } = ImmutableArray.Create('-', '\\', '|', '/');

        public ConsoleProgressLabel(string label, ConsoleColor color, bool showSpinner = false)
        {
            Label = label;

            _line = 0;
            _color = color;
            if (showSpinner)
            {
                _showSpinner = showSpinner;
                _spinner = new ConsoleSpinner(SpinnerCharacters);
            }
            _time = DateTime.UtcNow.TimeOfDay.TotalSeconds;
            _isFast = true;
            _isNotFirst = false;
            _line = Console.CursorTop;

            Console.WriteLine();
        }

        public void Report(string value)
        {
            Label = value;
            Print();
        }

        public void Print()
        {
            if (_isFast && _isNotFirst)
            {
                if (DateTime.UtcNow.TimeOfDay.TotalSeconds - _time > .2f)
                { _isFast = false; }
                else
                { return; }
            }
            _isNotFirst = true;

            (int prevLeft, int prevTop) = (Console.CursorLeft, Console.CursorTop);

            Console.SetCursorPosition(0, _line);

            int width = Console.WindowWidth;
            Console.ForegroundColor = _color;

            if (_showSpinner && width > 2)
            {
                Console.Write(_spinner.Current);
                Console.Write(' ');
                width -= 2;
            }

            for (int i = 0; i < width; i++)
            {
                if (i < Label.Length)
                { Console.Write(Label[i]); }
                else
                { Console.Write(' '); }
            }
            Console.ResetColor();

            Console.SetCursorPosition(prevLeft, prevTop);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            (int prevLeft, int prevTop) = (Console.CursorLeft, Console.CursorTop);

            Console.SetCursorPosition(0, _line);

            int width = Console.WindowWidth;
            for (int i = 0; i < width; i++)
            { Console.Write(' '); }

            Console.SetCursorPosition(prevLeft, prevTop - 1);
        }
    }
}
