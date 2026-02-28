using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Dual-layer code editor with HLSL syntax highlighting, line numbers,
    /// Tab/Shift-Tab indentation, Ctrl-Z/Y undo-redo, and hooks for
    /// autocomplete, inline-LLM and context-menu extensions.
    ///
    /// Architecture:
    ///   ScrollView
    ///     _root (row)
    ///       _lineNumbers (Label, left gutter)
    ///       _editorArea (relative container)
    ///         _editor (TextField, transparent text, z-top so caret is visible)
    ///         _highlight (Label, rich-text, absolute, pickingMode=Ignore, z-below editor)
    ///
    /// The editor text is fully transparent; the highlight label paints the
    /// colored text underneath.  The caret remains visible via --unity-cursor-color.
    /// Because _highlight is added AFTER _editor but has position:absolute, it
    /// actually renders in the same area.  pickingMode=Ignore lets all pointer
    /// events pass through to the TextField.
    /// </summary>
    public class CodeEditorView
    {
        // ---- UI elements ----
        readonly VisualElement _root;
        readonly VisualElement _editorArea;
        readonly Label _lineNumbers;
        readonly TextField _editor;
        readonly Label _highlight;
        readonly ScrollView _scrollView;

        // ---- Undo / Redo ----
        readonly List<string> _undoStack = new List<string>();
        readonly List<string> _redoStack = new List<string>();
        const int MaxUndoEntries = 200;
        bool _isUndoRedoAction;
        IVisualElementScheduledItem _debouncedRecord;
        string _pendingSnapshot;
        const long DebounceMs = 400;

        // ---- Events ----
        public event Action<string> OnCodeChanged;
        public event Action OnInlineLLMRequested;
        public event Action<ContextualMenuPopulateEvent> OnContextMenu;

        // ---- Regex for syntax highlighting ----
        static readonly Regex RePreprocessor = new Regex(
            @"^\s*(#\w+.*)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex ReKeywords = new Regex(
            @"\b(void|float|float2|float3|float4|float4x4|half|half2|half3|half4|" +
            @"int|int2|int3|int4|uint|uint2|uint3|uint4|bool|" +
            @"return|if|else|for|while|do|switch|case|break|continue|discard|" +
            @"struct|inout|out|in|uniform|const|static|inline|" +
            @"sampler|Texture2D|Texture3D|TextureCube|SamplerState|" +
            @"CBUFFER_START|CBUFFER_END|TEXTURE2D|TEXTURECUBE|SAMPLER|" +
            @"HLSLPROGRAM|ENDHLSL|pragma|include|define)\b",
            RegexOptions.Compiled);

        static readonly Regex ReTypes = new Regex(
            @"\b(Attributes|Varyings|InputData|SurfaceData|Light|" +
            @"VertexPositionInputs|VertexNormalInputs|SurfaceDescription)\b",
            RegexOptions.Compiled);

        static readonly Regex ReFunctions = new Regex(
            @"\b(saturate|normalize|dot|cross|lerp|clamp|sin|cos|tan|atan2|abs|pow|sqrt|rsqrt|" +
            @"mul|min|max|step|smoothstep|frac|floor|ceil|round|sign|length|distance|reflect|refract|" +
            @"ddx|ddy|fwidth|clip|" +
            @"TransformObjectToWorldNormal|TransformObjectToWorld|GetVertexPositionInputs|" +
            @"GetVertexNormalInputs|GetWorldSpaceNormalizeViewDir|GetWorldSpaceViewDir|" +
            @"UniversalFragmentPBR|UniversalFragmentBlinnPhong|" +
            @"ComputeFogFactor|MixFog|ComputeScreenPos|GetShadowCoord|" +
            @"SAMPLE_TEXTURE2D|TRANSFORM_TEX|LOAD_TEXTURE2D)\b",
            RegexOptions.Compiled);

        static readonly Regex ReComments = new Regex(
            @"(//.*$)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex ReAILabTags = new Regex(
            @"(//\s*\[AILab_\w+[^\]]*\])",
            RegexOptions.Compiled);

        static readonly Regex ReStrings = new Regex(
            @"""([^""\\]|\\.)*""",
            RegexOptions.Compiled);

        static readonly Regex ReNumbers = new Regex(
            @"\b(\d+\.?\d*[fh]?)\b",
            RegexOptions.Compiled);

        // ---- Colors (VS Code Dark+ inspired) ----
        const string ColKeyword   = "#569CD6";
        const string ColType      = "#4EC9B0";
        const string ColFunction  = "#DCDCAA";
        const string ColComment   = "#6A9955";
        const string ColAILabTag  = "#C586C0";
        const string ColNumber    = "#B5CEA8";
        const string ColString    = "#CE9178";
        const string ColPreproc   = "#C586C0";

        // Unicode look-alikes for < and > that won't be parsed as rich-text tags
        const char AngleBracketLeft  = '\u2039';  // single left-pointing angle quotation mark  ‹
        const char AngleBracketRight = '\u203A';  // single right-pointing angle quotation mark ›

        public CodeEditorView(ScrollView scrollView, TextField editor)
        {
            _scrollView = scrollView;
            _editor = editor;

            _root = new VisualElement();
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.flexGrow = 1;
            _root.style.minHeight = 200;

            // Line numbers
            _lineNumbers = new Label("  1");
            _lineNumbers.AddToClassList("code-line-numbers");
            _lineNumbers.pickingMode = PickingMode.Ignore;
            _root.Add(_lineNumbers);

            // Editor area (overlay container)
            _editorArea = new VisualElement();
            _editorArea.AddToClassList("code-editor-area");
            _root.Add(_editorArea);

            // Highlighted label FIRST in DOM (paints underneath the editor).
            // position:absolute so it occupies the same space.
            _highlight = new Label();
            _highlight.AddToClassList("code-highlight-layer");
            _highlight.enableRichText = true;
            _highlight.pickingMode = PickingMode.Ignore;
            _editorArea.Add(_highlight);

            // Move the actual TextField on top (later in DOM = higher z).
            // Its text is transparent; only the caret is visible.
            if (_editor.parent != null)
                _editor.parent.Remove(_editor);
            _editor.AddToClassList("code-editor-input");
            _editorArea.Add(_editor);

            // Force every internal layer of the TextField to be transparent
            // so the highlight label shows through. Also apply the mono font.
            _editor.schedule.Execute(() =>
            {
                MakeFullyTransparent(_editor);
                ApplyMonoFont();
            }).StartingIn(10);

            // Re-apply after Unity finishes internal layout passes
            _editor.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                MakeFullyTransparent(_editor);
                ApplyMonoFont();
            });

            // Replace the ScrollView content with our custom layout
            _scrollView.Clear();
            _scrollView.Add(_root);

            WireEvents();
            RefreshHighlight();
        }

        /// <summary>
        /// Walk the visual tree of a TextField and force every child element
        /// to have a transparent background, so the highlight label behind
        /// (or in front, with pickingMode=Ignore) is fully visible.
        /// </summary>
        static void MakeFullyTransparent(VisualElement root)
        {
            root.style.backgroundColor = Color.clear;
            foreach (var child in root.Children())
            {
                child.style.backgroundColor = Color.clear;
                MakeFullyTransparent(child);
            }
        }

        /// <summary>
        /// Apply a monospace font to the editor, highlight layer, and line numbers.
        /// Loads FiraMono from project, falling back to system monospace fonts.
        /// </summary>
        void ApplyMonoFont()
        {
            if (_monoFont == null)
                _monoFont = LoadMonoFont();
            if (_monoFont == null) return;

            var style = new StyleFont(_monoFont);
            _highlight.style.unityFont = style;
            _lineNumbers.style.unityFont = style;

            // Force font on every text-rendering element inside the TextField
            ApplyFontRecursive(_editor, _monoFont);
        }

        static void ApplyFontRecursive(VisualElement root, Font font)
        {
            if (root is TextElement te)
                te.style.unityFont = new StyleFont(font);

            foreach (var child in root.Children())
                ApplyFontRecursive(child, font);
        }

        static Font _monoFont;

        static Font LoadMonoFont()
        {
            // 1) Load from project assets (FiraMono shipped with the package)
            string[] assetPaths =
            {
                "Assets/ShaderAILab/Fonts/FiraMono-Regular.ttf",
                "Assets/ShaderAILab/Fonts/FiraMono-Medium.ttf",
            };
            foreach (var path in assetPaths)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (font != null) return font;
            }

            // 2) Fallback to system monospace fonts
            string[] systemFonts = { "Consolas", "Courier New", "Lucida Console" };
            // Get the list of actual OS font names to verify existence
            var installedFonts = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var name in systemFonts)
            {
                if (installedFonts.Contains(name))
                    return Font.CreateDynamicFontFromOSFont(name, 12);
            }

            return null;
        }

        public string Code
        {
            get => _editor?.value ?? "";
            set
            {
                if (_editor != null)
                {
                    _editor.SetValueWithoutNotify(value);
                    RefreshHighlight();
                    RecordUndoSnapshot(value);
                }
            }
        }

        public TextField Editor => _editor;
        public VisualElement Root => _root;

        public int CursorIndex => _editor?.cursorIndex ?? 0;
        public int SelectionStart => _editor != null ? Math.Min(_editor.cursorIndex, _editor.selectIndex) : 0;
        public int SelectionEnd => _editor != null ? Math.Max(_editor.cursorIndex, _editor.selectIndex) : 0;
        public string SelectedText
        {
            get
            {
                if (_editor == null) return "";
                int start = SelectionStart;
                int end = SelectionEnd;
                if (start == end || start < 0 || end > _editor.value.Length) return "";
                return _editor.value.Substring(start, end - start);
            }
        }

        // ---- Wiring ----

        void WireEvents()
        {
            _editor.RegisterValueChangedCallback(OnValueChanged);
            _editor.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _editor.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                OnContextMenu?.Invoke(evt);
            });
            // Re-apply transparency whenever the editor rebuilds its internal elements
            _editor.RegisterCallback<GeometryChangedEvent>(_ => MakeFullyTransparent(_editor));
        }

        void OnValueChanged(ChangeEvent<string> evt)
        {
            RefreshHighlight();

            if (!_isUndoRedoAction)
                ScheduleDebouncedRecord(evt.newValue);

            OnCodeChanged?.Invoke(evt.newValue);
        }

        // ---- Keyboard handling (Tab, Shift+Tab, Ctrl+Z, Ctrl+Y, Ctrl+Shift+Space) ----

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.Space)
            {
                evt.StopPropagation();
                evt.PreventDefault();
                OnInlineLLMRequested?.Invoke();
                return;
            }

            if (evt.ctrlKey && !evt.shiftKey && evt.keyCode == KeyCode.Z)
            {
                evt.StopPropagation();
                evt.PreventDefault();
                Undo();
                return;
            }

            if (evt.ctrlKey && !evt.shiftKey && evt.keyCode == KeyCode.Y)
            {
                evt.StopPropagation();
                evt.PreventDefault();
                Redo();
                return;
            }

            if (evt.keyCode == KeyCode.Tab)
            {
                evt.StopPropagation();
                evt.PreventDefault();

                if (evt.shiftKey)
                    HandleShiftTab();
                else
                    HandleTab();
                return;
            }
        }

        // ---- Tab / Shift-Tab ----

        void HandleTab()
        {
            string text = _editor.value;
            int cursorIdx = _editor.cursorIndex;
            int selectIdx = _editor.selectIndex;
            int selStart = Math.Min(cursorIdx, selectIdx);
            int selEnd = Math.Max(cursorIdx, selectIdx);

            if (selStart == selEnd)
            {
                string newText = text.Insert(cursorIdx, "    ");
                _editor.value = newText;
                _editor.SelectRange(cursorIdx + 4, cursorIdx + 4);
            }
            else
            {
                int lineStart = FindLineStart(text, selStart);
                int lineEnd = FindLineEnd(text, selEnd);

                string region = text.Substring(lineStart, lineEnd - lineStart);
                string[] lines = region.Split('\n');
                string indented = string.Join("\n", IndentLines(lines, "    "));

                string newText = text.Substring(0, lineStart) + indented + text.Substring(lineEnd);
                _editor.value = newText;
                _editor.SelectRange(selStart + 4, selEnd + 4 * lines.Length);
            }
        }

        void HandleShiftTab()
        {
            string text = _editor.value;
            int cursorIdx = _editor.cursorIndex;
            int selectIdx = _editor.selectIndex;
            int selStart = Math.Min(cursorIdx, selectIdx);
            int selEnd = Math.Max(cursorIdx, selectIdx);

            int lineStart = FindLineStart(text, selStart);
            int lineEnd = FindLineEnd(text, selEnd);

            string region = text.Substring(lineStart, lineEnd - lineStart);
            string[] lines = region.Split('\n');
            int totalRemoved = 0;
            int firstLineRemoved = 0;
            var sb = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int removed = 0;
                while (removed < 4 && removed < line.Length && line[removed] == ' ')
                    removed++;
                if (removed == 0 && line.Length > 0 && line[0] == '\t')
                    removed = 1;
                if (i > 0) sb.Append('\n');
                sb.Append(line.Substring(removed));
                if (i == 0) firstLineRemoved = removed;
                totalRemoved += removed;
            }

            string newText = text.Substring(0, lineStart) + sb.ToString() + text.Substring(lineEnd);
            _editor.value = newText;

            int newStart = Math.Max(lineStart, selStart - firstLineRemoved);
            int newEnd = Math.Max(newStart, selEnd - totalRemoved);
            _editor.SelectRange(newStart, newEnd);
        }

        static int FindLineStart(string text, int position)
        {
            if (position <= 0) return 0;
            int idx = text.LastIndexOf('\n', position - 1);
            return idx < 0 ? 0 : idx + 1;
        }

        static int FindLineEnd(string text, int position)
        {
            if (position >= text.Length) return text.Length;
            int idx = text.IndexOf('\n', position);
            return idx < 0 ? text.Length : idx;
        }

        static string[] IndentLines(string[] lines, string indent)
        {
            var result = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                result[i] = indent + lines[i];
            return result;
        }

        // ---- Undo / Redo ----

        void ScheduleDebouncedRecord(string value)
        {
            _pendingSnapshot = value;
            _debouncedRecord?.Pause();
            _debouncedRecord = _editor.schedule.Execute(() =>
            {
                if (_pendingSnapshot != null)
                {
                    RecordUndoSnapshot(_pendingSnapshot);
                    _pendingSnapshot = null;
                }
            }).StartingIn(DebounceMs);
        }

        void RecordUndoSnapshot(string value)
        {
            if (_undoStack.Count > 0 && _undoStack[_undoStack.Count - 1] == value)
                return;

            _undoStack.Add(value);
            if (_undoStack.Count > MaxUndoEntries)
                _undoStack.RemoveAt(0);

            _redoStack.Clear();
        }

        void Undo()
        {
            if (_pendingSnapshot != null)
            {
                RecordUndoSnapshot(_pendingSnapshot);
                _pendingSnapshot = null;
                _debouncedRecord?.Pause();
            }

            if (_undoStack.Count <= 1) return;

            string current = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(current);

            string prev = _undoStack[_undoStack.Count - 1];

            _isUndoRedoAction = true;
            _editor.value = prev;
            _isUndoRedoAction = false;
            RefreshHighlight();
        }

        void Redo()
        {
            if (_redoStack.Count == 0) return;

            string next = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(next);

            _isUndoRedoAction = true;
            _editor.value = next;
            _isUndoRedoAction = false;
            RefreshHighlight();
        }

        // ---- Insert text at cursor ----

        public void InsertAtCursor(string text)
        {
            string code = _editor.value;
            int cursor = _editor.cursorIndex;
            if (cursor < 0 || cursor > code.Length)
                cursor = code.Length;

            string newCode = code.Insert(cursor, text);
            _editor.value = newCode;
            _editor.SelectRange(cursor + text.Length, cursor + text.Length);
        }

        // ---- Replace selection ----

        public void ReplaceSelection(string replacement)
        {
            string code = _editor.value;
            int start = SelectionStart;
            int end = SelectionEnd;

            string newCode = code.Substring(0, start) + replacement + code.Substring(end);
            _editor.value = newCode;
            _editor.SelectRange(start + replacement.Length, start + replacement.Length);
        }

        // ---- Get current word at cursor (for autocomplete) ----

        public string GetCurrentToken()
        {
            string code = _editor.value;
            int cursor = _editor.cursorIndex;
            if (string.IsNullOrEmpty(code) || cursor <= 0) return "";

            int start = cursor - 1;
            while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] == '_' || code[start] == '.'))
                start--;
            start++;

            return code.Substring(start, cursor - start);
        }

        public int GetCurrentTokenStart()
        {
            string code = _editor.value;
            int cursor = _editor.cursorIndex;
            if (string.IsNullOrEmpty(code) || cursor <= 0) return cursor;

            int start = cursor - 1;
            while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] == '_' || code[start] == '.'))
                start--;
            return start + 1;
        }

        // ---- Syntax highlighting ----

        void RefreshHighlight()
        {
            string code = _editor?.value ?? "";
            _lineNumbers.text = GenerateLineNumbers(code);
            _highlight.text = Highlight(code);
        }

        /// <summary>
        /// Produce rich-text HLSL with VS Code Dark+ coloring.
        /// Angle brackets in source code are replaced with Unicode look-alikes
        /// (‹ ›) so they cannot break Unity's rich-text tag parser.
        /// </summary>
        public static string Highlight(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;

            var spans = new List<(int start, int end, string color)>();

            void CollectMatches(Regex rx, string color)
            {
                foreach (Match m in rx.Matches(code))
                    spans.Add((m.Index, m.Index + m.Length, color));
            }

            CollectMatches(ReNumbers, ColNumber);
            CollectMatches(ReKeywords, ColKeyword);
            CollectMatches(ReTypes, ColType);
            CollectMatches(ReFunctions, ColFunction);
            CollectMatches(RePreprocessor, ColPreproc);
            CollectMatches(ReStrings, ColString);
            CollectMatches(ReAILabTags, ColAILabTag);
            CollectMatches(ReComments, ColComment);

            spans.Sort((a, b) => a.start != b.start ? a.start.CompareTo(b.start) : a.end.CompareTo(b.end));

            var colors = new string[code.Length];
            foreach (var (s, e, c) in spans)
            {
                for (int i = s; i < e && i < colors.Length; i++)
                    colors[i] = c;
            }

            var sb = new StringBuilder(code.Length * 2);
            string currentColor = null;

            for (int i = 0; i < code.Length; i++)
            {
                string col = colors[i];
                if (col != currentColor)
                {
                    if (currentColor != null)
                        sb.Append("</color>");
                    if (col != null)
                        sb.Append("<color=").Append(col).Append('>');
                    currentColor = col;
                }

                char ch = code[i];
                switch (ch)
                {
                    // Replace angle brackets with Unicode look-alikes to avoid
                    // breaking the rich-text parser.  ‹ and › are visually close
                    // to < and > at small font sizes.
                    case '<': sb.Append(AngleBracketLeft);  break;
                    case '>': sb.Append(AngleBracketRight); break;
                    default:  sb.Append(ch); break;
                }
            }

            if (currentColor != null)
                sb.Append("</color>");

            return sb.ToString();
        }

        public static string GenerateLineNumbers(string code)
        {
            if (string.IsNullOrEmpty(code)) return "  1";

            int lineCount = 1;
            for (int i = 0; i < code.Length; i++)
                if (code[i] == '\n') lineCount++;

            int padWidth = Math.Max(3, lineCount.ToString().Length + 1);
            var sb = new StringBuilder(lineCount * (padWidth + 1));
            for (int i = 1; i <= lineCount; i++)
            {
                if (i > 1) sb.Append('\n');
                sb.Append(i.ToString().PadLeft(padWidth));
            }
            return sb.ToString();
        }
    }
}
