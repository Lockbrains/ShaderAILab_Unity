using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace ShaderAILab.Editor.Core
{
    /// <summary>
    /// Checks shader compilation errors and maps them back to AILab blocks.
    /// </summary>
    public static class ShaderCompileChecker
    {
        public struct CompileError
        {
            public string ShaderName;
            public string Message;
            public int Line;
            public string BlockId;
            public string BlockTitle;
        }

        static string ToRelativePath(string fullPath)
        {
            if (fullPath.StartsWith(Application.dataPath))
                return "Assets" + fullPath.Substring(Application.dataPath.Length);
            return fullPath;
        }

        /// <summary>
        /// Compile-check a shader and map errors to AILab blocks.
        /// </summary>
        public static List<CompileError> Check(ShaderDocument doc)
        {
            var errors = new List<CompileError>();
            if (doc == null || string.IsNullOrEmpty(doc.FilePath)) return errors;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ToRelativePath(doc.FilePath));
            if (shader == null) return errors;

            var messages = ShaderUtil.GetShaderMessages(shader);
            if (messages == null || messages.Length == 0) return errors;
            foreach (var msg in messages)
            {
                if (msg.severity != ShaderCompilerMessageSeverity.Error) continue;

                var error = new CompileError
                {
                    ShaderName = doc.ShaderName,
                    Message = msg.message,
                    Line = msg.line
                };

                foreach (var block in doc.AllBlocks)
                {
                    if (msg.line >= block.StartLine && msg.line <= block.EndLine)
                    {
                        error.BlockId = block.Id;
                        error.BlockTitle = block.Title;
                        break;
                    }
                }

                errors.Add(error);
            }

            return errors;
        }

        public static bool HasErrors(ShaderDocument doc)
        {
            if (doc == null || string.IsNullOrEmpty(doc.FilePath)) return false;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ToRelativePath(doc.FilePath));
            if (shader == null) return true;
            var messages = ShaderUtil.GetShaderMessages(shader);
            if (messages == null) return false;
            foreach (var msg in messages)
            {
                if (msg.severity == ShaderCompilerMessageSeverity.Error)
                    return true;
            }
            return false;
        }
    }
}
