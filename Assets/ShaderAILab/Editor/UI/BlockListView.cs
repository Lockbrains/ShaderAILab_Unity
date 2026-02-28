using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Manages the left-panel list of natural language blocks with
    /// inline expand/collapse to show code previews.
    /// </summary>
    public class BlockListView
    {
        readonly VisualElement _container;
        readonly Dictionary<string, bool> _expandedState = new Dictionary<string, bool>();
        string _selectedId;

        public event Action<string> OnBlockSelected;
        public event Action<string> OnBlockDeleteRequested;
        public event Action<string, bool> OnBlockToggleEnabled;

        public BlockListView(VisualElement container)
        {
            _container = container;
        }

        public void Rebuild(ShaderDocument doc)
        {
            _container.Clear();
            if (doc == null) return;

            foreach (var block in doc.Blocks)
                _container.Add(CreateBlockItem(block));
        }

        public void SetSelected(string blockId)
        {
            _selectedId = blockId;
            foreach (var child in _container.Children())
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
