using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    public class PlanView
    {
        readonly VisualElement _container;
        ShaderDocument _document;

        VisualElement _headerRow;
        Label _statusBadge;
        Button _newPlanBtn;
        Button _resetBtn;

        VisualElement _emptyState;
        TextField _requestInput;
        Button _createBtn;

        VisualElement _planBody;
        Label _requestLabel;
        ScrollView _phasesScroll;

        VisualElement _executeBar;
        Button _executeBtn;
        Label _progressLabel;
        VisualElement _progressFill;

        readonly List<PlanPhaseCard> _phaseCards = new List<PlanPhaseCard>();

        public event Action<string> OnCreatePlanRequested;
        public event Action<string> OnPhaseConfirmed;
        public event Action<string> OnPhaseSkipped;
        public event Action<string, string> OnPhaseFeedbackSent;
        public event Action OnExecutePlanRequested;
        public event Action OnResetPlanRequested;

        public PlanView(VisualElement container)
        {
            _container = container;
            Build();
        }

        void Build()
        {
            _container.Clear();

            // --- Header ---
            _headerRow = new VisualElement();
            _headerRow.AddToClassList("plan-header");

            var title = new Label("Shader Plan");
            title.AddToClassList("panel-header");
            _headerRow.Add(title);

            _statusBadge = new Label("Empty");
            _statusBadge.AddToClassList("plan-status-badge");
            _headerRow.Add(_statusBadge);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            _headerRow.Add(spacer);

            _newPlanBtn = new Button { text = "New Plan" };
            _newPlanBtn.AddToClassList("plan-action-btn");
            _newPlanBtn.clicked += () =>
            {
                ShowEmptyState();
            };
            _headerRow.Add(_newPlanBtn);

            _resetBtn = new Button { text = "Reset" };
            _resetBtn.AddToClassList("plan-action-btn");
            _resetBtn.AddToClassList("plan-action-btn--danger");
            _resetBtn.clicked += () => OnResetPlanRequested?.Invoke();
            _headerRow.Add(_resetBtn);

            _container.Add(_headerRow);

            // --- Empty state (request input) ---
            _emptyState = new VisualElement();
            _emptyState.AddToClassList("plan-empty-state");

            var emptyTitle = new Label("Create a Shader Plan");
            emptyTitle.AddToClassList("plan-empty-title");
            _emptyState.Add(emptyTitle);

            var emptyDesc = new Label(
                "Describe the shader effect you want to create. The AI will decompose it into manageable phases: " +
                "visual analysis, data flow, textures, vertex/fragment shaders, options, and multi-pass setup.");
            emptyDesc.AddToClassList("plan-empty-desc");
            _emptyState.Add(emptyDesc);

            _requestInput = new TextField();
            _requestInput.multiline = true;
            _requestInput.AddToClassList("plan-request-input");
            _requestInput.value = "";
            _emptyState.Add(_requestInput);

            _createBtn = new Button { text = "Generate Plan" };
            _createBtn.AddToClassList("action-btn");
            _createBtn.AddToClassList("primary");
            _createBtn.AddToClassList("plan-create-btn");
            _createBtn.clicked += () =>
            {
                string request = _requestInput.value?.Trim();
                if (!string.IsNullOrEmpty(request))
                    OnCreatePlanRequested?.Invoke(request);
            };
            _emptyState.Add(_createBtn);

            _container.Add(_emptyState);

            // --- Plan body (shown when a plan exists) ---
            _planBody = new VisualElement();
            _planBody.AddToClassList("plan-body");
            _planBody.style.display = DisplayStyle.None;

            var requestSection = new VisualElement();
            requestSection.AddToClassList("plan-request-section");

            var reqHeader = new Label("User Request");
            reqHeader.AddToClassList("plan-section-label");
            requestSection.Add(reqHeader);

            _requestLabel = new Label("");
            _requestLabel.AddToClassList("plan-request-text");
            requestSection.Add(_requestLabel);

            _planBody.Add(requestSection);

            _phasesScroll = new ScrollView(ScrollViewMode.Vertical);
            _phasesScroll.AddToClassList("plan-phases-scroll");
            _planBody.Add(_phasesScroll);

            _container.Add(_planBody);

            // --- Execute bar ---
            _executeBar = new VisualElement();
            _executeBar.AddToClassList("plan-execute-bar");
            _executeBar.style.display = DisplayStyle.None;

            var progressTrack = new VisualElement();
            progressTrack.AddToClassList("plan-progress-track");

            _progressFill = new VisualElement();
            _progressFill.AddToClassList("plan-progress-fill");
            progressTrack.Add(_progressFill);
            _executeBar.Add(progressTrack);

            var execRow = new VisualElement();
            execRow.AddToClassList("plan-execute-row");

            _progressLabel = new Label("");
            _progressLabel.AddToClassList("plan-progress-label");
            execRow.Add(_progressLabel);

            var execSpacer = new VisualElement();
            execSpacer.style.flexGrow = 1;
            execRow.Add(execSpacer);

            _executeBtn = new Button { text = "Execute Plan" };
            _executeBtn.AddToClassList("action-btn");
            _executeBtn.AddToClassList("primary");
            _executeBtn.AddToClassList("plan-execute-btn");
            _executeBtn.clicked += () => OnExecutePlanRequested?.Invoke();
            execRow.Add(_executeBtn);

            _executeBar.Add(execRow);
            _container.Add(_executeBar);
        }

        public void Bind(ShaderDocument doc)
        {
            _document = doc;
            Refresh();
        }

        public void Refresh()
        {
            var plan = _document?.Plan;

            if (plan == null || plan.Status == PlanStatus.Empty)
            {
                ShowEmptyState();
                return;
            }

            ShowPlanState(plan);
        }

        void ShowEmptyState()
        {
            _emptyState.style.display = DisplayStyle.Flex;
            _planBody.style.display = DisplayStyle.None;
            _executeBar.style.display = DisplayStyle.None;
            _statusBadge.text = "Empty";
            _statusBadge.RemoveFromClassList("plan-status-badge--refining");
            _statusBadge.RemoveFromClassList("plan-status-badge--ready");
            _statusBadge.RemoveFromClassList("plan-status-badge--executing");
            _statusBadge.RemoveFromClassList("plan-status-badge--completed");
            _statusBadge.RemoveFromClassList("plan-status-badge--failed");
            _resetBtn.style.display = DisplayStyle.None;
            _newPlanBtn.style.display = DisplayStyle.None;
        }

        void ShowPlanState(ShaderPlan plan)
        {
            _emptyState.style.display = DisplayStyle.None;
            _planBody.style.display = DisplayStyle.Flex;
            _executeBar.style.display = DisplayStyle.Flex;
            _resetBtn.style.display = DisplayStyle.Flex;
            _newPlanBtn.style.display = DisplayStyle.Flex;

            _requestLabel.text = plan.UserRequest;
            UpdateStatusBadge(plan.Status);
            RebuildPhaseCards(plan);
            UpdateExecuteBar(plan);
        }

        void UpdateStatusBadge(PlanStatus status)
        {
            _statusBadge.RemoveFromClassList("plan-status-badge--refining");
            _statusBadge.RemoveFromClassList("plan-status-badge--ready");
            _statusBadge.RemoveFromClassList("plan-status-badge--executing");
            _statusBadge.RemoveFromClassList("plan-status-badge--completed");
            _statusBadge.RemoveFromClassList("plan-status-badge--failed");

            switch (status)
            {
                case PlanStatus.Decomposing:
                    _statusBadge.text = "Decomposing...";
                    _statusBadge.AddToClassList("plan-status-badge--refining");
                    break;
                case PlanStatus.Refining:
                    _statusBadge.text = "Refining";
                    _statusBadge.AddToClassList("plan-status-badge--refining");
                    break;
                case PlanStatus.Ready:
                    _statusBadge.text = "Ready";
                    _statusBadge.AddToClassList("plan-status-badge--ready");
                    break;
                case PlanStatus.Executing:
                    _statusBadge.text = "Executing...";
                    _statusBadge.AddToClassList("plan-status-badge--executing");
                    break;
                case PlanStatus.Completed:
                    _statusBadge.text = "Completed";
                    _statusBadge.AddToClassList("plan-status-badge--completed");
                    break;
                case PlanStatus.Failed:
                    _statusBadge.text = "Failed";
                    _statusBadge.AddToClassList("plan-status-badge--failed");
                    break;
                default:
                    _statusBadge.text = "Empty";
                    break;
            }
        }

        void RebuildPhaseCards(ShaderPlan plan)
        {
            _phasesScroll.Clear();
            _phaseCards.Clear();

            for (int i = 0; i < plan.Phases.Count; i++)
            {
                var phase = plan.Phases[i];
                var card = new PlanPhaseCard(phase, i + 1);
                card.OnConfirm += phaseId => OnPhaseConfirmed?.Invoke(phaseId);
                card.OnSkip += phaseId => OnPhaseSkipped?.Invoke(phaseId);
                card.OnSendFeedback += (phaseId, feedback) => OnPhaseFeedbackSent?.Invoke(phaseId, feedback);
                _phaseCards.Add(card);
                _phasesScroll.Add(card);
            }
        }

        void UpdateExecuteBar(ShaderPlan plan)
        {
            int total = plan.Phases.Count;
            int handled = plan.ConfirmedCount;
            float pct = total > 0 ? (float)handled / total : 0f;

            _progressFill.style.width = new Length(pct * 100f, LengthUnit.Percent);
            _progressLabel.text = $"{handled}/{total} phases confirmed";

            bool canExecute = plan.AllPhasesHandled
                           && plan.Status != PlanStatus.Executing
                           && plan.Status != PlanStatus.Completed;
            _executeBtn.SetEnabled(canExecute);

            if (plan.Status == PlanStatus.Executing)
            {
                _executeBtn.text = "Executing...";
                _executeBtn.SetEnabled(false);
            }
            else if (plan.Status == PlanStatus.Completed)
            {
                _executeBtn.text = "Completed";
                _executeBtn.SetEnabled(false);
            }
            else
            {
                _executeBtn.text = "Execute Plan";
            }
        }

        public void SetDecomposing()
        {
            _emptyState.style.display = DisplayStyle.None;
            _planBody.style.display = DisplayStyle.Flex;
            _executeBar.style.display = DisplayStyle.None;
            _requestLabel.text = _requestInput.value?.Trim() ?? "";
            _phasesScroll.Clear();
            _phaseCards.Clear();

            var loading = new Label("Analyzing your request and decomposing into phases...");
            loading.AddToClassList("plan-loading-label");
            _phasesScroll.Add(loading);

            UpdateStatusBadge(PlanStatus.Decomposing);
            _resetBtn.style.display = DisplayStyle.Flex;
            _newPlanBtn.style.display = DisplayStyle.None;
        }

        public void SetPhaseRefining(string phaseId)
        {
            foreach (var card in _phaseCards)
            {
                if (card.PhaseId == phaseId)
                    card.SetRefining();
            }
        }

        public void UpdatePhaseCard(string phaseId, PlanPhase updatedPhase)
        {
            foreach (var card in _phaseCards)
            {
                if (card.PhaseId == phaseId)
                {
                    card.UpdateContent(updatedPhase);
                    return;
                }
            }
        }

        public void SetExecutionProgress(PlanPhaseType phaseType, string message)
        {
            _progressLabel.text = message;
            foreach (var card in _phaseCards)
                card.UpdateExecutionState(phaseType);
        }
    }

    public class PlanPhaseCard : VisualElement
    {
        readonly PlanPhase _phase;
        readonly int _index;

        Label _titleLabel;
        Label _statusLabel;
        VisualElement _bodyContainer;
        Label _proposalLabel;
        VisualElement _itemsContainer;
        Label _questionLabel;
        VisualElement _questionSection;
        TextField _feedbackInput;
        string _cachedFeedbackText = "";
        Button _confirmBtn;
        Button _refineBtn;
        Button _skipBtn;
        VisualElement _userInputSection;
        Label _footerLabel;
        Button _collapseToggle;
        bool _collapsed;

        public string PhaseId => _phase.Id;

        public event Action<string> OnConfirm;
        public event Action<string> OnSkip;
        public event Action<string, string> OnSendFeedback;

        public PlanPhaseCard(PlanPhase phase, int index)
        {
            _phase = phase;
            _index = index;
            BuildCard();
        }

        void BuildCard()
        {
            AddToClassList("phase-card");
            UpdateStatusClass(_phase.Status);

            // --- Header ---
            var header = new VisualElement();
            header.AddToClassList("phase-card__header");

            var iconLabel = new Label(ShaderPlan.GetPhaseTypeIcon(_phase.Type));
            iconLabel.AddToClassList("phase-card__icon");
            header.Add(iconLabel);

            _titleLabel = new Label($"Phase {_index}: {_phase.Title}");
            _titleLabel.AddToClassList("phase-card__title");
            header.Add(_titleLabel);

            var headerSpacer = new VisualElement();
            headerSpacer.style.flexGrow = 1;
            header.Add(headerSpacer);

            _statusLabel = new Label(GetStatusText(_phase.Status));
            _statusLabel.AddToClassList("phase-card__status-label");
            UpdateStatusLabelClass(_phase.Status);
            header.Add(_statusLabel);

            _collapseToggle = new Button { text = "\u25bc" };
            _collapseToggle.AddToClassList("phase-card__collapse-btn");
            _collapseToggle.clicked += ToggleCollapse;
            header.Add(_collapseToggle);

            Add(header);

            // --- Body ---
            _bodyContainer = new VisualElement();
            _bodyContainer.AddToClassList("phase-card__body");

            // Proposal section
            _proposalLabel = new Label(_phase.LLMProposal ?? "");
            _proposalLabel.AddToClassList("phase-card__proposal");
            _proposalLabel.enableRichText = true;
            _bodyContainer.Add(_proposalLabel);

            // Items section
            _itemsContainer = new VisualElement();
            _itemsContainer.AddToClassList("phase-card__items");
            RebuildItems();
            _bodyContainer.Add(_itemsContainer);

            // Question section
            _questionSection = new VisualElement();
            _questionSection.AddToClassList("phase-card__question-section");
            _questionLabel = new Label("");
            _questionLabel.AddToClassList("phase-card__question");
            _questionLabel.enableRichText = true;
            _questionSection.Add(_questionLabel);

            if (!string.IsNullOrEmpty(_phase.LLMQuestion))
            {
                _questionLabel.text = $"<b>AI asks:</b> {_phase.LLMQuestion}";
                _questionSection.style.display = DisplayStyle.Flex;
            }
            else
            {
                _questionSection.style.display = DisplayStyle.None;
            }
            _bodyContainer.Add(_questionSection);

            // User input section
            _userInputSection = new VisualElement();
            _userInputSection.AddToClassList("phase-card__user-input");

            _feedbackInput = new TextField();
            _feedbackInput.multiline = true;
            _feedbackInput.isDelayed = false;
            _feedbackInput.AddToClassList("phase-card__feedback-input");
            _cachedFeedbackText = _phase.UserResponse ?? "";
            _feedbackInput.value = _cachedFeedbackText;
            _feedbackInput.RegisterValueChangedCallback(evt =>
            {
                _cachedFeedbackText = evt.newValue ?? "";
            });
            _userInputSection.Add(_feedbackInput);

            var feedbackHint = new Label("");
            feedbackHint.AddToClassList("phase-card__feedback-hint");
            feedbackHint.style.display = DisplayStyle.None;
            _userInputSection.Add(feedbackHint);

            var actionRow = new VisualElement();
            actionRow.AddToClassList("phase-card__actions");

            _confirmBtn = new Button { text = "Confirm" };
            _confirmBtn.AddToClassList("phase-card__action-btn");
            _confirmBtn.AddToClassList("phase-card__action-btn--confirm");
            _confirmBtn.clicked += () => OnConfirm?.Invoke(_phase.Id);
            actionRow.Add(_confirmBtn);

            var capturedHint = feedbackHint;
            _refineBtn = new Button { text = "Send Feedback" };
            _refineBtn.AddToClassList("phase-card__action-btn");
            _refineBtn.AddToClassList("phase-card__action-btn--refine");
            _refineBtn.clicked += () =>
            {
                string feedback = _cachedFeedbackText?.Trim();
                if (string.IsNullOrEmpty(feedback))
                    feedback = _feedbackInput.value?.Trim();
                if (string.IsNullOrEmpty(feedback))
                {
                    capturedHint.text = "Please enter feedback before sending.";
                    capturedHint.style.display = DisplayStyle.Flex;
                    capturedHint.style.color = new StyleColor(new UnityEngine.Color(0.9f, 0.5f, 0.3f));
                    return;
                }
                capturedHint.text = "Sending feedback...";
                capturedHint.style.display = DisplayStyle.Flex;
                capturedHint.style.color = new StyleColor(new UnityEngine.Color(0.6f, 0.8f, 0.6f));
                OnSendFeedback?.Invoke(_phase.Id, feedback);
            };
            actionRow.Add(_refineBtn);

            _skipBtn = new Button { text = "Skip" };
            _skipBtn.AddToClassList("phase-card__action-btn");
            _skipBtn.AddToClassList("phase-card__action-btn--skip");
            _skipBtn.clicked += () => OnSkip?.Invoke(_phase.Id);
            actionRow.Add(_skipBtn);

            _userInputSection.Add(actionRow);
            _bodyContainer.Add(_userInputSection);

            // Footer (execution result)
            _footerLabel = new Label("");
            _footerLabel.AddToClassList("phase-card__footer");
            _footerLabel.style.display = DisplayStyle.None;
            _bodyContainer.Add(_footerLabel);

            Add(_bodyContainer);

            UpdateInteractivity(_phase.Status);
        }

        void RebuildItems()
        {
            _itemsContainer.Clear();
            if (_phase.Items == null || _phase.Items.Count == 0)
            {
                _itemsContainer.style.display = DisplayStyle.None;
                return;
            }

            _itemsContainer.style.display = DisplayStyle.Flex;

            if (_phase.Type == PlanPhaseType.Textures)
            {
                var hint = new Label(
                    "Textures will be added as shader properties. " +
                    "You can assign them in the Material Inspector after execution. " +
                    "AI image generation capability may be limited or unsupported by your provider.");
                hint.AddToClassList("phase-card__texture-hint");
                _itemsContainer.Add(hint);
            }

            string currentCategory = null;

            foreach (var item in _phase.Items)
            {
                if (!string.IsNullOrEmpty(item.Category) && item.Category != currentCategory)
                {
                    currentCategory = item.Category;
                    var catLabel = new Label(currentCategory);
                    catLabel.AddToClassList("phase-card__item-category");
                    _itemsContainer.Add(catLabel);
                }

                var itemRow = new VisualElement();
                itemRow.AddToClassList("phase-card__item-row");

                var checkbox = new Toggle();
                checkbox.value = item.IsConfirmed;
                checkbox.AddToClassList("phase-card__item-check");
                var capturedItem = item;
                checkbox.RegisterValueChangedCallback(evt => capturedItem.IsConfirmed = evt.newValue);
                itemRow.Add(checkbox);

                var keyLabel = new Label(item.Key);
                keyLabel.AddToClassList("phase-card__item-key");
                itemRow.Add(keyLabel);

                var descLabel = new Label(item.Description);
                descLabel.AddToClassList("phase-card__item-desc");
                itemRow.Add(descLabel);

                if (!string.IsNullOrEmpty(item.Detail))
                {
                    var detailLabel = new Label(item.Detail);
                    detailLabel.AddToClassList("phase-card__item-detail");
                    itemRow.Add(detailLabel);
                }

                _itemsContainer.Add(itemRow);
            }
        }

        void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            _bodyContainer.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _collapseToggle.text = _collapsed ? "\u25b6" : "\u25bc";
        }

        void UpdateStatusClass(PhaseStatus status)
        {
            RemoveFromClassList("phase-card--pending");
            RemoveFromClassList("phase-card--waiting");
            RemoveFromClassList("phase-card--confirmed");
            RemoveFromClassList("phase-card--executing");
            RemoveFromClassList("phase-card--done");
            RemoveFromClassList("phase-card--skipped");

            switch (status)
            {
                case PhaseStatus.Pending:        AddToClassList("phase-card--pending"); break;
                case PhaseStatus.WaitingForUser:  AddToClassList("phase-card--waiting"); break;
                case PhaseStatus.Confirmed:       AddToClassList("phase-card--confirmed"); break;
                case PhaseStatus.Executing:       AddToClassList("phase-card--executing"); break;
                case PhaseStatus.Done:            AddToClassList("phase-card--done"); break;
                case PhaseStatus.Skipped:         AddToClassList("phase-card--skipped"); break;
            }
        }

        void UpdateStatusLabelClass(PhaseStatus status)
        {
            _statusLabel.RemoveFromClassList("phase-card__status-label--pending");
            _statusLabel.RemoveFromClassList("phase-card__status-label--waiting");
            _statusLabel.RemoveFromClassList("phase-card__status-label--confirmed");
            _statusLabel.RemoveFromClassList("phase-card__status-label--done");
            _statusLabel.RemoveFromClassList("phase-card__status-label--skipped");

            switch (status)
            {
                case PhaseStatus.Pending:        _statusLabel.AddToClassList("phase-card__status-label--pending"); break;
                case PhaseStatus.WaitingForUser:  _statusLabel.AddToClassList("phase-card__status-label--waiting"); break;
                case PhaseStatus.Confirmed:       _statusLabel.AddToClassList("phase-card__status-label--confirmed"); break;
                case PhaseStatus.Done:            _statusLabel.AddToClassList("phase-card__status-label--done"); break;
                case PhaseStatus.Skipped:         _statusLabel.AddToClassList("phase-card__status-label--skipped"); break;
            }
        }

        void UpdateInteractivity(PhaseStatus status)
        {
            bool canInteract = status == PhaseStatus.WaitingForUser || status == PhaseStatus.Pending;
            _feedbackInput.SetEnabled(canInteract);
            _confirmBtn.SetEnabled(canInteract);
            _refineBtn.SetEnabled(canInteract);
            _skipBtn.SetEnabled(canInteract);

            if (status == PhaseStatus.Confirmed || status == PhaseStatus.Done || status == PhaseStatus.Skipped)
                _userInputSection.style.display = DisplayStyle.None;
            else
                _userInputSection.style.display = DisplayStyle.Flex;

            if (status == PhaseStatus.Done)
                _footerLabel.style.display = DisplayStyle.Flex;
        }

        static string GetStatusText(PhaseStatus status)
        {
            switch (status)
            {
                case PhaseStatus.Pending:        return "Pending";
                case PhaseStatus.WaitingForUser:  return "Waiting for Input";
                case PhaseStatus.Confirmed:       return "Confirmed";
                case PhaseStatus.Executing:       return "Executing...";
                case PhaseStatus.Done:            return "Done";
                case PhaseStatus.Skipped:         return "Skipped";
                default:                          return status.ToString();
            }
        }

        public void SetRefining()
        {
            _statusLabel.text = "Refining...";
            _confirmBtn.SetEnabled(false);
            _refineBtn.SetEnabled(false);
            _skipBtn.SetEnabled(false);
        }

        public void UpdateContent(PlanPhase updated)
        {
            _proposalLabel.text = updated.LLMProposal ?? "";

            if (!string.IsNullOrEmpty(updated.LLMQuestion))
            {
                _questionLabel.text = $"<b>AI asks:</b> {updated.LLMQuestion}";
                _questionSection.style.display = DisplayStyle.Flex;
            }
            else
            {
                _questionSection.style.display = DisplayStyle.None;
            }

            _phase.LLMProposal = updated.LLMProposal;
            _phase.LLMQuestion = updated.LLMQuestion;
            _phase.Items = updated.Items;
            _phase.Status = updated.Status;
            _phase.RefinementCount = updated.RefinementCount;

            _feedbackInput.value = "";
            _cachedFeedbackText = "";

            RebuildItems();
            _statusLabel.text = GetStatusText(updated.Status);
            UpdateStatusClass(updated.Status);
            UpdateStatusLabelClass(updated.Status);
            UpdateInteractivity(updated.Status);
        }

        public void UpdateExecutionState(PlanPhaseType executingType)
        {
            if (_phase.Type == executingType)
            {
                _phase.Status = PhaseStatus.Executing;
                _statusLabel.text = "Executing...";
                UpdateStatusClass(PhaseStatus.Executing);
            }
            else if (_phase.Status == PhaseStatus.Executing)
            {
                _phase.Status = PhaseStatus.Done;
                _statusLabel.text = "Done";
                UpdateStatusClass(PhaseStatus.Done);
                _footerLabel.text = "Phase executed successfully.";
                _footerLabel.style.display = DisplayStyle.Flex;
            }
        }
    }
}
