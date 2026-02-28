using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ShaderAILab.Editor.Core
{
    /// <summary>
    /// Watches a .shader file for external modifications (e.g. from VSCode).
    /// Uses file hash comparison on EditorApplication.update to detect changes
    /// without relying on FileSystemWatcher (which can be unreliable on some OS).
    /// </summary>
    [InitializeOnLoad]
    public class ShaderFileWatcher
    {
        static ShaderFileWatcher _instance;
        string _watchedPath;
        string _lastHash;
        DateTime _lastCheck;
        bool _enabled;

        /// <summary>Fires when the watched file is modified externally.</summary>
        public event Action<string> OnFileChanged;

        /// <summary>Fires when the file's AILab tags appear to be damaged.</summary>
        public event Action<string> OnTagsDamaged;

        const float CheckIntervalSeconds = 1.5f;

        static ShaderFileWatcher()
        {
            EditorApplication.update += StaticUpdate;
        }

        public static ShaderFileWatcher Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new ShaderFileWatcher();
                return _instance;
            }
        }

        public void Watch(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Stop();
                return;
            }

            _watchedPath = filePath;
            _lastHash = ComputeHash(filePath);
            _lastCheck = DateTime.UtcNow;
            _enabled = true;
        }

        public void Stop()
        {
            _enabled = false;
            _watchedPath = null;
            _lastHash = null;
        }

        /// <summary>
        /// Update the stored hash to the current file state
        /// (call after we write to the file ourselves so we don't self-trigger).
        /// </summary>
        public void AcknowledgeWrite()
        {
            if (!string.IsNullOrEmpty(_watchedPath) && File.Exists(_watchedPath))
                _lastHash = ComputeHash(_watchedPath);
        }

        static void StaticUpdate()
        {
            _instance?.Update();
        }

        void Update()
        {
            if (!_enabled || string.IsNullOrEmpty(_watchedPath)) return;

            if ((DateTime.UtcNow - _lastCheck).TotalSeconds < CheckIntervalSeconds)
                return;

            _lastCheck = DateTime.UtcNow;

            if (!File.Exists(_watchedPath))
            {
                Stop();
                return;
            }

            string currentHash = ComputeHash(_watchedPath);
            if (currentHash == _lastHash) return;

            _lastHash = currentHash;
            Debug.Log($"[ShaderAILab] External modification detected: {_watchedPath}");

            // Quick tag health check
            string content = File.ReadAllText(_watchedPath);
            if (content.Contains("[AILab_Block_Start") && !content.Contains("[AILab_Block_End]"))
            {
                OnTagsDamaged?.Invoke(_watchedPath);
            }

            OnFileChanged?.Invoke(_watchedPath);
        }

        static string ComputeHash(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    var sb = new StringBuilder(32);
                    foreach (byte b in hash)
                        sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
