using System;
using System.Collections.Generic;

namespace ShaderAILab.Editor.Core
{
    [Serializable]
    public class DataFlowDependency
    {
        public string SourceFieldName;
        public string TargetFieldName;
        public string TransformCode;
        public string Description;

        public DataFlowDependency() { }

        public DataFlowDependency(string source, string target, string transformCode, string description)
        {
            SourceFieldName = source;
            TargetFieldName = target;
            TransformCode = transformCode;
            Description = description;
        }
    }

    [Serializable]
    public class DataFlowError
    {
        public string FieldName;
        public DataFlowStage Stage;
        public string Message;

        public DataFlowError(string fieldName, DataFlowStage stage, string message)
        {
            FieldName = fieldName;
            Stage = stage;
            Message = message;
        }
    }

    [Serializable]
    public class NodePositionData
    {
        public string NodeId;
        public float X;
        public float Y;

        public NodePositionData() { }

        public NodePositionData(string nodeId, float x, float y)
        {
            NodeId = nodeId;
            X = x;
            Y = y;
        }
    }

    [Serializable]
    public class DataFlowGraph
    {
        public List<DataFlowField> AttributeFields = new List<DataFlowField>();
        public List<DataFlowField> VaryingFields = new List<DataFlowField>();
        public List<NodePositionData> NodePositions = new List<NodePositionData>();

        public void SaveNodePosition(string nodeId, float x, float y)
        {
            var existing = NodePositions.Find(p => p.NodeId == nodeId);
            if (existing != null)
            {
                existing.X = x;
                existing.Y = y;
            }
            else
            {
                NodePositions.Add(new NodePositionData(nodeId, x, y));
            }
        }

        public NodePositionData GetNodePosition(string nodeId)
        {
            return NodePositions.Find(p => p.NodeId == nodeId);
        }

        public DataFlowField FindField(string name, DataFlowStage stage)
        {
            var list = stage == DataFlowStage.Attributes ? AttributeFields : VaryingFields;
            return list.Find(f => f.Name == name);
        }

        public List<DataFlowField> GetActiveFields(DataFlowStage stage)
        {
            var list = stage == DataFlowStage.Attributes ? AttributeFields : VaryingFields;
            return list.FindAll(f => f.IsActive);
        }

        public void SetFieldActive(string name, DataFlowStage stage, bool active)
        {
            var field = FindField(name, stage);
            if (field != null && !field.IsRequired)
                field.IsActive = active;
        }

        /// <summary>
        /// Activate a Varyings field and auto-activate required Attributes dependencies.
        /// Returns list of fields that were auto-activated.
        /// </summary>
        public List<string> ActivateVaryingWithDependencies(string varyingFieldName)
        {
            var activated = new List<string>();
            var field = FindField(varyingFieldName, DataFlowStage.Varyings);
            if (field == null) return activated;

            field.IsActive = true;

            foreach (var dep in DataFlowRegistry.GetDependencies(varyingFieldName))
            {
                var srcField = FindField(dep.SourceFieldName, DataFlowStage.Attributes);
                if (srcField != null && !srcField.IsActive)
                {
                    srcField.IsActive = true;
                    activated.Add(srcField.Name);
                }
            }

            return activated;
        }

        /// <summary>
        /// Deactivate a Varyings field. Does NOT auto-deactivate Attributes fields
        /// because other Varyings might still need them.
        /// </summary>
        public void DeactivateVarying(string varyingFieldName)
        {
            var field = FindField(varyingFieldName, DataFlowStage.Varyings);
            if (field != null && !field.IsRequired)
                field.IsActive = false;
        }

        /// <summary>
        /// Get all active dependencies (edges) based on currently active fields.
        /// </summary>
        public List<DataFlowDependency> GetActiveDependencies()
        {
            var result = new List<DataFlowDependency>();
            foreach (var vf in VaryingFields)
            {
                if (!vf.IsActive) continue;
                foreach (var dep in DataFlowRegistry.GetDependencies(vf.Name))
                {
                    var srcField = FindField(dep.SourceFieldName, DataFlowStage.Attributes);
                    if (srcField != null && srcField.IsActive)
                        result.Add(dep);
                }
            }
            return result;
        }

        /// <summary>
        /// Validate the graph: check that every active Varying field has its
        /// Attributes dependencies also active.
        /// </summary>
        public List<DataFlowError> Validate()
        {
            var errors = new List<DataFlowError>();
            foreach (var vf in VaryingFields)
            {
                if (!vf.IsActive) continue;
                foreach (var dep in DataFlowRegistry.GetDependencies(vf.Name))
                {
                    var srcField = FindField(dep.SourceFieldName, DataFlowStage.Attributes);
                    if (srcField == null || !srcField.IsActive)
                    {
                        errors.Add(new DataFlowError(
                            vf.Name, DataFlowStage.Varyings,
                            $"'{vf.DisplayName}' requires '{dep.SourceFieldName}' in Attributes, but it is not active."));
                    }
                }
            }
            return errors;
        }

        /// <summary>
        /// Build a default graph with standard URP fields from the registry.
        /// </summary>
        public static DataFlowGraph CreateDefault()
        {
            var graph = new DataFlowGraph();
            foreach (var proto in DataFlowRegistry.AllAttributes)
                graph.AttributeFields.Add(proto.Clone());
            foreach (var proto in DataFlowRegistry.AllVaryings)
                graph.VaryingFields.Add(proto.Clone());
            return graph;
        }

        /// <summary>
        /// Reset all fields to registry defaults (required fields active, others inactive).
        /// Preserves annotations.
        /// </summary>
        public void ResetToDefaults()
        {
            foreach (var f in AttributeFields) f.IsActive = f.IsRequired;
            foreach (var f in VaryingFields) f.IsActive = f.IsRequired;
        }
    }
}
