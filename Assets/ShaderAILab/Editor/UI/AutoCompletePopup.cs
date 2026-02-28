using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Floating autocomplete popup showing variables, properties, and HLSL built-ins.
    /// Supports keyboard navigation (Up/Down/Enter/Escape) and fuzzy matching.
    /// </summary>
    public class AutoCompletePopup : VisualElement
    {
        readonly ScrollView _list;
        readonly List<CompletionItem> _allItems = new List<CompletionItem>();
        readonly List<CompletionItem> _filteredItems = new List<CompletionItem>();
        int _selectedIndex = -1;

        public event Action<string> OnItemSelected;

        public struct CompletionItem
        {
            public string Name;
            public string Type;
            public string Description;
            public string Category; // "property", "attribute", "varying", "global", "function"
        }

        public AutoCompletePopup()
        {
            AddToClassList("autocomplete-popup");
            style.display = DisplayStyle.None;

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;
            Add(_list);
        }

        public void SetCompletionSource(ShaderDocument doc)
        {
            _allItems.Clear();

            if (doc != null)
            {
                foreach (var p in doc.Properties)
                {
                    _allItems.Add(new CompletionItem
                    {
                        Name = p.Name,
                        Type = p.PropertyType.ToString(),
                        Description = p.DisplayName,
                        Category = "property"
                    });
                }
            }

            foreach (var f in DataFlowRegistry.AllAttributes)
            {
                _allItems.Add(new CompletionItem
                {
                    Name = "input." + f.Name,
                    Type = f.HLSLType,
                    Description = f.DisplayName,
                    Category = "attribute"
                });
            }

            foreach (var f in DataFlowRegistry.AllVaryings)
            {
                _allItems.Add(new CompletionItem
                {
                    Name = "input." + f.Name,
                    Type = f.HLSLType,
                    Description = f.DisplayName + " (fragment)",
                    Category = "varying"
                });
            }

            foreach (var g in DataFlowRegistry.AllGlobals)
            {
                _allItems.Add(new CompletionItem
                {
                    Name = g.Name,
                    Type = g.HLSLType,
                    Description = g.DisplayName,
                    Category = "global"
                });
            }

            AddHLSLBuiltins();
        }

        void AddHLSLBuiltins()
        {
            string[] funcs =
            {
                "saturate", "normalize", "dot", "cross", "lerp", "clamp",
                "sin", "cos", "tan", "atan2", "abs", "pow", "sqrt", "rsqrt",
                "mul", "min", "max", "step", "smoothstep", "frac", "floor", "ceil",
                "round", "sign", "length", "distance", "reflect", "refract",
                "ddx", "ddy", "fwidth", "clip",
                "SAMPLE_TEXTURE2D", "TRANSFORM_TEX"
            };

            foreach (string fn in funcs)
            {
                _allItems.Add(new CompletionItem
                {
                    Name = fn,
                    Type = "function",
                    Description = "HLSL built-in",
                    Category = "function"
                });
            }
        }

        public void Show(string filter, float x, float y)
        {
            UpdateFilter(filter);
            if (_filteredItems.Count == 0)
            {
                Hide();
                return;
            }

            style.display = DisplayStyle.Flex;
            style.left = x;
            style.top = y;
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            _selectedIndex = -1;
        }

        public bool IsVisible => resolvedStyle.display == DisplayStyle.Flex;

        public void UpdateFilter(string filter)
        {
            _filteredItems.Clear();
            _list.Clear();

            if (string.IsNullOrEmpty(filter))
            {
                Hide();
                return;
            }

            string lowerFilter = filter.ToLowerInvariant();

            foreach (var item in _allItems)
            {
                if (FuzzyMatch(item.Name.ToLowerInvariant(), lowerFilter) ||
                    FuzzyMatch(item.Description.ToLowerInvariant(), lowerFilter))
                {
                    _filteredItems.Add(item);
                }
            }

            if (_filteredItems.Count == 0) return;

            // Sort: exact prefix first, then contains
            _filteredItems.Sort((a, b) =>
            {
                bool aPrefix = a.Name.ToLowerInvariant().StartsWith(lowerFilter);
                bool bPrefix = b.Name.ToLowerInvariant().StartsWith(lowerFilter);
                if (aPrefix != bPrefix) return aPrefix ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            int maxShow = Mathf.Min(_filteredItems.Count, 12);
            for (int i = 0; i < maxShow; i++)
            {
                var item = _filteredItems[i];
                int idx = i;

                var row = new VisualElement();
                row.AddToClassList("autocomplete-item");

                var icon = new Label(GetCategoryIcon(item.Category));
                icon.AddToClassList("autocomplete-item__icon");
                row.Add(icon);

                var nameLabel = new Label(item.Name);
                nameLabel.AddToClassList("autocomplete-item__name");
                row.Add(nameLabel);

                var typeLabel = new Label(item.Type);
                typeLabel.AddToClassList("autocomplete-item__type");
                row.Add(typeLabel);

                row.tooltip = item.Description;
                row.RegisterCallback<ClickEvent>(_ => OnItemSelected?.Invoke(item.Name));

                _list.Add(row);
            }

            _selectedIndex = 0;
            UpdateSelection();
        }

        public bool HandleKey(KeyCode key)
        {
            if (!IsVisible || _filteredItems.Count == 0) return false;

            if (key == KeyCode.DownArrow)
            {
                _selectedIndex = Mathf.Min(_selectedIndex + 1, Mathf.Min(_filteredItems.Count, 12) - 1);
                UpdateSelection();
                return true;
            }
            if (key == KeyCode.UpArrow)
            {
                _selectedIndex = Mathf.Max(_selectedIndex - 1, 0);
                UpdateSelection();
                return true;
            }
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _filteredItems.Count)
                    OnItemSelected?.Invoke(_filteredItems[_selectedIndex].Name);
                Hide();
                return true;
            }
            if (key == KeyCode.Escape)
            {
                Hide();
                return true;
            }

            return false;
        }

        void UpdateSelection()
        {
            int i = 0;
            foreach (var child in _list.contentContainer.Children())
            {
                child.EnableInClassList("autocomplete-item--selected", i == _selectedIndex);
                i++;
            }
        }

        static string GetCategoryIcon(string category)
        {
            switch (category)
            {
                case "property":  return "P";
                case "attribute": return "A";
                case "varying":   return "V";
                case "global":    return "G";
                case "function":  return "f";
                default:          return "?";
            }
        }

        static bool FuzzyMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(text)) return false;

            // Simple subsequence match
            int pi = 0;
            for (int ti = 0; ti < text.Length && pi < pattern.Length; ti++)
            {
                if (text[ti] == pattern[pi])
                    pi++;
            }
            return pi == pattern.Length;
        }
    }
}
