using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    public class ShaderOptionsNodeView : Node
    {
        static readonly string[] CullOptions   = { "", "Back", "Front", "Off" };
        static readonly string[] ZWriteOptions = { "", "On", "Off" };
        static readonly string[] ZTestOptions  = { "", "LEqual", "Less", "Equal", "GEqual", "Greater", "NotEqual", "Always" };
        static readonly string[] BlendPresets  = { "", "Off", "SrcAlpha OneMinusSrcAlpha", "One One", "One OneMinusSrcAlpha", "DstColor Zero", "OneMinusDstColor One" };
        static readonly string[] ColorMasks    = { "", "RGBA", "RGB", "R", "G", "B", "A", "0" };
        static readonly string[] RenderQueues  = { "Background", "Geometry", "AlphaTest", "Transparent", "Overlay" };
        static readonly string[] RenderTypes   = { "Opaque", "Transparent", "TransparentCutout" };
        static readonly string[] StencilComps  = { "Always", "Less", "LEqual", "Equal", "GEqual", "Greater", "NotEqual", "Never" };
        static readonly string[] StencilOps    = { "Keep", "Zero", "Replace", "IncrSat", "DecrSat", "Invert", "IncrWrap", "DecrWrap" };

        readonly VisualElement _content;
        PassRenderState _state;
        ShaderGlobalSettings _globalSettings;

        public event Action OnOptionsChanged;

        public ShaderOptionsNodeView()
        {
            title = "Shader Options";

            var titleLabel = titleContainer.Q<Label>();
            if (titleLabel != null)
            {
                titleLabel.style.fontSize = 13;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            titleContainer.style.backgroundColor = new Color(0.4f, 0.2f, 0.35f, 1f);

            _content = new VisualElement();
            _content.style.paddingTop = 6;
            _content.style.paddingBottom = 6;
            _content.style.paddingLeft = 8;
            _content.style.paddingRight = 8;
            _content.style.minWidth = 240;
            extensionContainer.Add(_content);
            RefreshExpandedState();

            capabilities &= ~Capabilities.Deletable;
            style.minWidth = 260;
        }

        public void Rebuild(PassRenderState state, ShaderGlobalSettings globalSettings = null)
        {
            _state = state ?? new PassRenderState();
            _globalSettings = globalSettings;
            _content.Clear();

            if (_globalSettings != null)
            {
                AddDropdown("Queue", RenderQueues, _globalSettings.RenderQueue,
                    "SubShader render queue (Background=1000, Geometry=2000, AlphaTest=2450, Transparent=3000, Overlay=4000)",
                    v => { _globalSettings.RenderQueue = v; OnOptionsChanged?.Invoke(); });

                AddDropdown("RenderType", RenderTypes, _globalSettings.RenderType,
                    "SubShader RenderType tag for replacement shaders",
                    v => { _globalSettings.RenderType = v; OnOptionsChanged?.Invoke(); });

                AddSeparator();
            }

            AddDropdown("Cull", CullOptions, _state.CullMode, "Face culling mode",
                v => { _state.CullMode = v; OnOptionsChanged?.Invoke(); });

            AddDropdown("ZWrite", ZWriteOptions, _state.ZWriteMode, "Depth buffer write",
                v => { _state.ZWriteMode = v; OnOptionsChanged?.Invoke(); });

            AddDropdown("ZTest", ZTestOptions, _state.ZTestMode, "Depth comparison function",
                v => { _state.ZTestMode = v; OnOptionsChanged?.Invoke(); });

            AddDropdown("Blend", BlendPresets, _state.BlendMode, "Source Dest blend mode",
                v => { _state.BlendMode = v; OnOptionsChanged?.Invoke(); });

            AddDropdown("ColorMask", ColorMasks, _state.ColorMask, "Color channel write mask",
                v => { _state.ColorMask = v; OnOptionsChanged?.Invoke(); });

            AddSeparator();
            AddStencilSection();
        }

        public PassRenderState GetState() => _state;

        void AddDropdown(string label, string[] options, string current, string tooltipText, Action<string> onChanged)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.tooltip = tooltipText;

            var lbl = new Label(label);
            lbl.style.fontSize = 11;
            lbl.style.color = new Color(0.85f, 0.85f, 0.85f);
            lbl.style.width = 72;
            lbl.style.minWidth = 72;
            row.Add(lbl);

            var choices = new List<string>(options);
            int idx = string.IsNullOrEmpty(current) ? 0 : choices.IndexOf(current);
            if (idx < 0) { choices.Add(current); idx = choices.Count - 1; }

            var dropdown = new PopupField<string>(choices, idx);
            dropdown.style.flexGrow = 1;
            dropdown.style.height = 20;
            dropdown.style.fontSize = 11;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                string val = evt.newValue;
                if (val == choices[0] && string.IsNullOrEmpty(choices[0]))
                    val = null;
                onChanged?.Invoke(val);
            });
            row.Add(dropdown);

            _content.Add(row);
        }

        void AddSeparator()
        {
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            sep.style.marginTop = 4;
            sep.style.marginBottom = 4;
            _content.Add(sep);
        }

        void AddStencilSection()
        {
            var stencil = _state.Stencil ?? new StencilState();

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4;

            var stencilLabel = new Label("Stencil");
            stencilLabel.style.fontSize = 12;
            stencilLabel.style.color = new Color(0.9f, 0.75f, 0.5f);
            stencilLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            stencilLabel.style.flexGrow = 1;
            headerRow.Add(stencilLabel);

            bool hasStencil = _state.Stencil != null && _state.Stencil.HasOverrides;
            var enableToggle = new Toggle { value = hasStencil };
            enableToggle.style.width = 16;
            enableToggle.tooltip = "Enable/disable stencil test";
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    if (_state.Stencil == null) _state.Stencil = new StencilState();
                    _state.Stencil.Comp = "Always";
                    _state.Stencil.Pass = "Replace";
                    _state.Stencil.Ref = 1;
                }
                else
                {
                    _state.Stencil = null;
                }
                Rebuild(_state);
                OnOptionsChanged?.Invoke();
            });
            headerRow.Add(enableToggle);

            _content.Add(headerRow);

            if (_state.Stencil == null) return;

            AddIntField("Ref", stencil.Ref, 0, 255, "Stencil reference value",
                v => { stencil.Ref = v; _state.Stencil = stencil; OnOptionsChanged?.Invoke(); });

            AddDropdown("Comp", StencilComps, stencil.Comp, "Stencil comparison function",
                v => { stencil.Comp = v; _state.Stencil = stencil; OnOptionsChanged?.Invoke(); });

            AddDropdown("Pass", StencilOps, stencil.Pass, "Operation when stencil+depth pass",
                v => { stencil.Pass = v; _state.Stencil = stencil; OnOptionsChanged?.Invoke(); });

            AddDropdown("Fail", StencilOps, stencil.Fail, "Operation when stencil fails",
                v => { stencil.Fail = v; _state.Stencil = stencil; OnOptionsChanged?.Invoke(); });

            AddDropdown("ZFail", StencilOps, stencil.ZFail, "Operation when stencil passes but depth fails",
                v => { stencil.ZFail = v; _state.Stencil = stencil; OnOptionsChanged?.Invoke(); });

            AddIntField("ReadMask", stencil.ReadMask, 0, 255, "Stencil read bitmask",
                v => { stencil.ReadMask = v; _state.Stencil = stencil; OnOptionsChanged?.Invoke(); });

            AddIntField("WriteMask", stencil.WriteMask, 0, 255, "Stencil write bitmask",
                v => { stencil.WriteMask = v; _state.Stencil = stencil; OnOptionsChanged?.Invoke(); });
        }

        void AddIntField(string label, int current, int min, int max, string tooltipText, Action<int> onChanged)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.tooltip = tooltipText;

            var lbl = new Label(label);
            lbl.style.fontSize = 11;
            lbl.style.color = new Color(0.85f, 0.85f, 0.85f);
            lbl.style.width = 72;
            lbl.style.minWidth = 72;
            row.Add(lbl);

            var slider = new SliderInt(min, max) { value = current };
            slider.style.flexGrow = 1;
            slider.style.height = 20;
            slider.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            row.Add(slider);

            var valLabel = new Label(current.ToString());
            valLabel.style.fontSize = 10;
            valLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            valLabel.style.width = 28;
            valLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            slider.RegisterValueChangedCallback(evt => valLabel.text = evt.newValue.ToString());
            row.Add(valLabel);

            _content.Add(row);
        }
    }
}
