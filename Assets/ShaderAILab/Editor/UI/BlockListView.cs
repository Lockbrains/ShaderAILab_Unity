using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Manages the left-panel list of natural language blocks with
    /// inline expand/collapse to show code previews and section filtering.
    /// </summary>
    public class BlockListView
    {
        readonly VisualElement _container;
        readonly Dictionary<string, bool> _expandedState = new Dictionary<string, bool>();
        string _selectedId;

        VisualElement _filterBar;
        VisualElement _listArea;
        ShaderSectionType? _activeFilter;
        ShaderPass _currentPass;
        readonly Dictionary<ShaderSectionType, Button> _filterButtons = new Dictionary<ShaderSectionType, Button>();
        Button _allButton;

        public event Action<string> OnBlockSelected;
        public event Action<string> OnBlockDeleteRequested;
        public event Action<string, bool> OnBlockToggleEnabled;

        static readonly Color ActiveBg = new Color(0.055f, 0.39f, 0.61f);
        static readonly Color InactiveBg = new Color(0.235f, 0.235f, 0.235f);

        public BlockListView(VisualElement container)
        {
            _container = container;
            BuildFilterBar();
        }

        void BuildFilterBar()
        {
            _filterBar = new VisualElement();
            _filterBar.AddToClassList("block-filter-bar");

            _allButton = MakeFilterButton("All", null);
            _allButton.AddToClassList("block-filter-btn--active");
            _filterBar.Add(_allButton);

            var fragBtn = MakeFilterButton("Frag", ShaderSectionType.Fragment);
            _filterButtons[ShaderSectionType.Fragment] = fragBtn;
            _filterBar.Add(fragBtn);

            var vertBtn = MakeFilterButton("Vert", ShaderSectionType.Vertex);
            _filterButtons[ShaderSectionType.Vertex] = vertBtn;
            _filterBar.Add(vertBtn);

            var helpBtn = MakeFilterButton("Helper", ShaderSectionType.Helper);
            _filterButtons[ShaderSectionType.Helper] = helpBtn;
            _filterBar.Add(helpBtn);

            _container.Add(_filterBar);

            _listArea = new VisualElement();
            _listArea.style.flexGrow = 1;
            _container.Add(_listArea);
        }

        Button MakeFilterButton(string label, ShaderSectionType? section)
        {
            var btn = new Button(() => SetFilter(section)) { text = label };
            btn.AddToClassList("block-filter-btn");
            return btn;
        }

        void SetFilter(ShaderSectionType? section)
        {
            _activeFilter = section;

            _allButton.EnableInClassList("block-filter-btn--active", section == null);
            foreach (var kv in _filterButtons)
                kv.Value.EnableInClassList("block-filter-btn--active", section == kv.Key);

            RebuildList();
        }

        public void Rebuild(ShaderDocument doc)
        {
            _currentPass = doc?.ActivePass;
            RebuildList();
        }

        public void Rebuild(ShaderPass pass)
        {
            _currentPass = pass;
            RebuildList();
        }

        void RebuildList()
        {
            _listArea.Clear();
            if (_currentPass == null) return;

            foreach (var block in _currentPass.Blocks)
            {
                if (_activeFilter.HasValue && block.Section != _activeFilter.Value)
                    continue;
                _listArea.Add(CreateBlockItem(block));
            }
        }

        public void SetSelected(string blockId)
        {
            _selectedId = blockId;
            foreach (var child in _listArea.Children())
            {
                bool isSelected = child.userData as string == blockId;
                child.EnableInClassList("block-item--selected", isSelected);
            }
        }

        VisualElement CreateBlockItem(ShaderBlock block)
        {
            var item = new VisualElement();
            item.AddToClassList("block-item");
            item.userData = block.Id;

            // Header row: expand arrow + title
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            bool isExpanded = _expandedState.ContainsKey(block.Id) && _expandedState[block.Id];

            var expandArrow = new Label(isExpanded ? "\u25BC" : "\u25B6");
            expandArrow.style.width = 16;
            expandArrow.style.fontSize = 10;
            expandArrow.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
            expandArrow.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;

            var title = new Label(block.Title);
            title.AddToClassList("block-item__title");
            title.style.flexGrow = 1;

            headerRow.Add(expandArrow);
            headerRow.Add(title);
            item.Add(headerRow);

            if (!string.IsNullOrEmpty(block.Intent))
            {
                var intent = new Label(block.Intent);
                intent.AddToClassList("block-item__intent");
                item.Add(intent);
            }

            var badge = new Label(block.Section.ToString());
            badge.AddToClassList("block-item__section-badge");
            if (block.Section == ShaderSectionType.Helper)
                badge.AddToClassList("block-item__section-badge--helper");
            item.Add(badge);

            // Collapsible code preview
            var codePreview = new Label();
            codePreview.AddToClassList("block-item__code-preview");
            codePreview.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            codePreview.style.fontSize = 10;
            codePreview.style.color = new UnityEngine.Color(0.65f, 0.65f, 0.65f);
            codePreview.style.backgroundColor = new UnityEngine.Color(0.12f, 0.12f, 0.12f);
            codePreview.style.paddingTop = 4;
            codePreview.style.paddingBottom = 4;
            codePreview.style.paddingLeft = 8;
            codePreview.style.paddingRight = 8;
            codePreview.style.marginTop = 4;
            codePreview.style.borderTopLeftRadius = 3;
            codePreview.style.borderTopRightRadius = 3;
            codePreview.style.borderBottomLeftRadius = 3;
            codePreview.style.borderBottomRightRadius = 3;
            codePreview.style.whiteSpace = WhiteSpace.Normal;
            codePreview.text = TruncateCode(block.Code, 8);
            item.Add(codePreview);

            // Toggle expand/collapse on header click
            headerRow.RegisterCallback<ClickEvent>(evt =>
            {
                bool newState = !_expandedState.ContainsKey(block.Id) || !_expandedState[block.Id];
                _expandedState[block.Id] = newState;
                expandArrow.text = newState ? "\u25BC" : "\u25B6";
                codePreview.style.display = newState ? DisplayStyle.Flex : DisplayStyle.None;
                evt.StopPropagation();
            });

            // Action buttons
            var actions = new VisualElement();
            actions.AddToClassList("block-item__actions");

            var editBtn = new Button(() => OnBlockSelected?.Invoke(block.Id)) { text = "Edit" };
            editBtn.AddToClassList("block-item__action-btn");
            actions.Add(editBtn);

            var toggleBtn = new Button() { text = block.IsEnabled ? "Disable" : "Enable" };
            toggleBtn.AddToClassList("block-item__action-btn");
            if (!block.IsEnabled) toggleBtn.AddToClassList("block-item__action-btn--disabled");
            toggleBtn.clicked += () =>
            {
                block.IsEnabled = !block.IsEnabled;
                toggleBtn.text = block.IsEnabled ? "Disable" : "Enable";
                toggleBtn.EnableInClassList("block-item__action-btn--disabled", !block.IsEnabled);
                item.EnableInClassList("block-item--disabled", !block.IsEnabled);
                OnBlockToggleEnabled?.Invoke(block.Id, block.IsEnabled);
            };
            actions.Add(toggleBtn);

            var deleteBtn = new Button(() => OnBlockDeleteRequested?.Invoke(block.Id)) { text = "Delete" };
            deleteBtn.AddToClassList("block-item__action-btn");
            actions.Add(deleteBtn);

            item.Add(actions);

            // Apply initial disabled style
            if (!block.IsEnabled)
                item.AddToClassList("block-item--disabled");

            item.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == deleteBtn || evt.target == expandArrow) return;
                OnBlockSelected?.Invoke(block.Id);
            });

            return item;
        }

        static string TruncateCode(string code, int maxLines)
        {
            if (string.IsNullOrEmpty(code)) return "(empty)";
            var lines = code.Split('\n');
            if (lines.Length <= maxLines)
                return code;
            return string.Join("\n", lines, 0, maxLines) + "\n  ...";
        }
    }
}
