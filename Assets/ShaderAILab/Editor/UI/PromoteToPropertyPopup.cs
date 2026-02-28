using System;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// Popup for promoting a selected variable in code to a ShaderProperty.
    /// Allows choosing property type, display name, default value, and range.
    /// </summary>
    public class PromoteToPropertyPopup : VisualElement
    {
        readonly TextField _nameField;
        readonly TextField _displayNameField;
        readonly DropdownField _typeDropdown;
        readonly TextField _defaultField;
        readonly TextField _minField;
        readonly TextField _maxField;
        readonly VisualElement _rangeRow;

        public event Action<ShaderProperty> OnConfirm;
        public event Action OnCancel;

        public PromoteToPropertyPopup(string initialName)
        {
            AddToClassList("promote-popup");

            var title = new Label("Promote to Property");
            title.style.fontSize = 13;
            title.style.color = new Color(0.88f, 0.88f, 0.88f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            Add(title);

            string propName = initialName.StartsWith("_") ? initialName : "_" + initialName;

            // Name
            _nameField = AddRow("Name", propName);

            // Display name
            string displayName = propName.TrimStart('_');
            displayName = System.Text.RegularExpressions.Regex.Replace(displayName, "(\\B[A-Z])", " $1");
            _displayNameField = AddRow("Display Name", displayName);

            // Type dropdown
            var typeRow = new VisualElement();
            typeRow.AddToClassList("promote-popup__row");
            var typeLabel = new Label("Type");
            typeLabel.AddToClassList("promote-popup__label");
            typeRow.Add(typeLabel);

            _typeDropdown = new DropdownField(
                new System.Collections.Generic.List<string> { "Float", "Range", "Color", "Vector", "Int" },
                0
            );
            _typeDropdown.AddToClassList("promote-popup__field");
            _typeDropdown.RegisterValueChangedCallback(evt =>
            {
                _rangeRow.style.display = evt.newValue == "Range" ? DisplayStyle.Flex : DisplayStyle.None;
            });
            typeRow.Add(_typeDropdown);
            Add(typeRow);

            // Default value
            _defaultField = AddRow("Default", "0");

            // Range row (min/max)
            _rangeRow = new VisualElement();
            _rangeRow.AddToClassList("promote-popup__row");
            _rangeRow.style.display = DisplayStyle.None;

            var minLabel = new Label("Min / Max");
            minLabel.AddToClassList("promote-popup__label");
            _rangeRow.Add(minLabel);

            _minField = new TextField { value = "0" };
            _minField.style.width = 60;
            _rangeRow.Add(_minField);

            var slash = new Label(" / ");
            slash.style.marginLeft = 4;
            slash.style.marginRight = 4;
            _rangeRow.Add(slash);

            _maxField = new TextField { value = "1" };
            _maxField.style.width = 60;
            _rangeRow.Add(_maxField);
            Add(_rangeRow);

            // Actions
            var actions = new VisualElement();
            actions.AddToClassList("promote-popup__actions");

            var confirmBtn = new Button(OnConfirmClicked) { text = "Create Property" };
            confirmBtn.AddToClassList("action-btn");
            confirmBtn.AddToClassList("primary");
            actions.Add(confirmBtn);

            var cancelBtn = new Button(() => OnCancel?.Invoke()) { text = "Cancel" };
            cancelBtn.AddToClassList("action-btn");
            actions.Add(cancelBtn);

            Add(actions);

            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    OnCancel?.Invoke();
                }
            });
        }

        TextField AddRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("promote-popup__row");

            var lbl = new Label(label);
            lbl.AddToClassList("promote-popup__label");
            row.Add(lbl);

            var field = new TextField { value = value };
            field.AddToClassList("promote-popup__field");
            row.Add(field);

            Add(row);
            return field;
        }

        void OnConfirmClicked()
        {
            var prop = new ShaderProperty
            {
                Name = _nameField.value.Trim(),
                DisplayName = _displayNameField.value.Trim(),
                DefaultValue = _defaultField.value.Trim()
            };

            switch (_typeDropdown.value)
            {
                case "Float": prop.PropertyType = ShaderPropertyType.Float; break;
                case "Range":
                    prop.PropertyType = ShaderPropertyType.Range;
                    float.TryParse(_minField.value, out prop.MinValue);
                    float.TryParse(_maxField.value, out prop.MaxValue);
                    break;
                case "Color":
                    prop.PropertyType = ShaderPropertyType.Color;
                    if (string.IsNullOrEmpty(prop.DefaultValue) || prop.DefaultValue == "0")
                        prop.DefaultValue = "(1,1,1,1)";
                    break;
                case "Vector":
                    prop.PropertyType = ShaderPropertyType.Vector;
                    if (string.IsNullOrEmpty(prop.DefaultValue) || prop.DefaultValue == "0")
                        prop.DefaultValue = "(0,0,0,0)";
                    break;
                case "Int": prop.PropertyType = ShaderPropertyType.Int; break;
            }

            if (string.IsNullOrEmpty(prop.Name))
            {
                return;
            }

            OnConfirm?.Invoke(prop);
        }
    }
}
