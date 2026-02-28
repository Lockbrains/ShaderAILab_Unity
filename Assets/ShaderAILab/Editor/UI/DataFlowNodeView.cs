using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// A GraphView Node representing either the Attributes (a2v) or Varyings (v2f) struct.
    /// Each active field becomes a Port; inactive fields are shown as a dimmed toggle row.
    /// Labels show human-readable DisplayName; variable names appear on hover.
    /// </summary>
    public class DataFlowNodeView : Node
    {
        public DataFlowStage Stage { get; private set; }
        public event Action<DataFlowField, bool> OnFieldToggled;

        readonly Dictionary<string, Port> _ports = new Dictionary<string, Port>();
        readonly VisualElement _fieldContainer;

        public DataFlowNodeView(DataFlowStage stage)
        {
            Stage = stage;
            title = stage == DataFlowStage.Attributes
                ? "Attributes  (a2v)"
                : stage == DataFlowStage.Varyings
                    ? "Varyings  (v2f)"
                    : "Globals";

            var titleLabel = titleContainer.Q<Label>();
            if (titleLabel != null)
            {
                titleLabel.style.fontSize = 13;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            if (stage == DataFlowStage.Attributes)
                titleContainer.style.backgroundColor = new Color(0.15f, 0.35f, 0.15f, 1f);
            else if (stage == DataFlowStage.Varyings)
                titleContainer.style.backgroundColor = new Color(0.15f, 0.2f, 0.4f, 1f);
            else
                titleContainer.style.backgroundColor = new Color(0.35f, 0.25f, 0.12f, 1f);

            _fieldContainer = new VisualElement();
            _fieldContainer.style.paddingTop = 4;
            _fieldContainer.style.paddingBottom = 4;
            extensionContainer.Add(_fieldContainer);
            RefreshExpandedState();

            capabilities &= ~Capabilities.Deletable;
            style.minWidth = 260;
        }

        public void Rebuild(List<DataFlowField> fields, List<DataFlowError> errors)
        {
            _fieldContainer.Clear();
            _ports.Clear();
            inputContainer.Clear();
            outputContainer.Clear();

            var errorFields = new HashSet<string>();
            if (errors != null)
                foreach (var e in errors)
                    if (e.Stage == Stage)
                        errorFields.Add(e.FieldName);

            foreach (var field in fields)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.height = 24;

                string tipText;
                if (field.Stage == DataFlowStage.Global
                    && DataFlowRegistry.GlobalTooltips.TryGetValue(field.Name, out string globalTip))
                {
                    tipText = globalTip;
                }
                else
                {
                    tipText = $"{field.Name}  ({field.HLSLType})";
                    if (!string.IsNullOrEmpty(field.Semantic))
                        tipText += $"  : {field.Semantic}";
                }
                if (!string.IsNullOrEmpty(field.Annotation))
                    tipText += $"\n{field.Annotation}";
                row.tooltip = tipText;

                // Toggle (not shown for Global fields)
                if (field.Stage != DataFlowStage.Global)
                {
                    var toggle = new Toggle();
                    toggle.value = field.IsActive;
                    toggle.SetEnabled(!field.IsRequired);
                    toggle.style.marginRight = 4;
                    toggle.style.width = 16;
                    var capturedField = field;
                    toggle.RegisterValueChangedCallback(evt =>
                        OnFieldToggled?.Invoke(capturedField, evt.newValue));
                    row.Add(toggle);
                }

                // Display name only (natural language)
                var label = new Label(field.DisplayName);
                label.style.fontSize = 11;
                label.style.flexGrow = 1;
                label.style.color = field.IsActive
                    ? new Color(0.88f, 0.88f, 0.88f)
                    : new Color(0.5f, 0.5f, 0.5f);
                if (field.IsRequired)
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(label);

                // Type badge (small muted text)
                var typeBadge = new Label(field.HLSLType);
                typeBadge.style.fontSize = 9;
                typeBadge.style.color = GetColorForType(field.HLSLType) * 0.7f;
                typeBadge.style.marginLeft = 4;
                typeBadge.style.unityFontStyleAndWeight = FontStyle.Italic;
                row.Add(typeBadge);

                // Error indicator
                if (errorFields.Contains(field.Name))
                {
                    var errorDot = CreateCircle(10, new Color(0.9f, 0.2f, 0.2f));
                    errorDot.style.marginLeft = 4;
                    errorDot.tooltip = "Missing dependency in Attributes";
                    row.Add(errorDot);
                }

                // LLM annotation badge
                if (!string.IsNullOrEmpty(field.Annotation))
                {
                    var annotBadge = CreateCircle(8, new Color(0.3f, 0.6f, 0.9f));
                    annotBadge.style.marginLeft = 4;
                    annotBadge.tooltip = field.Annotation;
                    row.Add(annotBadge);
                }

                _fieldContainer.Add(row);

                // Ports for active struct fields only (not Globals)
                if (field.IsActive && field.Stage != DataFlowStage.Global)
                {
                    Direction dir = Stage == DataFlowStage.Attributes
                        ? Direction.Output : Direction.Input;
                    Port port = InstantiatePort(Orientation.Horizontal, dir, Port.Capacity.Multi, typeof(float));
                    port.portName = field.DisplayName;
                    port.tooltip = $"{field.Name} ({field.HLSLType})";
                    port.portColor = GetColorForType(field.HLSLType);

                    if (Stage == DataFlowStage.Attributes)
                        outputContainer.Add(port);
                    else
                        inputContainer.Add(port);

                    _ports[field.Name] = port;
                }
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public Port GetPort(string fieldName)
        {
            _ports.TryGetValue(fieldName, out var port);
            return port;
        }

        static VisualElement CreateCircle(int size, Color color)
        {
            var el = new VisualElement();
            el.style.width = size;
            el.style.height = size;
            int r = size / 2;
            el.style.borderTopLeftRadius = r;
            el.style.borderTopRightRadius = r;
            el.style.borderBottomLeftRadius = r;
            el.style.borderBottomRightRadius = r;
            el.style.backgroundColor = color;
            return el;
        }

        static Color GetColorForType(string hlslType)
        {
            if (hlslType.Contains("4x4"))
                return new Color(0.9f, 0.5f, 0.9f);
            if (hlslType.StartsWith("float4") || hlslType == "half4")
                return new Color(0.4f, 0.5f, 0.9f);
            if (hlslType.StartsWith("float3") || hlslType == "half3")
                return new Color(0.4f, 0.8f, 0.4f);
            if (hlslType.StartsWith("float2") || hlslType == "half2")
                return new Color(0.9f, 0.8f, 0.3f);
            return new Color(0.7f, 0.7f, 0.7f);
        }
    }
}
