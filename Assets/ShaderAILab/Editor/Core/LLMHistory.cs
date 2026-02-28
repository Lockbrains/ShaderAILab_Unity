using System;
using System.Collections.Generic;

namespace ShaderAILab.Editor.Core
{
    [Serializable]
    public class LLMHistory
    {
        const int MaxEntries = 100;

        public List<LLMHistoryEntry> Entries = new List<LLMHistoryEntry>();

        public event Action OnHistoryChanged;

        public void Add(LLMHistoryEntry entry)
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
            OnHistoryChanged?.Invoke();
        }

        public void Clear()
        {
            Entries.Clear();
            OnHistoryChanged?.Invoke();
        }

        public LLMHistoryEntry RecordStart(string prompt, string targetContext)
        {
            var entry = new LLMHistoryEntry(prompt, targetContext);
            return entry;
        }

        public void RecordSuccess(LLMHistoryEntry entry, string fullResponse, string summary)
        {
            entry.Success = true;
            entry.FullResponse = fullResponse;
            entry.ResponseSummary = summary;
            Add(entry);
        }

        public void RecordFailure(LLMHistoryEntry entry, string error)
        {
            entry.Success = false;
            entry.Error = error;
            Add(entry);
        }
    }
}
