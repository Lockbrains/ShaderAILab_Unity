using System;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// A floating panel inside the DataFlow GraphView that shows all available
    /// fields grouped by stage, letting the user quickly activate fields.
    /// </summary>
    public class DataFlowFieldListPanel : VisualElement
    {
        public event Action<string, DataFlowStage> OnFieldActivateRequested;

        readonly VisualElement _attrList;
        readonly VisualElement _varyList;
        readonly VisualElement _globalList;
        readonly ScrollView _scrollView;

        public DataFlowFieldListPanel()
        {
            style.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 0.92f);
            style.borderTopLeftRadius = 6;
            style.borderTopRightRadius = 6;
            style.borderBottomLeftRadius = 6;
            style.borderBottomRightRadius = 6;
            style.borderTopWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;
            style.borderRightWidth = 1;
            style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
            style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);
            style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);
            style.paddingTop = 6;
            style.paddingBottom = 6;
            style.paddingLeft = 8;
            style.paddingRight = 8;
            style.maxHeight = 500;

            var header = new Label("Available Fields");
            header.style.fontSize = 12;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new Color(0.85f, 0.85f, 0.85f);
            header.style.marginBottom = 6;
            Add(header);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            Add(_scrollView);

            var attrHeader = CreateSectionHeader("Attributes (a2v)", new Color(0.5f, 0.8f, 0.5f));
            _scrollView.Add(attrHeader);
            _attrList = new VisualElement();
            _scrollView.Add(_attrList);

            var varyHeader = CreateSectionHeader("Varyings (v2f)", new Color(0.5f, 0.6f, 0.9f));
            _scrollView.Add(varyHeader);
            _varyList = new VisualElement();
            _scrollView.Add(_varyList);

            var globalHeader = CreateSectionHeader("Global Uniforms (always available)", new Color(0.85f, 0.65f, 0.3f));
            _scrollView.Add(globalHeader);
            _globalList = new VisualElement();
            _scrollView.Add(_globalList);
        }

        static Label CreateSectionHeader(string text, Color color)
        {
            var lbl = new Label(text);
            lbl.style.fontSize = 11;
            lbl.style.color = color;
            lbl.style.marginBottom = 2;
            lbl.style.marginTop = 6;
            return lbl;
        }

        public void Rebuild(DataFlowGraph graph)
        {
            _attrList.Clear();
            _varyList.Clear();
            _globalList.Clear();

            foreach (var f in graph.AttributeFields)
                _attrList.Add(CreateFieldRow(f, DataFlowStage.Attributes));

            foreach (var f in graph.VaryingFields)
                _varyList.Add(CreateFieldRow(f, DataFlowStage.Varyings));

            foreach (var g in DataFlowRegistry.AllGlobals)
                _globalList.Add(CreateGlobalRow(g));
        }

        VisualElement CreateFieldRow(DataFlowField field, DataFlowStage stage)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 20;
            row.style.marginBottom = 1;
            row.tooltip = $"{field.Name}  ({field.HLSLType})";

            var dot = CreateDot(field.IsActive
                ? new Color(0.3f, 0.8f, 0.3f)
                : new Color(0.35f, 0.35f, 0.35f));
            row.Add(dot);

            var label = new Label(field.DisplayName);
            label.style.fontSize = 10;
            label.style.flexGrow = 1;
            label.style.color = field.IsActive
                ? new Color(0.75f, 0.75f, 0.75f)
                : new Color(0.5f, 0.5f, 0.5f);
            row.Add(label);

            if (!field.IsActive && !field.IsRequired)
            {
                var addBtn = new Button(() => OnFieldActivateRequested?.Invoke(field.Name, stage));
                addBtn.text = "+";
                addBtn.style.width = 20;
                addBtn.style.height = 18;
                addBtn.style.fontSize = 12;
                addBtn.style.borderTopWidth = 0;
                addBtn.style.borderBottomWidth = 0;
                addBtn.style.borderLeftWidth = 0;
                addBtn.style.borderRightWidth = 0;
                addBtn.style.backgroundColor = new Color(0.2f, 0.4f, 0.2f);
                addBtn.style.color = new Color(0.7f, 0.9f, 0.7f);
                addBtn.style.borderTopLeftRadius = 3;
                addBtn.style.borderTopRightRadius = 3;
                addBtn.style.borderBottomLeftRadius = 3;
                addBtn.style.borderBottomRightRadius = 3;
                row.Add(addBtn);
            }

            if (field.IsRequired)
            {
                var lockLabel = new Label("req");
                lockLabel.style.fontSize = 9;
                lockLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                lockLabel.style.marginLeft = 4;
                row.Add(lockLabel);
            }

            return row;
        }

        VisualElement CreateGlobalRow(DataFlowField field)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 20;
            row.style.marginBottom = 1;
            row.tooltip = $"{field.Name}  ({field.HLSLType})\nAvailable everywhere without struct fields.";

            var dot = CreateDot(new Color(0.85f, 0.65f, 0.3f));
            row.Add(dot);

            var label = new Label(field.DisplayName);
            label.style.fontSize = 10;
            label.style.flexGrow = 1;
            label.style.color = new Color(0.65f, 0.6f, 0.5f);
            row.Add(label);

            return row;
        }

        static VisualElement CreateDot(Color color)
        {
            var dot = new VisualElement();
            dot.style.width = 8;
            dot.style.height = 8;
            dot.style.borderTopLeftRadius = 4;
            dot.style.borderTopRightRadius = 4;
            dot.style.borderBottomLeftRadius = 4;
            dot.style.borderBottomRightRadius = 4;
            dot.style.marginRight = 6;
            dot.style.backgroundColor = color;
            return dot;
        }
    }
}
