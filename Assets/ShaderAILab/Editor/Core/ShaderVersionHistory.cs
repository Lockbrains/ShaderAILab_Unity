using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShaderAILab.Editor.Core
{
    /// <summary>
    /// Tracks revision history for a shader file. Each save or LLM generation
    /// creates a snapshot that can be restored.
    /// Snapshots are stored as JSON files alongside the shader.
    /// </summary>
    [Serializable]
    public class ShaderVersionHistory
    {
        const int MaxSnapshots = 50;
        const string HistoryFolderName = ".ailab_history";

        [Serializable]
        public class Snapshot
        {
            public string Id;
            public DateTime Timestamp;
            public string Description;
            public string Content;

            public Snapshot() { }

            public Snapshot(string description, string content)
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8);
                Timestamp = DateTime.UtcNow;
                Description = description;
                Content = content;
            }
        }

        string _shaderPath;
        List<Snapshot> _snapshots = new List<Snapshot>();

        public IReadOnlyList<Snapshot> Snapshots => _snapshots;

        public ShaderVersionHistory(string shaderPath)
        {
            _shaderPath = shaderPath;
            LoadHistory();
        }

        public void RecordSnapshot(string description, string content)
        {
            _snapshots.Add(new Snapshot(description, content));

            while (_snapshots.Count > MaxSnapshots)
                _snapshots.RemoveAt(0);

            SaveHistory();
        }

        public string GetSnapshotContent(string snapshotId)
        {
            var snap = _snapshots.Find(s => s.Id == snapshotId);
            return snap?.Content;
        }

        public bool RestoreSnapshot(string snapshotId)
        {
            string content = GetSnapshotContent(snapshotId);
            if (content == null || string.IsNullOrEmpty(_shaderPath)) return false;

            RecordSnapshot("Before restore", File.ReadAllText(_shaderPath));
            File.WriteAllText(_shaderPath, content);
            return true;
        }

        string GetHistoryDir()
        {
            if (string.IsNullOrEmpty(_shaderPath)) return null;
            string dir = Path.GetDirectoryName(_shaderPath);
            return Path.Combine(dir, HistoryFolderName);
        }

        string GetHistoryFilePath()
        {
            string histDir = GetHistoryDir();
            if (histDir == null) return null;
            string fileName = Path.GetFileNameWithoutExtension(_shaderPath) + "_history.json";
            return Path.Combine(histDir, fileName);
        }

        void SaveHistory()
        {
            string path = GetHistoryFilePath();
            if (path == null) return;

            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(new SnapshotList { snapshots = _snapshots }, true);
            File.WriteAllText(path, json);
        }

        void LoadHistory()
        {
            string path = GetHistoryFilePath();
            if (path == null || !File.Exists(path))
            {
                _snapshots = new List<Snapshot>();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var list = JsonUtility.FromJson<SnapshotList>(json);
                _snapshots = list?.snapshots ?? new List<Snapshot>();
            }
            catch
            {
                _snapshots = new List<Snapshot>();
            }
        }

        [Serializable]
        class SnapshotList
        {
            public List<Snapshot> snapshots = new List<Snapshot>();
        }
    }
}
