using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    /// <summary>
    /// A GraphView that visualises the Attributes->Varyings data flow.
    /// Three nodes: Attributes (left), Globals (center), Varyings (right),
    /// aligned top with edges representing vertex-stage transforms.
    /// Node positions are persisted in DataFlowGraph.NodePositions.
    /// </summary>
    public class DataFlowGraphView : GraphView
    {
        const string NodeIdAttributes = "node_attributes";
        const string NodeIdVaryings   = "node_varyings";
        const string NodeIdGlobals    = "node_globals";
        const string NodeIdOptions    = "node_options";

        static readonly Vector2 DefaultAttrPos    = new Vector2(80, 80);
        static readonly Vector2 DefaultGlobalsPos = new Vector2(380, 80);
        static readonly Vector2 DefaultVaryPos    = new Vector2(680, 80);
        static readonly Vector2 DefaultOptionsPos = new Vector2(380, 440);

        DataFlowGraph _graph;
        ShaderPass _currentPass;
        ShaderGlobalSettings _globalSettings;
        DataFlowNodeView _attrNode;
        DataFlowNodeView _varyNode;
        DataFlowNodeView _globalsNode;
        ShaderOptionsNodeView _optionsNode;
        DataFlowFieldListPanel _fieldListPanel;
        Label _errorLabel;

        public event Action OnGraphChanged;

        public DataFlowGraphView()
        {
            style.flexGrow = 1;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var bg = new VisualElement();
            bg.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            bg.StretchToParentSize();
            Insert(0, bg);

            _errorLabel = new Label();
            _errorLabel.style.position = Position.Absolute;
            _errorLabel.style.bottom = 8;
            _errorLabel.style.left = 8;
            _errorLabel.style.right = 8;
            _errorLabel.style.fontSize = 11;
            _errorLabel.style.color = new Color(0.95f, 0.3f, 0.3f);
            _errorLabel.style.whiteSpace = WhiteSpace.Normal;
            _errorLabel.style.display = DisplayStyle.None;
            Add(_errorLabel);

            _fieldListPanel = new DataFlowFieldListPanel();
            _fieldListPanel.OnFieldActivateRequested += OnFieldActivateFromPanel;
            _fieldListPanel.style.position = Position.Absolute;
            _fieldListPanel.style.top = 8;
            _fieldListPanel.style.right = 8;
            _fieldListPanel.style.width = 220;
            Add(_fieldListPanel);

            graphViewChanged += OnGraphViewChanged;
        }

        public void Rebuild(DataFlowGraph graph)
        {
            Rebuild(graph, null);
        }

        public void Rebuild(DataFlowGraph graph, ShaderPass pass, ShaderGlobalSettings globalSettings = null)
        {
            _graph = graph;
            _currentPass = pass;
            _globalSettings = globalSettings;

            var edgeList = new List<GraphElement>();
            foreach (var e in edges) edgeList.Add(e);
            DeleteElements(edgeList);
            if (_attrNode != null) { RemoveElement(_attrNode); _attrNode = null; }
            if (_varyNode != null) { RemoveElement(_varyNode); _varyNode = null; }
            if (_globalsNode != null) { RemoveElement(_globalsNode); _globalsNode = null; }
            if (_optionsNode != null) { RemoveElement(_optionsNode); _optionsNode = null; }

            var errors = graph.Validate();

            // Attributes node
            _attrNode = new DataFlowNodeView(DataFlowStage.Attributes);
            _attrNode.SetPosition(GetSavedOrDefaultRect(NodeIdAttributes, DefaultAttrPos, 260, 400));
            _attrNode.OnFieldToggled += OnAttributeFieldToggled;
            _attrNode.Rebuild(graph.AttributeFields, errors);
            AddElement(_attrNode);

            // Varyings node
            _varyNode = new DataFlowNodeView(DataFlowStage.Varyings);
            _varyNode.SetPosition(GetSavedOrDefaultRect(NodeIdVaryings, DefaultVaryPos, 260, 400));
            _varyNode.OnFieldToggled += OnVaryingFieldToggled;
            _varyNode.Rebuild(graph.VaryingFields, errors);
            AddElement(_varyNode);

            // Globals node
            var globalFields = new List<DataFlowField>();
            foreach (var g in DataFlowRegistry.AllGlobals)
            {
                var clone = g.Clone();
                clone.IsActive = true;
                globalFields.Add(clone);
            }
            _globalsNode = new DataFlowNodeView(DataFlowStage.Global);
            _globalsNode.SetPosition(GetSavedOrDefaultRect(NodeIdGlobals, DefaultGlobalsPos, 260, 300));
            _globalsNode.Rebuild(globalFields, null);
            AddElement(_globalsNode);

            // Shader Options node
            if (pass != null)
            {
                if (pass.RenderState == null)
                    pass.RenderState = new PassRenderState();

                _optionsNode = new ShaderOptionsNodeView();
                _optionsNode.SetPosition(GetSavedOrDefaultRect(NodeIdOptions, DefaultOptionsPos, 280, 360));
                _optionsNode.OnOptionsChanged += () => OnGraphChanged?.Invoke();
                _optionsNode.Rebuild(pass.RenderState, _globalSettings);
                AddElement(_optionsNode);
            }

            // Edges
            foreach (var dep in graph.GetActiveDependencies())
            {
                var outPort = _attrNode.GetPort(dep.SourceFieldName);
                var inPort = _varyNode.GetPort(dep.TargetFieldName);
                if (outPort != null && inPort != null)
                {
                    var edge = outPort.ConnectTo(inPort);
                    edge.tooltip = dep.Description;
                    AddElement(edge);
                }
            }

            UpdateErrorLabel(errors);
            _fieldListPanel.Rebuild(graph);
        }

        Rect GetSavedOrDefaultRect(string nodeId, Vector2 defaultPos, float w, float h)
        {
            if (_graph != null)
            {
                var saved = _graph.GetNodePosition(nodeId);
                if (saved != null)
                    return new Rect(saved.X, saved.Y, w, h);
            }
            return new Rect(defaultPos.x, defaultPos.y, w, h);
        }

        // Persist node positions when nodes are moved
        GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.movedElements != null && _graph != null)
            {
                foreach (var elem in change.movedElements)
                {
                    if (elem == _attrNode)
                        _graph.SaveNodePosition(NodeIdAttributes, _attrNode.GetPosition().x, _attrNode.GetPosition().y);
                    else if (elem == _varyNode)
                        _graph.SaveNodePosition(NodeIdVaryings, _varyNode.GetPosition().x, _varyNode.GetPosition().y);
                    else if (elem == _globalsNode)
                        _graph.SaveNodePosition(NodeIdGlobals, _globalsNode.GetPosition().x, _globalsNode.GetPosition().y);
                    else if (elem == _optionsNode)
                        _graph.SaveNodePosition(NodeIdOptions, _optionsNode.GetPosition().x, _optionsNode.GetPosition().y);
                }
                OnGraphChanged?.Invoke();
            }
            return change;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return new List<Port>();
        }

        void OnAttributeFieldToggled(DataFlowField field, bool active)
        {
            if (_graph == null) return;
            _graph.SetFieldActive(field.Name, DataFlowStage.Attributes, active);
            Rebuild(_graph);
            OnGraphChanged?.Invoke();
        }

        void OnVaryingFieldToggled(DataFlowField field, bool active)
        {
            if (_graph == null) return;

            if (active)
                _graph.ActivateVaryingWithDependencies(field.Name);
            else
                _graph.DeactivateVarying(field.Name);

            Rebuild(_graph);
            OnGraphChanged?.Invoke();
        }

        void OnFieldActivateFromPanel(string fieldName, DataFlowStage stage)
        {
            if (_graph == null) return;

            if (stage == DataFlowStage.Varyings)
                _graph.ActivateVaryingWithDependencies(fieldName);
            else
                _graph.SetFieldActive(fieldName, stage, true);

            Rebuild(_graph);
            OnGraphChanged?.Invoke();
        }

        void UpdateErrorLabel(List<DataFlowError> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                _errorLabel.style.display = DisplayStyle.None;
                return;
            }

            _errorLabel.style.display = DisplayStyle.Flex;
            var sb = new System.Text.StringBuilder();
            foreach (var e in errors)
                sb.AppendLine(e.Message);
            _errorLabel.text = sb.ToString().TrimEnd();
        }
    }
}
