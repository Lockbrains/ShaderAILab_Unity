using System;

namespace ShaderAILab.Editor.Core
{
    [Serializable]
    public class LLMHistoryEntry
    {
        public string Id;
        public DateTime Timestamp;
        public string UserPrompt;
        public string TargetContext;
        public string ResponseSummary;
        public string FullResponse;
        public bool Success;
        public string Error;

        public LLMHistoryEntry()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Timestamp = DateTime.Now;
            Success = true;
        }

        public LLMHistoryEntry(string prompt, string targetContext) : this()
        {
            UserPrompt = prompt;
            TargetContext = targetContext;
        }
    }
}
