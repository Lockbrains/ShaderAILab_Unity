using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Houdini-style draggable float field: click and drag horizontally on the label
    /// or the value to change the number. Supports configurable sensitivity and range.
    /// </summary>
    public class DraggableFloatField : VisualElement
    {
        readonly Label _label;
        readonly Label _valueLabel;
        readonly VisualElement _track;
        readonly VisualElement _fill;

        float _value;
        float _min;
        float _max;
        float _sensitivity;
        bool _isDragging;
        float _dragStartX;
        float _dragStartValue;

        public event Action<float> OnValueChanged;

        public float Value
        {
            get => _value;
            set => SetValue(value, true);
        }

        public DraggableFloatField(string label, float defaultValue, float min, float max, float sensitivity = 0.01f)
        {
            _min = min;
            _max = max;
            _sensitivity = sensitivity;

            style.flexDirection = FlexDirection.Column;
            style.marginBottom = 4;
            style.marginTop = 2;

            // Top row: label + value
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.SpaceBetween;
            topRow.style.paddingLeft = 4;
            topRow.style.paddingRight = 4;

            _label = new Label(label);
            _label.style.fontSize = 11;
            _label.style.color = new Color(0.7f, 0.7f, 0.7f);
            _label.style.unityTextAlign = TextAnchor.MiddleLeft;
            topRow.Add(_label);

            _valueLabel = new Label();
            _valueLabel.style.fontSize = 11;
            _valueLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _valueLabel.style.minWidth = 40;
            topRow.Add(_valueLabel);

            Add(topRow);

            // Track + fill bar (like a mini progress bar)
            _track = new VisualElement();
            _track.style.height = 6;
            _track.style.marginTop = 2;
            _track.style.marginLeft = 4;
            _track.style.marginRight = 4;
            _track.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            _track.style.borderTopLeftRadius = 3;
            _track.style.borderTopRightRadius = 3;
            _track.style.borderBottomLeftRadius = 3;
            _track.style.borderBottomRightRadius = 3;
            _track.style.overflow = Overflow.Hidden;

            _fill = new VisualElement();
            _fill.style.height = new StyleLength(Length.Percent(100));
            _fill.style.backgroundColor = new Color(0.2f, 0.5f, 0.8f, 0.8f);
            _fill.style.borderTopLeftRadius = 3;
            _fill.style.borderBottomLeftRadius = 3;
            _track.Add(_fill);

            Add(_track);

            SetValue(defaultValue, false);

            // Drag handling on the entire element
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
            RegisterCallback<MouseLeaveEvent>(OnMouseLeave);

            style.cursor = new UnityEngine.UIElements.Cursor();
        }

        void SetValue(float newValue, bool notify)
        {
            _value = Mathf.Clamp(newValue, _min, _max);
            _valueLabel.text = _value.ToString("F3");

            float t = (_max - _min) > 0.0001f ? (_value - _min) / (_max - _min) : 0f;
            _fill.style.width = new StyleLength(Length.Percent(t * 100f));

            if (notify)
                OnValueChanged?.Invoke(_value);
        }

        void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0) return;
            _isDragging = true;
            _dragStartX = evt.mousePosition.x;
            _dragStartValue = _value;
            this.CaptureMouse();
            evt.StopPropagation();
        }

        void OnMouseMove(MouseMoveEvent evt)
        {
            if (!_isDragging) return;
            float delta = (evt.mousePosition.x - _dragStartX) * _sensitivity;
            SetValue(_dragStartValue + delta * (_max - _min), true);
            evt.StopPropagation();
        }

        void OnMouseUp(MouseUpEvent evt)
        {
            if (!_isDragging) return;
            _isDragging = false;
            this.ReleaseMouse();
            evt.StopPropagation();
        }

        void OnMouseLeave(MouseLeaveEvent evt)
        {
            // Keep dragging even if mouse leaves, as CaptureMouse handles it
        }
    }
}
