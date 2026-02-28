using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Builds the right-panel parameter UI with sliders for numeric properties
    /// and color fields for color properties.
    /// </summary>
    public class ParameterPanelView
    {
        readonly VisualElement _container;

        public event Action<string, object> OnParameterChanged;

        public ParameterPanelView(VisualElement container)
        {
            _container = container;
        }

        public void Rebuild(ShaderDocument doc)
        {
            _container.Clear();
            if (doc == null) return;

            foreach (var prop in doc.Properties)
                _container.Add(CreateParameterItem(prop));
        }

        VisualElement CreateParameterItem(ShaderProperty prop)
        {
            var item = new VisualElement();
            item.AddToClassList("param-item");

            switch (prop.PropertyType)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    AddSlider(item, prop);
                    break;
                case ShaderPropertyType.Int:
                    AddIntSlider(item, prop);
                    break;
                case ShaderPropertyType.Color:
                    AddColorField(item, prop);
                    break;
                case ShaderPropertyType.Vector:
                    AddVectorField(item, prop);
                    break;
                default:
                    item.Add(new Label($"({prop.PropertyType})"));
                    break;
            }

            return item;
        }

        void AddSlider(VisualElement parent, ShaderProperty prop)
        {
            float defaultVal = 0f;
            float.TryParse(prop.DefaultValue, out defaultVal);

            float min = prop.PropertyType == ShaderPropertyType.Range ? prop.MinValue : 0f;
            float max = prop.PropertyType == ShaderPropertyType.Range ? prop.MaxValue : 1f;

            // Houdini-style draggable field as the primary control
            var draggable = new DraggableFloatField(prop.DisplayName, defaultVal, min, max);
            draggable.OnValueChanged += val => OnParameterChanged?.Invoke(prop.Name, val);
            parent.Add(draggable);
        }

        void AddIntSlider(VisualElement parent, ShaderProperty prop)
        {
            int defaultVal = 0;
            int.TryParse(prop.DefaultValue, out defaultVal);

            var slider = new SliderInt((int)prop.MinValue, (int)prop.MaxValue) { value = defaultVal };
            slider.RegisterValueChangedCallback(evt =>
                OnParameterChanged?.Invoke(prop.Name, evt.newValue));
            parent.Add(slider);
        }

        void AddColorField(VisualElement parent, ShaderProperty prop)
        {
            Color defaultColor = Color.white;
            if (!string.IsNullOrEmpty(prop.DefaultValue))
                TryParseColor(prop.DefaultValue, out defaultColor);

            var colorField = new ColorField { value = defaultColor };
            colorField.RegisterValueChangedCallback(evt =>
                OnParameterChanged?.Invoke(prop.Name, evt.newValue));
            parent.Add(colorField);
        }

        void AddVectorField(VisualElement parent, ShaderProperty prop)
        {
            var vectorField = new Vector4Field();
            vectorField.RegisterValueChangedCallback(evt =>
                OnParameterChanged?.Invoke(prop.Name, evt.newValue));
            parent.Add(vectorField);
        }

        static bool TryParseColor(string str, out Color color)
        {
            color = Color.white;
            str = str.Trim('(', ')');
            var parts = str.Split(',');
            if (parts.Length >= 3)
            {
                float.TryParse(parts[0].Trim(), out float r);
                float.TryParse(parts[1].Trim(), out float g);
                float.TryParse(parts[2].Trim(), out float b);
                float a = 1f;
                if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out a);
                color = new Color(r, g, b, a);
                return true;
            }
            return false;
        }
    }
}
