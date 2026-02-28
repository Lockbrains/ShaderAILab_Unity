using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Floating popup triggered by Ctrl+Shift+Space for inline AI code generation.
    /// Features a gradient border for visual AI branding.
    /// Enter to submit, Escape to close.
    /// </summary>
    public class InlineLLMPopup : VisualElement
    {
        readonly TextField _input;
        readonly Label _status;
        readonly VisualElement _gradientBorder;

        public event Action<string> OnSubmit;
        public event Action OnClose;

        // Gradient colors (purple → blue → cyan)
        static readonly Color ColLeft   = new Color(0.58f, 0.33f, 0.87f);  // #9454DE
        static readonly Color ColCenter = new Color(0.25f, 0.47f, 0.87f);  // #4078DE
        static readonly Color ColRight  = new Color(0.20f, 0.74f, 0.85f);  // #33BDD9

        public InlineLLMPopup()
        {
            // Outer gradient border container
            _gradientBorder = new VisualElement();
            _gradientBorder.AddToClassList("inline-llm-gradient");
            Add(_gradientBorder);

            // Inner content (sits inside the gradient border with a small gap)
            var inner = new VisualElement();
            inner.AddToClassList("inline-llm-inner");
            _gradientBorder.Add(inner);

            // Input row
            var row = new VisualElement();
            row.AddToClassList("inline-llm-popup__row");

            _input = new TextField();
            _input.AddToClassList("inline-llm-popup__input");
            _input.multiline = false;

            // Placeholder via the text element
            var textInput = _input.Q(className: "unity-text-field__input");
            if (textInput != null)
                textInput.style.unityTextAlign = TextAnchor.MiddleLeft;

            row.Add(_input);

            var sendBtn = new Button(Submit) { text = "\u2728" };
            sendBtn.AddToClassList("inline-llm-popup__btn");
            sendBtn.tooltip = "Generate (Enter)";
            row.Add(sendBtn);

            inner.Add(row);

            _status = new Label();
            _status.AddToClassList("inline-llm-popup__status");
            inner.Add(_status);

            // Placeholder behavior
            _input.value = "";
            bool showingPlaceholder = true;
            string placeholderText = "Describe what code to insert here...";

            _input.schedule.Execute(() =>
            {
                if (string.IsNullOrEmpty(_input.value) && showingPlaceholder)
                    SetPlaceholderStyle(true, placeholderText);
            }).StartingIn(30);

            _input.RegisterCallback<FocusInEvent>(_ =>
            {
                if (showingPlaceholder)
                {
                    _input.SetValueWithoutNotify("");
                    SetPlaceholderStyle(false, null);
                    showingPlaceholder = false;
                }
            });

            _input.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (string.IsNullOrEmpty(_input.value))
                {
                    showingPlaceholder = true;
                    SetPlaceholderStyle(true, placeholderText);
                }
            });

            _input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();
                    evt.PreventDefault();
                    Submit();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    evt.PreventDefault();
                    OnClose?.Invoke();
                }
            }, TrickleDown.TrickleDown);

            // Animate the gradient border color
            float hueOffset = 0f;
            schedule.Execute(() =>
            {
                hueOffset += 0.003f;
                if (hueOffset > 1f) hueOffset -= 1f;
                UpdateGradientBorder(hueOffset);
            }).Every(50);
        }

        void SetPlaceholderStyle(bool isPlaceholder, string text)
        {
            if (isPlaceholder && text != null)
            {
                _input.SetValueWithoutNotify(text);
                var te = _input.Q<TextElement>();
                if (te != null) te.style.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            }
            else
            {
                var te = _input.Q<TextElement>();
                if (te != null) te.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            }
        }

        void UpdateGradientBorder(float offset)
        {
            Color c = Color.Lerp(
                Color.Lerp(ColLeft, ColCenter, Mathf.PingPong(offset * 2f, 1f)),
                ColRight,
                Mathf.PingPong(offset * 3f + 0.3f, 1f)
            );
            _gradientBorder.style.borderTopColor = c;
            _gradientBorder.style.borderBottomColor = Color.Lerp(c, ColRight, 0.5f);
            _gradientBorder.style.borderLeftColor = Color.Lerp(ColLeft, c, 0.5f);
            _gradientBorder.style.borderRightColor = Color.Lerp(c, ColCenter, 0.5f);
        }

        void Submit()
        {
            string text = _input.value?.Trim();
            if (string.IsNullOrEmpty(text) || text == "Describe what code to insert here...") return;
            _input.SetEnabled(false);
            OnSubmit?.Invoke(text);
        }

        public void SetStatus(string text)
        {
            _status.text = text;
        }

        public void FocusInput()
        {
            schedule.Execute(() =>
            {
                _input.SetValueWithoutNotify("");
                SetPlaceholderStyle(false, null);
                _input.Focus();
            }).StartingIn(50);
        }
    }
}
