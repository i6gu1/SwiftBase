using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using SwiftBase.ViewModels;

namespace SwiftBase.Controls;

public partial class CodeEditor : UserControl
{
    private static readonly Brush SqlKeyword = Frozen("#5FC8EC");
    private static readonly Brush SqlString = Frozen("#F2A56F");
    private static readonly Brush SqlComment = Frozen("#567566");
    private static readonly Brush SqlNumber = Frozen("#F3C75C");
    private static readonly Brush SqlIdentifier = Frozen("#7BE0B7");
    private static readonly Brush JsonKey = Frozen("#5FC8EC");
    private static readonly Brush JsonString = Frozen("#8FD9A8");
    private static readonly Brush JsonNumber = Frozen("#F3C75C");
    private static readonly Brush JsonLiteral = Frozen("#F28A5B");

    private bool _syncing;
    private DocumentColorizingTransformer? _colorizer;
    private CompletionWindow? _completionWindow;

    public CodeEditor()
    {
        InitializeComponent();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.PreviewKeyDown += OnEditorPreviewKeyDown;
        Editor.TextArea.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => ApplyLanguage();
    }

    public event EventHandler? SelectionChanged;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(CodeEditor),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));

    public static readonly DependencyProperty SyntaxProperty = DependencyProperty.Register(
        nameof(Syntax),
        typeof(string),
        typeof(CodeEditor),
        new PropertyMetadata("sql", OnSyntaxChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(CodeEditor),
        new PropertyMetadata(false, OnReadOnlyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Syntax
    {
        get => (string)GetValue(SyntaxProperty);
        set => SetValue(SyntaxProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public string SelectedText => Editor.SelectedText;

    public IReadOnlyList<SqlCompletion> Completions { get; set; } = Array.Empty<SqlCompletion>();

    public void FocusEditor()
    {
        Editor.Focus();
        Editor.TextArea.Caret.Offset = Editor.Text.Length;
        Editor.ScrollToEnd();
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CodeEditor editor || editor._syncing)
        {
            return;
        }

        var text = e.NewValue as string ?? "";
        if (editor.Editor.Text == text)
        {
            return;
        }

        editor._syncing = true;
        var caret = editor.Editor.CaretOffset;
        editor.Editor.Text = text;
        editor.Editor.CaretOffset = Math.Min(caret, text.Length);
        editor._syncing = false;
    }

    private static void OnSyntaxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CodeEditor editor)
        {
            editor.ApplyLanguage();
        }
    }

    private static void OnReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CodeEditor editor)
        {
            editor.Editor.IsReadOnly = e.NewValue is true;
        }
    }

    private void ApplyLanguage()
    {
        if (_colorizer is not null)
        {
            Editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
            _colorizer = null;
        }

        var language = (Syntax ?? "sql").ToLowerInvariant();
        _colorizer = language switch
        {
            "json" => new JsonColorizer(),
            "sql" => new SqlColorizer(),
            _ => null,
        };

        if (_colorizer is not null)
        {
            Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        }

        Editor.TextArea.TextView.Redraw();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        SetCurrentValue(TextProperty, Editor.Text);
        _syncing = false;
    }

    private void OnTextEntering(object sender, TextCompositionEventArgs e)
    {
        if (_completionWindow is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var character = e.Text[0];
        if (!char.IsLetterOrDigit(character) && character != '_' && character != '.')
        {
            _completionWindow.Close();
        }
    }

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (Syntax?.Equals("sql", StringComparison.OrdinalIgnoreCase) != true || IsReadOnly)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var character = e.Text[0];
        if (!char.IsLetterOrDigit(character) && character != '_')
        {
            return;
        }

        var prefix = GetPrefix();
        if (prefix.Length < 2)
        {
            return;
        }

        ShowCompletions(prefix);
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowCompletions(GetPrefix());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _completionWindow is not null)
        {
            _completionWindow.Close();
            e.Handled = true;
        }
    }

    private string GetPrefix()
    {
        var offset = Editor.CaretOffset;
        var line = Editor.Document.GetLineByOffset(offset);
        var text = Editor.Document.GetText(line);
        var column = offset - line.Offset;
        var start = column;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        return text[start..column];
    }

    private void ShowCompletions(string prefix)
    {
        if (_completionWindow is not null || Completions.Count == 0)
        {
            return;
        }

        var matches = Completions
            .Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();

        if (matches.Count == 0)
        {
            return;
        }

        _completionWindow = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = Editor.CaretOffset - prefix.Length,
            Width = 330,
            MaxHeight = 250,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x1B, 0x28)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x40, 0x56)),
        };

        foreach (var match in matches)
        {
            _completionWindow.CompletionList.CompletionData.Add(new SqlCompletionData(match.Text, match.Description));
        }

        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed class SqlCompletionData : ICompletionData
    {
        public SqlCompletionData(string text, string description)
        {
            Text = text;
            Description = description;
        }

        public System.Windows.Media.ImageSource? Image => null;
        public string Text { get; }
        public object Content => Text;
        public object Description { get; }
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }

    private abstract class RegexColorizer : DocumentColorizingTransformer
    {
        protected abstract Regex Pattern { get; }
        protected abstract void Apply(Match match, Action<Action<VisualLineElement>> change);

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            foreach (Match match in Pattern.Matches(text))
            {
                var start = line.Offset + match.Index;
                var end = start + match.Length;
                ChangeLinePart(start, end, element => Apply(match, action => action(element)));
            }
        }

        protected static void Paint(VisualLineElement element, Brush brush, bool bold = false)
        {
            element.TextRunProperties.SetForegroundBrush(brush);
            if (bold)
            {
                element.TextRunProperties.SetTypeface(new Typeface(
                    element.TextRunProperties.Typeface.FontFamily,
                    FontStyles.Normal,
                    FontWeights.SemiBold,
                    FontStretches.Normal));
            }
        }
    }

    private sealed class SqlColorizer : RegexColorizer
    {
        private static readonly Regex SqlPattern = new(
            @"(?<comment>--.*$)|(?<string>'(?:[^']|'')*')|(?<ident>""(?:[^""]|"""")*"")|(?<number>\b\d+(?:\.\d+)?\b)|(?<keyword>\b(?:SELECT|FROM|WHERE|INSERT|INTO|VALUES|UPDATE|DELETE|SET|JOIN|LEFT|RIGHT|INNER|OUTER|ON|GROUP|ORDER|BY|HAVING|LIMIT|OFFSET|AS|AND|OR|NOT|NULL|IS|LIKE|IN|EXISTS|COUNT|SUM|AVG|MIN|MAX|DISTINCT|CASE|WHEN|THEN|ELSE|END|UNION|CREATE|ALTER|DROP|TABLE|VIEW|INDEX|BEGIN|COMMIT|ROLLBACK|RETURNING|WITH|ASC|DESC)\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        protected override Regex Pattern => SqlPattern;

        protected override void Apply(Match match, Action<Action<VisualLineElement>> change)
        {
            if (match.Groups["comment"].Success)
            {
                change(element => Paint(element, SqlComment));
            }
            else if (match.Groups["string"].Success)
            {
                change(element => Paint(element, SqlString));
            }
            else if (match.Groups["ident"].Success)
            {
                change(element => Paint(element, SqlIdentifier));
            }
            else if (match.Groups["number"].Success)
            {
                change(element => Paint(element, SqlNumber));
            }
            else if (match.Groups["keyword"].Success)
            {
                change(element => Paint(element, SqlKeyword, true));
            }
        }
    }

    private sealed class JsonColorizer : RegexColorizer
    {
        private static readonly Regex JsonPattern = new(
            @"(?<key>""(?:\\.|[^""\\])*""\s*:)|(?<string>""(?:\\.|[^""\\])*"")|(?<number>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)|(?<literal>\b(?:true|false|null)\b)",
            RegexOptions.Compiled);

        protected override Regex Pattern => JsonPattern;

        protected override void Apply(Match match, Action<Action<VisualLineElement>> change)
        {
            if (match.Groups["key"].Success)
            {
                change(element => Paint(element, JsonKey));
            }
            else if (match.Groups["string"].Success)
            {
                change(element => Paint(element, JsonString));
            }
            else if (match.Groups["number"].Success)
            {
                change(element => Paint(element, JsonNumber));
            }
            else if (match.Groups["literal"].Success)
            {
                change(element => Paint(element, JsonLiteral, true));
            }
        }
    }
}
