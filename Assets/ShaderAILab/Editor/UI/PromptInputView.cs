using System;
using UnityEngine.UIElements;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Manages the bottom prompt panel: target selector, text input,
    /// and generation status display with streaming output.
    /// </summary>
    public class PromptInputView
    {
        readonly TextField _input;
        readonly DropdownField _target;
        readonly Label _status;
        readonly Button _sendBtn;
        readonly VisualElement _streamOutputContainer;
        Label _streamOutput;

        bool _isGenerating;

        public event Action<string, string> OnPromptSubmitted; // (prompt, targetContext)

        public PromptInputView(TextField input, DropdownField target, Label status, Button sendBtn)
        {
            _input = input;
            _target = target;
            _status = status;
            _sendBtn = sendBtn;

            if (_input != null)
            {
                _input.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == UnityEngine.KeyCode.Return &&
                        (evt.ctrlKey || evt.commandKey) &&
                        !_isGenerating)
                    {
                        Submit();
                        evt.StopPropagation();
                        evt.PreventDefault();
                    }
                });
            }
        }

        public void SetStatus(string text)
        {
            if (_status != null)
                _status.text = text;
        }

        public void SetGenerating(bool generating)
        {
            _isGenerating = generating;
            if (_sendBtn != null)
                _sendBtn.SetEnabled(!generating);
            if (_input != null)
                _input.SetEnabled(!generating);
            if (_sendBtn != null)
                _sendBtn.text = generating ? "Generating..." : "Generate";
        }

        public void AppendStreamChunk(string chunk)
        {
            if (_streamOutput != null)
                _streamOutput.text += chunk;
        }

        public void ClearStreamOutput()
        {
            if (_streamOutput != null)
                _streamOutput.text = "";
        }

        void Submit()
        {
            string prompt = _input?.value;
            string target = _target?.value ?? "Global (auto-place)";
            if (!string.IsNullOrEmpty(prompt))
                OnPromptSubmitted?.Invoke(prompt, target);
        }
    }
}
