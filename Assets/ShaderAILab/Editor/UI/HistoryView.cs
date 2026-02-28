using System;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    public class HistoryView
    {
        readonly VisualElement _container;
        ScrollView _scrollView;
        Label _emptyLabel;

        public event Action<string> OnResendRequested;

        public HistoryView(VisualElement container)
        {
            _container = container;
            Build();
        }

        void Build()
        {
            _container.Clear();

            var header = new VisualElement();
            header.AddToClassList("history-header");

            var title = new Label("LLM Operation History");
            title.AddToClassList("panel-header");
            header.Add(title);

            var clearBtn = new Button { text = "Clear" };
            clearBtn.AddToClassList("history-clear-btn");
            clearBtn.clicked += OnClearClicked;
            header.Add(clearBtn);

            _container.Add(header);

            _emptyLabel = new Label("No operations yet. Use the AI Prompt to generate shader code.");
            _emptyLabel.AddToClassList("history-empty");
            _container.Add(_emptyLabel);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.AddToClassList("history-scroll");
            _container.Add(_scrollView);
        }

        LLMHistory _boundHistory;

        public void Bind(LLMHistory history)
        {
            if (_boundHistory != null)
                _boundHistory.OnHistoryChanged -= Refresh;

            _boundHistory = history;

            if (_boundHistory != null)
                _boundHistory.OnHistoryChanged += Refresh;

            Refresh();
        }

        public void Refresh()
        {
            _scrollView.Clear();

            if (_boundHistory == null || _boundHistory.Entries.Count == 0)
            {
                _emptyLabel.style.display = DisplayStyle.Flex;
                _scrollView.style.display = DisplayStyle.None;
                return;
            }

            _emptyLabel.style.display = DisplayStyle.None;
            _scrollView.style.display = DisplayStyle.Flex;

            for (int i = _boundHistory.Entries.Count - 1; i >= 0; i--)
                _scrollView.Add(CreateEntryCard(_boundHistory.Entries[i]));
        }

        VisualElement CreateEntryCard(LLMHistoryEntry entry)
        {
            var card = new VisualElement();
            card.AddToClassList("history-card");
            card.AddToClassList(entry.Success ? "history-card--success" : "history-card--error");

            // Top row: timestamp + target
            var topRow = new VisualElement();
            topRow.AddToClassList("history-card__top");

            var time = new Label(entry.Timestamp.ToString("HH:mm:ss"));
            time.AddToClassList("history-card__time");
            topRow.Add(time);

            var target = new Label(entry.TargetContext ?? "");
            target.AddToClassList("history-card__target");
            topRow.Add(target);

            var status = new Label(entry.Success ? "OK" : "ERR");
            status.AddToClassList("history-card__status");
            status.AddToClassList(entry.Success ? "history-card__status--ok" : "history-card__status--err");
            topRow.Add(status);

            card.Add(topRow);

            // Prompt text
            var prompt = new Label(Truncate(entry.UserPrompt, 120));
            prompt.AddToClassList("history-card__prompt");
            card.Add(prompt);

            // Summary
            if (!string.IsNullOrEmpty(entry.ResponseSummary))
            {
                var summary = new Label(entry.ResponseSummary);
                summary.AddToClassList("history-card__summary");
                card.Add(summary);
            }

            if (!string.IsNullOrEmpty(entry.Error))
            {
                var error = new Label(entry.Error);
                error.AddToClassList("history-card__error-text");
                card.Add(error);
            }

            // Expandable response section
            var responseContainer = new VisualElement();
            responseContainer.AddToClassList("history-card__response");
            responseContainer.style.display = DisplayStyle.None;

            if (!string.IsNullOrEmpty(entry.FullResponse))
            {
                var responseText = new Label(entry.FullResponse);
                responseText.AddToClassList("history-card__response-text");
                responseContainer.Add(responseText);
            }

            card.Add(responseContainer);

            // Bottom row: actions
            var bottomRow = new VisualElement();
            bottomRow.AddToClassList("history-card__bottom");

            var toggleBtn = new Button { text = "Show Response" };
            toggleBtn.AddToClassList("history-card__toggle-btn");
            bool expanded = false;
            toggleBtn.clicked += () =>
            {
                expanded = !expanded;
                responseContainer.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                toggleBtn.text = expanded ? "Hide Response" : "Show Response";
            };
            bottomRow.Add(toggleBtn);

            var resendBtn = new Button { text = "Resend" };
            resendBtn.AddToClassList("history-card__resend-btn");
            string promptText = entry.UserPrompt;
            resendBtn.clicked += () => OnResendRequested?.Invoke(promptText);
            bottomRow.Add(resendBtn);

            card.Add(bottomRow);

            return card;
        }

        void OnClearClicked()
        {
            _boundHistory?.Clear();
        }

        static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }
}
