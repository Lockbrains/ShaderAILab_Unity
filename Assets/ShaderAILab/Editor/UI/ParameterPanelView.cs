using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;
using Object = UnityEngine.Object;

namespace ShaderAILab.Editor.UI
{
    public class ParameterPanelView
    {
        readonly VisualElement _container;
        readonly VisualElement _toggleRow;
        readonly Toggle _showAllToggle;
        readonly VisualElement _itemsContainer;

        ShaderDocument _currentDoc;
        ShaderBlock _currentBlock;
        bool _showAll;

        public event Action<string, object> OnParameterChanged;

        public ParameterPanelView(VisualElement container)
        {
            _container = container;

            _toggleRow = new VisualElement();
            _toggleRow.AddToClassList("param-toggle-row");

            var label = new Label("Show All Parameters");
            label.AddToClassList("param-toggle-label");
            _toggleRow.Add(label);

            _showAllToggle = new Toggle { value = false };
            _showAllToggle.RegisterValueChangedCallback(evt =>
            {
                _showAll = evt.newValue;
                RebuildItems();
            });
            _toggleRow.Add(_showAllToggle);

            _itemsContainer = new VisualElement();
        }

        /// <summary>
        /// Rebuild showing only parameters referenced by the given block (with Show All toggle).
        /// </summary>
        public void RebuildForBlock(ShaderDocument doc, ShaderBlock block)
        {
            _currentDoc = doc;
            _currentBlock = block;
            RebuildItems();
        }

        /// <summary>
        /// Rebuild showing all parameters (legacy path, used when no block context).
        /// </summary>
        public void Rebuild(ShaderDocument doc)
        {
            _currentDoc = doc;
            _currentBlock = null;
            RebuildItems();
        }

        void RebuildItems()
        {
            _container.Clear();
            if (_currentDoc == null) return;

            List<ShaderProperty> propsToShow;
            bool hasBlockContext = _currentBlock != null && _currentBlock.ReferencedParams.Count > 0;

            if (hasBlockContext)
            {
                _container.Add(_toggleRow);

                if (_showAll)
                {
                    propsToShow = _currentDoc.Properties;
                }
                else
                {
                    propsToShow = _currentDoc.Properties
                        .Where(p => _currentBlock.ReferencedParams.Contains(p.Name))
                        .ToList();
                }
            }
            else
            {
                propsToShow = _currentDoc.Properties;
            }

            _container.Add(_itemsContainer);
            _itemsContainer.Clear();

            if (propsToShow.Count == 0)
            {
                var emptyLabel = new Label(hasBlockContext && !_showAll
                    ? "This block has no referenced parameters."
                    : "No parameters defined.");
                emptyLabel.AddToClassList("param-empty-label");
                _itemsContainer.Add(emptyLabel);
                return;
            }

            foreach (var prop in propsToShow)
                _itemsContainer.Add(CreateParameterItem(prop));
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
                case ShaderPropertyType.Texture2D:
                    AddTextureField(item, prop, typeof(Texture2D));
                    break;
                case ShaderPropertyType.Texture3D:
                    AddTextureField(item, prop, typeof(Texture3D));
                    break;
                case ShaderPropertyType.Cubemap:
                    AddTextureField(item, prop, typeof(Cubemap));
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

        void AddTextureField(VisualElement parent, ShaderProperty prop, System.Type textureType)
        {
            var label = new Label(prop.DisplayName);
            label.AddToClassList("param-item__label");
            parent.Add(label);

            var objField = new ObjectField();
            objField.objectType = textureType;
            objField.allowSceneObjects = false;
            objField.RegisterValueChangedCallback(evt =>
                OnParameterChanged?.Invoke(prop.Name, evt.newValue));
            parent.Add(objField);
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
