using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    public class ShaderAILabWindow : EditorWindow
    {
        enum EditorTab { NLBlocks, DataFlow, Plan, History }

        ShaderDocument _document;
        ShaderVersionHistory _versionHistory;
        string _selectedBlockId;
        EditorTab _activeTab = EditorTab.NLBlocks;

        // UI references
        Label _shaderNameLabel;
        VisualElement _blockListContainer;
        TextField _codeEditorField;
        Label _codeEditorHeader;
        VisualElement _parameterContainer;
        TextField _promptInput;
        DropdownField _promptTarget;
        Label _promptStatus;
        IMGUIContainer _previewContainer;
        VisualElement _popupLayer;

        // Tab bar
        Button _tabNLBlocks;
        Button _tabDataFlow;
        Button _tabPlan;
        Button _tabHistory;
        VisualElement _mainContent;
        VisualElement _dataFlowContent;
        VisualElement _planContent;
        VisualElement _historyContent;

        // Pass bar
        VisualElement _passTabContainer;
        Button _btnAddPass;

        // Sub-views
        BlockListView _blockListView;
        CodeEditorView _codeEditorView;
        ParameterPanelView _parameterPanelView;
        ShaderPreviewView _previewView;
        DataFlowGraphView _dataFlowGraphView;
        HistoryView _historyView;
        PlanView _planView;
        AutoCompletePopup _autoComplete;
        InlineLLMPopup _inlineLLMPopup;

        [MenuItem("ShaderAILab/Open Editor %#l")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<ShaderAILabWindow>();
            wnd.titleContent = new GUIContent("Shader AILab");
            wnd.minSize = new Vector2(900, 500);
        }

        public static void OpenShader(string shaderPath)
        {
            var wnd = GetWindow<ShaderAILabWindow>();
            wnd.titleContent = new GUIContent("Shader AILab");
            wnd.minSize = new Vector2(900, 500);
            wnd.LoadShader(shaderPath);
        }

        void CreateGUI()
        {
            var uxmlAsset = Resources.Load<VisualTreeAsset>("ShaderAILabWindow");
            if (uxmlAsset == null)
            {
                rootVisualElement.Add(new Label("Error: Could not load ShaderAILabWindow.uxml"));
                return;
            }

            uxmlAsset.CloneTree(rootVisualElement);

            var ussAsset = Resources.Load<StyleSheet>("ShaderAILab");
            if (ussAsset != null)
                rootVisualElement.styleSheets.Add(ussAsset);

            BindUI();
            WireEvents();
            SetEmptyState();
        }

        void BindUI()
        {
            _shaderNameLabel = rootVisualElement.Q<Label>("shaderNameLabel");
            _blockListContainer = rootVisualElement.Q<VisualElement>("blockListContainer");
            _codeEditorField = rootVisualElement.Q<TextField>("codeEditor");
            _codeEditorHeader = rootVisualElement.Q<Label>("codeEditorHeader");
            _parameterContainer = rootVisualElement.Q<VisualElement>("parameterContainer");
            _promptInput = rootVisualElement.Q<TextField>("promptInput");
            _promptTarget = rootVisualElement.Q<DropdownField>("promptTarget");
            _promptStatus = rootVisualElement.Q<Label>("promptStatus");
            _previewContainer = rootVisualElement.Q<IMGUIContainer>("previewContainer");
            _popupLayer = rootVisualElement.Q<VisualElement>("popupLayer");
            if (_popupLayer != null)
                _popupLayer.pickingMode = PickingMode.Ignore;

            // Tab bar
            _tabNLBlocks = rootVisualElement.Q<Button>("tabNLBlocks");
            _tabDataFlow = rootVisualElement.Q<Button>("tabDataFlow");
            _tabPlan = rootVisualElement.Q<Button>("tabPlan");
            _tabHistory = rootVisualElement.Q<Button>("tabHistory");
            _mainContent = rootVisualElement.Q<VisualElement>("mainContent");
            _dataFlowContent = rootVisualElement.Q<VisualElement>("dataFlowContent");
            _planContent = rootVisualElement.Q<VisualElement>("planContent");
            _historyContent = rootVisualElement.Q<VisualElement>("historyContent");

            // History view
            if (_historyContent != null)
            {
                _historyView = new HistoryView(_historyContent);
                _historyView.OnResendRequested += OnHistoryResend;
            }

            // Plan view
            if (_planContent != null)
            {
                _planView = new PlanView(_planContent);
                _planView.OnCreatePlanRequested += OnCreatePlanRequested;
                _planView.OnPhaseConfirmed += OnPlanPhaseConfirmed;
                _planView.OnPhaseSkipped += OnPlanPhaseSkipped;
                _planView.OnPhaseFeedbackSent += OnPlanPhaseFeedback;
                _planView.OnExecutePlanRequested += OnExecutePlanRequested;
                _planView.OnResetPlanRequested += OnResetPlanRequested;
            }

            // Pass bar
            _passTabContainer = rootVisualElement.Q<VisualElement>("passTabContainer");
            _btnAddPass = rootVisualElement.Q<Button>("btnAddPass");

            // Code editor with syntax highlighting
            var codeScrollView = rootVisualElement.Q<ScrollView>("codeEditorContainer");
            if (codeScrollView != null && _codeEditorField != null)
            {
                _codeEditorView = new CodeEditorView(codeScrollView, _codeEditorField);
                _codeEditorView.OnInlineLLMRequested += OnInlineLLMRequested;
                _codeEditorView.OnContextMenu += OnCodeEditorContextMenu;
            }

            // Autocomplete popup
            if (_popupLayer != null)
            {
                _autoComplete = new AutoCompletePopup();
                _autoComplete.OnItemSelected += OnAutoCompleteSelected;
                _popupLayer.Add(_autoComplete);
            }

            _blockListView = new BlockListView(_blockListContainer);
            _blockListView.OnBlockSelected += OnBlockSelected;
            _blockListView.OnBlockDeleteRequested += OnBlockDeleteRequested;
            _blockListView.OnBlockToggleEnabled += OnBlockToggleEnabled;

            _parameterPanelView = new ParameterPanelView(_parameterContainer);
            _parameterPanelView.OnParameterChanged += OnParameterChanged;

            if (_previewContainer != null)
            {
                _previewView = new ShaderPreviewView();
                _previewContainer.onGUIHandler = _previewView.OnPreviewGUI;
            }
        }

        void WireEvents()
        {
            _tabNLBlocks?.RegisterCallback<ClickEvent>(_ => SwitchTab(EditorTab.NLBlocks));
            _tabDataFlow?.RegisterCallback<ClickEvent>(_ => SwitchTab(EditorTab.DataFlow));
            _tabPlan?.RegisterCallback<ClickEvent>(_ => SwitchTab(EditorTab.Plan));
            _tabHistory?.RegisterCallback<ClickEvent>(_ => SwitchTab(EditorTab.History));
            rootVisualElement.Q<Button>("btnOpen")?.RegisterCallback<ClickEvent>(_ => OnOpenShader());
            rootVisualElement.Q<Button>("btnSave")?.RegisterCallback<ClickEvent>(_ => OnSave());
            rootVisualElement.Q<Button>("btnNewFromTemplate")?.RegisterCallback<ClickEvent>(_ => OnNewFromTemplate());
            rootVisualElement.Q<Button>("btnOpenInVSCode")?.RegisterCallback<ClickEvent>(_ => OnOpenInVSCode());
            rootVisualElement.Q<Button>("btnSettings")?.RegisterCallback<ClickEvent>(_ => OnOpenSettings());
            rootVisualElement.Q<Button>("btnAddBlock")?.RegisterCallback<ClickEvent>(_ => OnAddBlock());
            rootVisualElement.Q<Button>("btnCompile")?.RegisterCallback<ClickEvent>(_ => OnCompile());
            rootVisualElement.Q<Button>("btnApplyCode")?.RegisterCallback<ClickEvent>(_ => OnApplyCode());
            rootVisualElement.Q<Button>("btnRevertCode")?.RegisterCallback<ClickEvent>(_ => OnRevertCode());
            rootVisualElement.Q<Button>("btnSendPrompt")?.RegisterCallback<ClickEvent>(_ => OnSendPrompt());
            _btnAddPass?.RegisterCallback<ClickEvent>(_ => OnAddPass());

            if (_promptInput != null)
            {
                bool placeholderActive = true;
                _promptInput.RegisterCallback<FocusInEvent>(_ =>
                {
                    if (placeholderActive)
                    {
                        _promptInput.value = "";
                        placeholderActive = false;
                    }
                });
            }
        }

        void SetEmptyState()
        {
            if (_codeEditorView != null)
                _codeEditorView.Code = "// Open or create a shader to begin.";
            else if (_codeEditorField != null)
                _codeEditorField.value = "// Open or create a shader to begin.";
            if (_shaderNameLabel != null)
                _shaderNameLabel.text = "Shader AILab - No shader loaded";
        }

        // ---- Tab switching ----

        void SwitchTab(EditorTab tab)
        {
            if (_activeTab == tab) return;
            _activeTab = tab;

            _mainContent.style.display = tab == EditorTab.NLBlocks ? DisplayStyle.Flex : DisplayStyle.None;
            _dataFlowContent.style.display = tab == EditorTab.DataFlow ? DisplayStyle.Flex : DisplayStyle.None;
            if (_planContent != null)
                _planContent.style.display = tab == EditorTab.Plan ? DisplayStyle.Flex : DisplayStyle.None;
            if (_historyContent != null)
                _historyContent.style.display = tab == EditorTab.History ? DisplayStyle.Flex : DisplayStyle.None;

            _tabNLBlocks.EnableInClassList("tab-btn--active", tab == EditorTab.NLBlocks);
            _tabDataFlow.EnableInClassList("tab-btn--active", tab == EditorTab.DataFlow);
            _tabPlan?.EnableInClassList("tab-btn--active", tab == EditorTab.Plan);
            _tabHistory?.EnableInClassList("tab-btn--active", tab == EditorTab.History);

            switch (tab)
            {
                case EditorTab.NLBlocks:
                    if (!string.IsNullOrEmpty(_selectedBlockId) && _document != null)
                        OnBlockSelected(_selectedBlockId);
                    break;
                case EditorTab.DataFlow:
                    EnsureDataFlowView();
                    break;
                case EditorTab.Plan:
                    _planView?.Refresh();
                    break;
                case EditorTab.History:
                    _historyView?.Refresh();
                    break;
            }
        }

        void EnsureDataFlowView()
        {
            if (_dataFlowGraphView == null && _dataFlowContent != null)
            {
                _dataFlowGraphView = new DataFlowGraphView();
                _dataFlowGraphView.OnGraphChanged += OnDataFlowChanged;
                _dataFlowContent.Add(_dataFlowGraphView);
            }

            if (_document?.ActivePass != null)
                _dataFlowGraphView?.Rebuild(_document.ActivePass.DataFlow, _document.ActivePass, _document.GlobalSettings);
        }

        void OnDataFlowChanged()
        {
            if (_document != null)
                _document.IsDirty = true;
        }

        // ---- Pass bar ----

        void RebuildPassBar()
        {
            if (_passTabContainer == null || _document == null) return;
            _passTabContainer.Clear();

            for (int i = 0; i < _document.Passes.Count; i++)
            {
                var pass = _document.Passes[i];
                int passIndex = i;

                var btn = new Button(() => OnPassSelected(passIndex));
                btn.text = pass.IsUsePass ? $"[{pass.Name}]" : pass.Name;
                btn.AddToClassList("pass-tab");

                if (pass.IsUsePass)
                    btn.AddToClassList("pass-tab--usepass");
                if (!pass.IsEnabled)
                    btn.AddToClassList("pass-tab--disabled");
                if (i == _document.ActivePassIndex)
                    btn.AddToClassList("pass-tab--active");

                btn.RegisterCallback<ContextClickEvent>(evt =>
                {
                    ShowPassContextMenu(passIndex);
                    evt.StopPropagation();
                });

                _passTabContainer.Add(btn);
            }
        }

        void OnPassSelected(int passIndex)
        {
            if (_document == null) return;
            if (passIndex < 0 || passIndex >= _document.Passes.Count) return;
            if (passIndex == _document.ActivePassIndex) return;

            _document.SetActivePass(passIndex);
            _selectedBlockId = null;
            RefreshActivePassViews();
        }

        void RefreshActivePassViews()
        {
            if (_document == null) return;

            RebuildPassBar();

            var pass = _document.ActivePass;
            if (pass == null) return;

            _blockListView.Rebuild(pass);
            _dataFlowGraphView?.Rebuild(pass.DataFlow, pass, _document?.GlobalSettings);
            RefreshPromptTargets();

            if (pass.IsUsePass)
            {
                if (_codeEditorView != null)
                    _codeEditorView.Code = $"// UsePass \"{pass.UsePassPath}\"\n// This pass references a built-in pass and cannot be edited.";
                _codeEditorHeader.text = $"Pass: {pass.Name} (UsePass)";
            }
            else if (pass.Blocks.Count > 0)
            {
                OnBlockSelected(pass.Blocks[0].Id);
            }
            else
            {
                if (_codeEditorView != null)
                    _codeEditorView.Code = "// No blocks in this pass. Click '+ Add Block' to create one.";
                _codeEditorHeader.text = $"Pass: {pass.Name}";
            }
        }

        void OnAddPass()
        {
            if (_document == null) return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Forward Lit Pass"), false, () => AddPassFromFactory(ShaderPass.CreateForwardLit()));
            menu.AddItem(new GUIContent("Unlit Pass"), false, () => AddPassFromFactory(ShaderPass.CreateUnlit()));
            menu.AddItem(new GUIContent("Outline Pass"), false, () => AddPassFromFactory(ShaderPass.CreateOutline()));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("UsePass: ShadowCaster"), false, () => AddPassFromFactory(ShaderPass.CreateShadowCaster()));
            menu.AddItem(new GUIContent("UsePass: DepthOnly"), false, () => AddPassFromFactory(ShaderPass.CreateDepthOnly()));
            menu.ShowAsContext();
        }

        void AddPassFromFactory(ShaderPass pass)
        {
            _document.AddPass(pass);
            _document.SetActivePass(_document.Passes.Count - 1);
            RefreshActivePassViews();
            _promptStatus.text = $"Added pass: {pass.Name}";
        }

        void ShowPassContextMenu(int passIndex)
        {
            if (_document == null || passIndex < 0 || passIndex >= _document.Passes.Count) return;
            var pass = _document.Passes[passIndex];

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename..."), false, () => RenamePass(passIndex));

            if (_document.Passes.Count > 1)
                menu.AddItem(new GUIContent("Delete"), false, () => DeletePass(passIndex));
            else
                menu.AddDisabledItem(new GUIContent("Delete"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enabled"), pass.IsEnabled, () => TogglePassEnabled(passIndex));

            if (passIndex > 0)
                menu.AddItem(new GUIContent("Move Up"), false, () => MovePass(passIndex, passIndex - 1));
            if (passIndex < _document.Passes.Count - 1)
                menu.AddItem(new GUIContent("Move Down"), false, () => MovePass(passIndex, passIndex + 1));

            menu.ShowAsContext();
        }

        void RenamePass(int passIndex)
        {
            var pass = _document.Passes[passIndex];
            string newName = pass.Name;
            if (DialogRenameField(ref newName))
            {
                pass.Name = newName;
                _document.IsDirty = true;
                RebuildPassBar();
            }
        }

        static bool DialogRenameField(ref string name)
        {
            string result = name;
            bool ok = false;
            result = EditorInputDialog.Show("Rename Pass", "Enter new pass name:", name);
            if (!string.IsNullOrEmpty(result) && result != name)
            {
                name = result;
                ok = true;
            }
            return ok;
        }

        void DeletePass(int passIndex)
        {
            var pass = _document.Passes[passIndex];
            if (EditorUtility.DisplayDialog("Delete Pass",
                    $"Delete pass \"{pass.Name}\"?", "Delete", "Cancel"))
            {
                _document.RemovePass(pass.Id);
                _selectedBlockId = null;
                RefreshActivePassViews();
                _parameterPanelView.Rebuild(_document);
                _promptStatus.text = $"Deleted pass: {pass.Name}";
            }
        }

        void TogglePassEnabled(int passIndex)
        {
            var pass = _document.Passes[passIndex];
            pass.IsEnabled = !pass.IsEnabled;
            _document.IsDirty = true;
            RebuildPassBar();
        }

        void MovePass(int fromIndex, int toIndex)
        {
            _document.MovePass(fromIndex, toIndex);
            RebuildPassBar();
        }

        // ---- File watcher integration ----

        void OnEnable()
        {
            ShaderFileWatcher.Instance.OnFileChanged += OnExternalFileChanged;
            ShaderFileWatcher.Instance.OnTagsDamaged += OnTagsDamaged;
        }

        void OnDisable()
        {
            ShaderFileWatcher.Instance.OnFileChanged -= OnExternalFileChanged;
            ShaderFileWatcher.Instance.OnTagsDamaged -= OnTagsDamaged;
            ShaderFileWatcher.Instance.Stop();
            _previewView?.Dispose();
        }

        void OnExternalFileChanged(string path)
        {
            if (_document == null || _document.FilePath != path) return;

            if (_document.IsDirty)
            {
                bool reload = EditorUtility.DisplayDialog("External Modification",
                    "The shader file was modified externally. Reload and lose unsaved changes?",
                    "Reload", "Keep Mine");
                if (!reload) return;
            }

            LoadShader(path);
        }

        void OnTagsDamaged(string path)
        {
            Debug.LogWarning($"[ShaderAILab] AILab metadata tags may be damaged in: {path}");
        }

        // ---- Load / Save ----

        public void LoadShader(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[ShaderAILab] File not found: {path}");
                return;
            }

            _document = ShaderParser.ParseFile(path);
            _versionHistory = new ShaderVersionHistory(path);
            ShaderFileWatcher.Instance.Watch(path);

            var savedPlan = ShaderPlan.LoadFromFile(path);
            if (savedPlan != null)
            {
                _document.Plan = savedPlan;
                savedPlan.OnPlanChanged += () => SavePlanToDisk();
            }

            RefreshAll();
            CheckCompileErrors();
            _promptStatus.text = $"Loaded: {path}";
        }

        void SavePlanToDisk()
        {
            if (_document?.Plan == null || string.IsNullOrEmpty(_document.FilePath)) return;
            _document.Plan.SaveToFile(_document.FilePath);
        }

        void RefreshAll()
        {
            if (_document == null) return;

            _shaderNameLabel.text = $"Shader AILab - {_document.ShaderName}";

            RebuildPassBar();

            var pass = _document.ActivePass;
            if (pass != null)
                _blockListView.Rebuild(pass);

            _parameterPanelView.Rebuild(_document);
            _previewView?.SetShader(_document);

            if (pass != null)
                _dataFlowGraphView?.Rebuild(pass.DataFlow, pass, _document.GlobalSettings);

            _autoComplete?.SetCompletionSource(_document);
            _historyView?.Bind(_document.History);
            _planView?.Bind(_document);
            RefreshPromptTargets();

            if (pass != null && !pass.IsUsePass && pass.Blocks.Count > 0)
                OnBlockSelected(pass.Blocks[0].Id);
        }

        void RefreshPromptTargets()
        {
            if (_promptTarget == null || _document == null) return;

            var choices = new System.Collections.Generic.List<string> { "Global (auto-place)" };
            var pass = _document.ActivePass;
            if (pass != null && !pass.IsUsePass)
            {
                foreach (var b in pass.Blocks)
                    choices.Add($"Block: {b.Title}");
            }
            choices.Add("New Pass");
            choices.Add("New Vertex Block");
            choices.Add("New Fragment Block");
            choices.Add("New Helper Function");
            choices.Add("Data Flow");

            _promptTarget.choices = choices;
            _promptTarget.index = 0;
        }

        // ---- Block selection ----

        void OnBlockSelected(string blockId)
        {
            _selectedBlockId = blockId;
            var block = _document?.FindBlockById(blockId);
            if (block == null) return;

            _codeEditorHeader.text = $"Code: {block.Title}";
            if (_codeEditorView != null)
                _codeEditorView.Code = block.Code;
            else
                _codeEditorField.SetValueWithoutNotify(block.Code);
            _blockListView.SetSelected(blockId);
            _parameterPanelView.RebuildForBlock(_document, block);
        }

        void OnBlockDeleteRequested(string blockId)
        {
            if (_document == null) return;
            var block = _document.FindBlockById(blockId);
            if (block == null) return;

            if (EditorUtility.DisplayDialog("Delete Block",
                    $"Delete block \"{block.Title}\"?", "Delete", "Cancel"))
            {
                _document.RemoveBlock(blockId);
                RefreshActivePassViews();
                _parameterPanelView.Rebuild(_document);
            }
        }

        // ---- Toolbar actions ----

        void OnOpenShader()
        {
            string path = EditorUtility.OpenFilePanel("Open Shader", "Assets", "shader");
            if (!string.IsNullOrEmpty(path))
                LoadShader(path);
        }

        void OnSave()
        {
            if (_document == null) return;

            if (!string.IsNullOrEmpty(_selectedBlockId))
            {
                string code = _codeEditorView != null ? _codeEditorView.Code : _codeEditorField.value;
                _document.UpdateBlockCode(_selectedBlockId, code);
            }

            if (string.IsNullOrEmpty(_document.FilePath))
            {
                string path = EditorUtility.SaveFilePanel("Save Shader", "Assets", "NewShader", "shader");
                if (string.IsNullOrEmpty(path)) return;
                _document.FilePath = path;
            }

            _versionHistory?.RecordSnapshot("Before save", _document.RawContent);
            ShaderWriter.WriteToFile(_document);
            ShaderFileWatcher.Instance.AcknowledgeWrite();
            AssetDatabase.Refresh();
            CheckCompileErrors();
            _promptStatus.text = $"Saved: {_document.FilePath}";
        }

        void OnNewFromTemplate()
        {
            string templateDir = Path.Combine(Application.dataPath, "ShaderAILab", "Templates");
            string[] templates = Directory.Exists(templateDir)
                ? Directory.GetFiles(templateDir, "*.txt")
                : new string[0];

            if (templates.Length == 0)
            {
                EditorUtility.DisplayDialog("No Templates", "No template files found in ShaderAILab/Templates.", "OK");
                return;
            }

            var menu = new GenericMenu();
            foreach (string t in templates)
            {
                string name = Path.GetFileNameWithoutExtension(t);
                string templatePath = t;
                menu.AddItem(new GUIContent(name), false, () => CreateFromTemplate(templatePath));
            }
            menu.ShowAsContext();
        }

        void CreateFromTemplate(string templatePath)
        {
            string savePath = EditorUtility.SaveFilePanel("Save New Shader", "Assets", "NewShader", "shader");
            if (string.IsNullOrEmpty(savePath)) return;

            string content = File.ReadAllText(templatePath);
            string shaderName = "AILab/" + Path.GetFileNameWithoutExtension(savePath);
            content = content.Replace("AILab/Template", shaderName);

            File.WriteAllText(savePath, content);
            AssetDatabase.Refresh();
            LoadShader(savePath);
        }

        void OnOpenInVSCode()
        {
            if (_document == null || string.IsNullOrEmpty(_document.FilePath)) return;

            string filePath = _document.FilePath;

#if UNITY_EDITOR_OSX
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-l -c 'code \"{filePath}\"'",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                System.Diagnostics.Process.Start(psi);
            }
            catch (System.Exception e)
            {
                // Fallback: open with the macOS 'open' command
                try { System.Diagnostics.Process.Start("open", $"-a \"Visual Studio Code\" \"{filePath}\""); }
                catch { Debug.LogError($"[ShaderAILab] Failed to open VSCode: {e.Message}"); }
            }
#elif UNITY_EDITOR_WIN
            try
            {
                System.Diagnostics.Process.Start("code", $"\"{filePath}\"");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ShaderAILab] Failed to open VSCode: {e.Message}");
            }
#else
            try
            {
                System.Diagnostics.Process.Start("code", $"\"{filePath}\"");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ShaderAILab] Failed to open VSCode: {e.Message}");
            }
#endif
        }

        void OnOpenSettings()
        {
            LLM.LLMSettingsWindow.ShowWindow();
        }

        // ---- Compile ----

        void OnCompile()
        {
            if (_document == null) return;

            if (!string.IsNullOrEmpty(_selectedBlockId))
            {
                string code = _codeEditorView != null ? _codeEditorView.Code : _codeEditorField.value;
                _document.UpdateBlockCode(_selectedBlockId, code);
            }

            if (string.IsNullOrEmpty(_document.FilePath))
            {
                _promptStatus.text = "Save the shader first before compiling.";
                return;
            }

            _versionHistory?.RecordSnapshot("Before compile", _document.RawContent);
            ShaderWriter.WriteToFile(_document);
            ShaderFileWatcher.Instance.AcknowledgeWrite();
            AssetDatabase.Refresh();
            CheckCompileErrors();

            var errors = ShaderCompileChecker.Check(_document);
            if (errors.Count == 0)
                _promptStatus.text = "Compile successful.";
        }

        // ---- Block toggle ----

        void OnBlockToggleEnabled(string blockId, bool enabled)
        {
            if (_document == null) return;
            var block = _document.FindBlockById(blockId);
            if (block != null)
            {
                block.IsEnabled = enabled;
                _document.IsDirty = true;
            }
        }

        // ---- Code editing ----

        void OnApplyCode()
        {
            if (_document == null || string.IsNullOrEmpty(_selectedBlockId)) return;
            string code = _codeEditorView != null ? _codeEditorView.Code : _codeEditorField.value;
            _document.UpdateBlockCode(_selectedBlockId, code);
            _promptStatus.text = "Code applied.";
        }

        void OnRevertCode()
        {
            if (_document == null || string.IsNullOrEmpty(_selectedBlockId)) return;
            var block = _document.FindBlockById(_selectedBlockId);
            if (block == null) return;

            if (_codeEditorView != null)
                _codeEditorView.Code = block.Code;
            else
                _codeEditorField.value = block.Code;
        }

        // ---- Add block ----

        void OnAddBlock()
        {
            if (_document == null) return;
            if (_document.ActivePass == null || _document.ActivePass.IsUsePass) return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Fragment Block"), false, () => AddBlockWithSection(ShaderSectionType.Fragment));
            menu.AddItem(new GUIContent("Vertex Block"), false, () => AddBlockWithSection(ShaderSectionType.Vertex));
            menu.AddItem(new GUIContent("Helper Function"), false, () => AddBlockWithSection(ShaderSectionType.Helper));
            menu.AddItem(new GUIContent("Constants"), false, () => AddBlockWithSection(ShaderSectionType.Constants));
            menu.ShowAsContext();
        }

        void AddBlockWithSection(ShaderSectionType section)
        {
            string title, intent, code;
            switch (section)
            {
                case ShaderSectionType.Vertex:
                    title = "New Vertex Block";
                    intent = "Modify vertex attributes";
                    code = "void ModifyVertex(inout float3 posOS) {\n    // Vertex modification\n}";
                    break;
                case ShaderSectionType.Helper:
                    title = "New Helper Function";
                    intent = "Utility function";
                    code = "float MyHelper(float input) {\n    return input;\n}";
                    break;
                case ShaderSectionType.Constants:
                    title = "New Constants";
                    intent = "Shared constants";
                    code = "// Constants\nstatic const float PI = 3.14159265;\nstatic const float TAU = 6.28318530;";
                    break;
                default:
                    title = "New Fragment Block";
                    intent = "Compute fragment color";
                    code = "half4 ComputeColor(Varyings input) {\n    return half4(1,1,1,1);\n}";
                    break;
            }

            var block = new ShaderBlock(title, intent, section);
            block.Code = code;
            _document.AddBlock(block);
            RefreshActivePassViews();
            _parameterPanelView.Rebuild(_document);
            OnBlockSelected(block.Id);
        }

        // ---- Inline LLM ----

        void OnInlineLLMRequested()
        {
            if (_document == null || _popupLayer == null) return;

            if (_inlineLLMPopup != null)
            {
                _inlineLLMPopup.RemoveFromHierarchy();
                _inlineLLMPopup = null;
            }

            _inlineLLMPopup = new InlineLLMPopup();
            _inlineLLMPopup.OnSubmit += OnInlineLLMSubmit;
            _inlineLLMPopup.OnClose += () =>
            {
                _inlineLLMPopup?.RemoveFromHierarchy();
                _inlineLLMPopup = null;
            };

            _inlineLLMPopup.style.top = 60;
            _inlineLLMPopup.style.left = 60;
            _popupLayer.Add(_inlineLLMPopup);
            _inlineLLMPopup.FocusInput();
        }

        async void OnInlineLLMSubmit(string request)
        {
            if (_document == null || _codeEditorView == null) return;

            _inlineLLMPopup?.SetStatus("Generating...");

            var historyEntry = _document.History.RecordStart(request, "Inline Insert");

            try
            {
                string blockCode = _codeEditorView.Code;
                int cursor = _codeEditorView.CursorIndex;
                string systemPrompt = LLM.PromptTemplates.BuildSystemPrompt(_document, "Block: inline");
                string userPrompt = LLM.PromptTemplates.BuildInlineInsertPrompt(blockCode, cursor, request);

                var llmService = LLM.LLMService.Instance;
                var response = await llmService.GenerateAsync(systemPrompt, userPrompt);
                string code = LLM.PromptTemplates.ExtractCodeFromResponse(response);

                if (!string.IsNullOrEmpty(code))
                {
                    _codeEditorView.InsertAtCursor(code);
                    _inlineLLMPopup?.SetStatus("Inserted.");
                    _document.History.RecordSuccess(historyEntry, response, "Inline code inserted");
                }
                else
                {
                    _inlineLLMPopup?.SetStatus("No code generated.");
                    _document.History.RecordSuccess(historyEntry, response, "No code generated");
                }
            }
            catch (System.Exception ex)
            {
                _document.History.RecordFailure(historyEntry, ex.Message);
                _inlineLLMPopup?.SetStatus($"Error: {ex.Message}");
                Debug.LogError($"[ShaderAILab] Inline LLM Error: {ex}");
            }

            _inlineLLMPopup?.RemoveFromHierarchy();
            _inlineLLMPopup = null;
        }

        // ---- Autocomplete ----

        void OnAutoCompleteSelected(string text)
        {
            if (_codeEditorView == null) return;
            int tokenStart = _codeEditorView.GetCurrentTokenStart();
            int cursor = _codeEditorView.CursorIndex;

            string code = _codeEditorView.Code;
            string newCode = code.Substring(0, tokenStart) + text + code.Substring(cursor);
            _codeEditorView.Editor.value = newCode;
            _codeEditorView.Editor.SelectRange(tokenStart + text.Length, tokenStart + text.Length);
            _autoComplete?.Hide();
        }

        // ---- Code editor context menu (Promote to Property) ----

        void OnCodeEditorContextMenu(ContextualMenuPopulateEvent evt)
        {
            if (_document == null || _codeEditorView == null) return;

            string selected = _codeEditorView.SelectedText.Trim();
            if (!string.IsNullOrEmpty(selected))
            {
                evt.menu.AppendAction("Promote to Property", _ => ShowPromoteToPropertyPopup(selected));
            }
        }

        void ShowPromoteToPropertyPopup(string variableName)
        {
            if (_popupLayer == null) return;

            var popup = new PromoteToPropertyPopup(variableName);
            popup.OnConfirm += (prop) =>
            {
                _document.AddProperty(prop);

                if (!string.IsNullOrEmpty(_selectedBlockId))
                {
                    var block = _document.FindBlockById(_selectedBlockId);
                    if (block != null && !block.ReferencedParams.Contains(prop.Name))
                        block.ReferencedParams.Add(prop.Name);
                }

                _document.IsDirty = true;
                _parameterPanelView.Rebuild(_document);
                var pass = _document.ActivePass;
                if (pass != null) _blockListView.Rebuild(pass);
                _promptStatus.text = $"Property promoted: {prop.Name}";
                popup.RemoveFromHierarchy();
            };
            popup.OnCancel += () => popup.RemoveFromHierarchy();

            popup.style.top = 80;
            popup.style.left = 40;
            _popupLayer.Add(popup);
        }

        // ---- Prompt (LLM integration) ----

        static bool IsPassLevelRequest(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return false;
            string lower = prompt.ToLowerInvariant();
            string[] passKeywords = {
                "new pass", "second pass", "another pass", "add pass", "outline pass",
                "描边", "第二个pass", "新pass", "新的pass", "添加pass",
                "增加pass", "额外pass", "多pass", "multi-pass", "multipass",
                "轮廓", "outline", "silhouette", "新增一个pass", "加一个pass"
            };
            foreach (string kw in passKeywords)
            {
                if (lower.Contains(kw)) return true;
            }
            return false;
        }

        static bool IsBugFixRequest(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return false;
            string lower = prompt.ToLowerInvariant();
            string[] fixKeywords = {
                "fix", "bug", "error", "compile", "broken", "wrong", "issue", "problem",
                "修复", "修", "bug", "错误", "编译", "报错", "问题", "不对", "不工作", "不正确",
                "debug", "repair", "doesn't work", "not working", "failed", "syntax"
            };
            foreach (string kw in fixKeywords)
            {
                if (lower.Contains(kw)) return true;
            }
            return false;
        }

        async void OnSendPrompt()
        {
            if (_document == null) return;
            string prompt = _promptInput?.value;
            if (string.IsNullOrEmpty(prompt)) return;

            string targetContext = _promptTarget?.value ?? "Global (auto-place)";

            if (targetContext == "Data Flow")
            {
                await OnSendDataFlowPrompt(prompt);
                return;
            }

            // Auto-detect: if user asks for a new pass but target is "Global", upgrade to "New Pass"
            if (targetContext == "Global (auto-place)" && IsPassLevelRequest(prompt))
                targetContext = "New Pass";

            System.Collections.Generic.List<ShaderCompileChecker.CompileError> compileErrors = null;
            if (IsBugFixRequest(prompt) || ShaderCompileChecker.HasErrors(_document))
            {
                compileErrors = ShaderCompileChecker.Check(_document);
                if (compileErrors.Count == 0) compileErrors = null;
            }

            _promptStatus.text = compileErrors != null
                ? $"Generating fix for {compileErrors.Count} compile error(s)..."
                : "Generating...";

            var historyEntry = _document.History.RecordStart(prompt, targetContext);

            try
            {
                var llmService = LLM.LLMService.Instance;
                string rawResult = await llmService.GenerateShaderCodeAsync(prompt, _document, targetContext, compileErrors);

                if (!string.IsNullOrEmpty(rawResult))
                {
                    var sectionType = ShaderSectionType.Fragment;
                    if (targetContext.Contains("Vertex"))
                        sectionType = ShaderSectionType.Vertex;
                    else if (targetContext.Contains("Helper"))
                        sectionType = ShaderSectionType.Helper;

                    var parsed = LLM.PromptTemplates.ParseFullResponse(rawResult, sectionType);
                    MergeNewProperties(parsed.Properties);

                    // Pass-level response: create a new ShaderPass
                    bool isPassResponse = targetContext == "New Pass" || parsed.PassInfo != null;

                    string summary;

                    if (isPassResponse && parsed.PassInfo != null && parsed.Blocks.Count > 0)
                    {
                        CreatePassFromLLMResponse(parsed, prompt);
                        summary = $"Created pass \"{parsed.PassInfo.Name}\" with {parsed.Blocks.Count} block(s)";
                    }
                    else if (targetContext.StartsWith("Block:") && !string.IsNullOrEmpty(_selectedBlockId))
                    {
                        var existingBlock = _document.FindBlockById(_selectedBlockId);
                        var blockSection = existingBlock?.Section ?? ShaderSectionType.Fragment;
                        if (blockSection != sectionType)
                            parsed = LLM.PromptTemplates.ParseFullResponse(rawResult, blockSection);

                        string code = parsed.Blocks.Count > 0 ? parsed.Blocks[0].Code : parsed.LeftoverCode;
                        if (string.IsNullOrEmpty(code))
                            code = LLM.PromptTemplates.ExtractCodeFromResponse(rawResult);

                        _document.UpdateBlockCode(_selectedBlockId, code);
                        if (_codeEditorView != null)
                            _codeEditorView.Code = code;
                        else
                            _codeEditorField.value = code;

                        summary = $"Updated block \"{existingBlock?.Title}\"";
                    }
                    else
                    {
                        string lastBlockId = null;
                        int blockCount = 0;

                        if (parsed.Blocks.Count > 0)
                        {
                            foreach (var pb in parsed.Blocks)
                            {
                                var inferredSection = pb.Section;
                                if (inferredSection == sectionType && targetContext == "Global (auto-place)")
                                    inferredSection = InferSectionFromCode(pb.Code);

                                var newBlock = new ShaderBlock(pb.Title, pb.Intent ?? prompt, inferredSection);
                                newBlock.Code = pb.Code;
                                foreach (var p in pb.ReferencedParams)
                                    newBlock.ReferencedParams.Add(p);
                                _document.AddBlock(newBlock);
                                lastBlockId = newBlock.Id;
                                blockCount++;
                            }
                        }
                        else
                        {
                            string code = !string.IsNullOrEmpty(parsed.LeftoverCode)
                                ? parsed.LeftoverCode
                                : LLM.PromptTemplates.ExtractCodeFromResponse(rawResult);
                            string title = ExtractTitleFromCode(code, prompt);
                            var inferredSection = targetContext == "Global (auto-place)"
                                ? InferSectionFromCode(code) : sectionType;
                            var newBlock = new ShaderBlock(title, prompt, inferredSection);
                            newBlock.Code = code;
                            _document.AddBlock(newBlock);
                            lastBlockId = newBlock.Id;
                            blockCount = 1;
                        }

                        RefreshActivePassViews();
                        _parameterPanelView.Rebuild(_document);
                        if (lastBlockId != null)
                            OnBlockSelected(lastBlockId);

                        summary = $"Added {blockCount} block(s), {parsed.Properties?.Count ?? 0} property(ies)";
                    }

                    _document.History.RecordSuccess(historyEntry, rawResult, summary);
                    _promptStatus.text = "Generation complete.";
                }
            }
            catch (System.Exception ex)
            {
                _document.History.RecordFailure(historyEntry, ex.Message);
                _promptStatus.text = $"Error: {ex.Message}";
                Debug.LogError($"[ShaderAILab] LLM Error: {ex}");
            }
        }

        void CreatePassFromLLMResponse(LLM.PromptTemplates.ParsedLLMResponse parsed, string prompt)
        {
            var passInfo = parsed.PassInfo;
            string passName = !string.IsNullOrEmpty(passInfo.Name) ? passInfo.Name : "NewPass";
            string lightMode = !string.IsNullOrEmpty(passInfo.LightMode) ? passInfo.LightMode : "SRPDefaultUnlit";

            var newPass = new ShaderPass(passName, lightMode);
            newPass.Pragmas.AddRange(new[] {
                "#pragma vertex vert",
                "#pragma fragment frag"
            });
            newPass.Includes.Add("Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl");

            bool hasCull = !string.IsNullOrEmpty(passInfo.CullOverride);
            bool hasBlend = !string.IsNullOrEmpty(passInfo.BlendOverride);
            bool hasZWrite = !string.IsNullOrEmpty(passInfo.ZWriteOverride);
            bool hasZTest = !string.IsNullOrEmpty(passInfo.ZTestOverride);
            bool hasCMask = !string.IsNullOrEmpty(passInfo.ColorMaskOverride);
            bool hasStencil = passInfo.StencilOverride != null;
            if (hasCull || hasBlend || hasZWrite || hasZTest || hasCMask || hasStencil)
            {
                var rs = new PassRenderState(
                    hasCull ? passInfo.CullOverride : null,
                    hasBlend ? passInfo.BlendOverride : null,
                    hasZWrite ? passInfo.ZWriteOverride : null
                );
                rs.ZTestMode = hasZTest ? passInfo.ZTestOverride : null;
                rs.ColorMask = hasCMask ? passInfo.ColorMaskOverride : null;
                rs.Stencil = hasStencil ? passInfo.StencilOverride : null;
                newPass.RenderState = rs;
            }

            foreach (var pb in parsed.Blocks)
            {
                var inferredSection = pb.Section;
                if (inferredSection == ShaderSectionType.Unknown || inferredSection == ShaderSectionType.Fragment)
                    inferredSection = InferSectionFromCode(pb.Code);

                var newBlock = new ShaderBlock(pb.Title, pb.Intent ?? prompt, inferredSection);
                newBlock.Code = pb.Code;
                foreach (var p in pb.ReferencedParams)
                    newBlock.ReferencedParams.Add(p);
                newPass.AddBlock(newBlock);
            }

            _document.AddPass(newPass);
            _document.SetActivePass(_document.Passes.Count - 1);
            RefreshAll();
            _promptStatus.text = $"New pass created: {passName}";
        }

        async System.Threading.Tasks.Task OnSendDataFlowPrompt(string prompt)
        {
            if (_document?.ActivePass == null) return;

            _promptStatus.text = "Analyzing data flow requirements...";

            var historyEntry = _document.History.RecordStart(prompt, "Data Flow");

            try
            {
                var llmService = LLM.LLMService.Instance;
                string systemPrompt = LLM.PromptTemplates.BuildDataFlowSystemPrompt(_document);
                string userPrompt = LLM.PromptTemplates.BuildDataFlowUserPrompt(prompt);

                string result = await llmService.GenerateShaderCodeAsync(userPrompt, _document, "Data Flow");

                var activeDataFlow = _document.ActivePass.DataFlow;

                if (!string.IsNullOrEmpty(result))
                {
                    var (fields, annotations, renderState) = LLM.PromptTemplates.ParseDataFlowResponse(result);

                    int activated = 0;
                    foreach (string fieldName in fields)
                    {
                        var autoActivated = activeDataFlow.ActivateVaryingWithDependencies(fieldName);
                        activated++;

                        if (annotations.TryGetValue(fieldName, out string annotation))
                        {
                            var vf = activeDataFlow.FindField(fieldName, Core.DataFlowStage.Varyings);
                            if (vf != null) vf.Annotation = annotation;
                        }

                        foreach (string autoName in autoActivated)
                        {
                            if (annotations.TryGetValue(autoName, out string autoAnnot))
                            {
                                var af = activeDataFlow.FindField(autoName, Core.DataFlowStage.Attributes);
                                if (af != null) af.Annotation = autoAnnot;
                            }
                        }
                    }

                    if (renderState != null)
                    {
                        var pass = _document.ActivePass;
                        if (pass.RenderState == null) pass.RenderState = new PassRenderState();
                        if (!string.IsNullOrEmpty(renderState.CullMode))   pass.RenderState.CullMode   = renderState.CullMode;
                        if (!string.IsNullOrEmpty(renderState.BlendMode))  pass.RenderState.BlendMode  = renderState.BlendMode;
                        if (!string.IsNullOrEmpty(renderState.ZWriteMode)) pass.RenderState.ZWriteMode = renderState.ZWriteMode;
                        if (!string.IsNullOrEmpty(renderState.ZTestMode))  pass.RenderState.ZTestMode  = renderState.ZTestMode;
                        if (!string.IsNullOrEmpty(renderState.ColorMask))  pass.RenderState.ColorMask  = renderState.ColorMask;
                        if (renderState.Stencil != null)                   pass.RenderState.Stencil    = renderState.Stencil;
                    }

                    _document.IsDirty = true;

                    if (_dataFlowGraphView != null)
                        _dataFlowGraphView.Rebuild(activeDataFlow, _document.ActivePass, _document.GlobalSettings);

                    string statusMsg = $"Data Flow updated: {activated} field(s) activated";
                    if (renderState != null) statusMsg += " + render state modified";

                    _document.History.RecordSuccess(historyEntry, result, statusMsg);

                    SwitchTab(EditorTab.DataFlow);
                    _promptStatus.text = statusMsg + ".";
                }
                else
                {
                    _document.History.RecordSuccess(historyEntry, "", "No data flow changes suggested");
                    _promptStatus.text = "No data flow changes suggested.";
                }
            }
            catch (System.Exception ex)
            {
                _document.History.RecordFailure(historyEntry, ex.Message);
                _promptStatus.text = $"Error: {ex.Message}";
                Debug.LogError($"[ShaderAILab] DataFlow LLM Error: {ex}");
            }
        }

        void OnHistoryResend(string prompt)
        {
            if (_promptInput != null)
            {
                _promptInput.value = prompt;
                SwitchTab(EditorTab.NLBlocks);
            }
        }

        // ---- Plan interaction ----

        async void OnCreatePlanRequested(string userRequest)
        {
            if (_document == null) return;

            var plan = new ShaderPlan(userRequest);
            plan.Status = PlanStatus.Decomposing;
            plan.OnPlanChanged += () => SavePlanToDisk();
            _document.Plan = plan;

            _planView?.SetDecomposing();
            _promptStatus.text = "Decomposing shader plan...";

            try
            {
                var llmService = LLM.LLMService.Instance;
                string systemPrompt = LLM.PromptTemplates.BuildPlanDecompositionSystemPrompt(_document);
                string userPrompt = LLM.PromptTemplates.BuildPlanDecompositionUserPrompt(userRequest);
                string response = await llmService.GenerateAsync(systemPrompt, userPrompt);

                var phases = LLM.PromptTemplates.ParsePlanDecompositionResponse(response);

                if (phases.Count == 0)
                {
                    plan.Status = PlanStatus.Failed;
                    _promptStatus.text = "Failed to decompose plan. LLM returned no phases.";
                    _planView?.Refresh();
                    return;
                }

                plan.Phases = phases;
                plan.Status = PlanStatus.Refining;
                plan.NotifyChanged();

                _promptStatus.text = $"Plan created with {phases.Count} phases. Review and confirm each phase.";
                _planView?.Refresh();
            }
            catch (System.Exception ex)
            {
                plan.Status = PlanStatus.Failed;
                _promptStatus.text = $"Plan error: {ex.Message}";
                _planView?.Refresh();
                Debug.LogError($"[ShaderAILab] Plan decomposition error: {ex}");
            }
        }

        void OnPlanPhaseConfirmed(string phaseId)
        {
            if (_document?.Plan == null) return;
            var phase = _document.Plan.FindPhaseById(phaseId);
            if (phase == null) return;

            phase.Status = PhaseStatus.Confirmed;
            _document.Plan.NotifyChanged();

            if (_document.Plan.AllPhasesHandled)
                _document.Plan.Status = PlanStatus.Ready;

            _planView?.Refresh();
            _promptStatus.text = $"Phase \"{phase.Title}\" confirmed. {_document.Plan.ConfirmedCount}/{_document.Plan.Phases.Count} phases handled.";
        }

        void OnPlanPhaseSkipped(string phaseId)
        {
            if (_document?.Plan == null) return;
            var phase = _document.Plan.FindPhaseById(phaseId);
            if (phase == null) return;

            phase.Status = PhaseStatus.Skipped;
            _document.Plan.NotifyChanged();

            if (_document.Plan.AllPhasesHandled)
                _document.Plan.Status = PlanStatus.Ready;

            _planView?.Refresh();
            _promptStatus.text = $"Phase \"{phase.Title}\" skipped.";
        }

        async void OnPlanPhaseFeedback(string phaseId, string feedback)
        {
            if (_document?.Plan == null) return;
            var plan = _document.Plan;
            var phase = plan.FindPhaseById(phaseId);
            if (phase == null) return;

            phase.UserResponse = feedback;
            phase.RefinementCount++;
            _planView?.SetPhaseRefining(phaseId);
            _promptStatus.text = $"Refining phase \"{phase.Title}\"...";

            try
            {
                var llmService = LLM.LLMService.Instance;
                string systemPrompt = LLM.PromptTemplates.BuildPhaseRefinementSystemPrompt(plan, phase);
                string userPrompt = LLM.PromptTemplates.BuildPhaseRefinementUserPrompt(feedback);
                string response = await llmService.GenerateAsync(systemPrompt, userPrompt);

                var updatedPhase = LLM.PromptTemplates.ParsePhaseRefinementResponse(response);

                if (updatedPhase != null)
                {
                    phase.LLMProposal = updatedPhase.LLMProposal ?? phase.LLMProposal;
                    phase.LLMQuestion = updatedPhase.LLMQuestion;
                    phase.Items = updatedPhase.Items ?? phase.Items;
                    phase.Status = PhaseStatus.WaitingForUser;

                    _planView?.UpdatePhaseCard(phaseId, phase);
                    _promptStatus.text = $"Phase \"{phase.Title}\" refined (iteration {phase.RefinementCount}).";
                }
                else
                {
                    phase.Status = PhaseStatus.WaitingForUser;
                    _planView?.UpdatePhaseCard(phaseId, phase);
                    _promptStatus.text = $"Could not parse refinement for \"{phase.Title}\". Please try again.";
                }

                plan.NotifyChanged();
            }
            catch (System.Exception ex)
            {
                phase.Status = PhaseStatus.WaitingForUser;
                _planView?.UpdatePhaseCard(phaseId, phase);
                _promptStatus.text = $"Refinement error: {ex.Message}";
                Debug.LogError($"[ShaderAILab] Phase refinement error: {ex}");
            }
        }

        async void OnExecutePlanRequested()
        {
            if (_document?.Plan == null) return;
            var plan = _document.Plan;

            if (!plan.AllPhasesHandled)
            {
                _promptStatus.text = "All phases must be confirmed or skipped before executing.";
                return;
            }

            plan.Status = PlanStatus.Executing;
            _planView?.Refresh();

            var executor = new PlanExecutor();
            executor.OnPhaseExecuting += (phaseType, msg) =>
            {
                _promptStatus.text = msg;
                _planView?.SetExecutionProgress(phaseType, msg);
            };

            executor.OnPhaseCompleted += (phaseType, success, msg) =>
            {
                _promptStatus.text = msg;
            };

            try
            {
                await executor.ExecuteAsync(_document, plan);

                plan.Status = PlanStatus.Completed;
                _planView?.Refresh();

                if (_document != null && !string.IsNullOrEmpty(_document.FilePath))
                {
                    ShaderWriter.WriteToFile(_document);
                    ShaderFileWatcher.Instance.AcknowledgeWrite();
                    UnityEditor.AssetDatabase.Refresh();
                }

                RefreshAll();
                CheckCompileErrors();

                _activeTab = EditorTab.Plan;
                SwitchTab(EditorTab.NLBlocks);

                _promptStatus.text = "Plan execution completed! Switched to NL Blocks tab.";
            }
            catch (System.Exception ex)
            {
                plan.Status = PlanStatus.Failed;
                _planView?.Refresh();
                RefreshAll();

                _activeTab = EditorTab.Plan;
                SwitchTab(EditorTab.NLBlocks);

                _promptStatus.text = $"Execution finished with errors: {ex.Message}";
                Debug.LogError($"[ShaderAILab] Plan execution error: {ex}");
            }
        }

        void OnResetPlanRequested()
        {
            if (_document == null) return;

            if (_document.Plan != null && _document.Plan.Status != PlanStatus.Empty)
            {
                if (!EditorUtility.DisplayDialog("Reset Plan",
                    "Are you sure you want to discard the current plan?", "Reset", "Cancel"))
                    return;
            }

            if (!string.IsNullOrEmpty(_document.FilePath))
                ShaderPlan.DeleteFile(_document.FilePath);

            _document.Plan = null;
            _planView?.Refresh();
            _promptStatus.text = "Plan reset.";
        }

        // ---- LLM response helpers ----

        void MergeNewProperties(List<ShaderProperty> newProps)
        {
            if (newProps == null || newProps.Count == 0) return;
            foreach (var prop in newProps)
            {
                if (string.IsNullOrEmpty(prop.Name)) continue;
                if (_document.FindProperty(prop.Name) != null) continue;
                _document.AddProperty(prop);
            }
        }

        static ShaderSectionType InferSectionFromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return ShaderSectionType.Fragment;

            var sig = System.Text.RegularExpressions.Regex.Match(code,
                @"\b(void|half4|half3|half2|half|float4|float3|float2|float|int)\s+\w+\s*\(([^)]*)\)");
            if (!sig.Success) return ShaderSectionType.Fragment;

            string returnType = sig.Groups[1].Value;
            string paramList = sig.Groups[2].Value;

            if ((returnType == "half4" || returnType == "float4") && paramList.Contains("Varyings"))
                return ShaderSectionType.Fragment;
            if (returnType == "void" && paramList.Contains("inout") && paramList.Contains("float3"))
                return ShaderSectionType.Vertex;

            return ShaderSectionType.Helper;
        }

        static string ExtractTitleFromCode(string code, string fallbackPrompt)
        {
            if (!string.IsNullOrEmpty(code))
            {
                var funcMatch = System.Text.RegularExpressions.Regex.Match(
                    code, @"(?:half4|float4|void|float|half)\s+(\w+)\s*\(");
                if (funcMatch.Success)
                {
                    string name = funcMatch.Groups[1].Value;
                    string spaced = System.Text.RegularExpressions.Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");
                    return spaced;
                }
            }
            if (fallbackPrompt.Length > 30)
                return fallbackPrompt.Substring(0, 30) + "...";
            return fallbackPrompt;
        }

        // ---- Parameter changes ----

        void OnParameterChanged(string propertyName, object newValue)
        {
            _previewView?.UpdateProperty(propertyName, newValue);
        }

        // ---- Compile error diagnostics ----

        void CheckCompileErrors()
        {
            if (_document == null) return;
            var errors = ShaderCompileChecker.Check(_document);
            if (errors.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"{errors.Count} compile error(s): ");
                foreach (var e in errors)
                {
                    string loc = !string.IsNullOrEmpty(e.BlockTitle) ? $"[{e.BlockTitle}]" : $"line {e.Line}";
                    sb.Append($"{loc} {e.Message}; ");
                }
                _promptStatus.text = sb.ToString();
                Debug.LogWarning($"[ShaderAILab] {sb}");
            }
        }
    }

    /// <summary>
    /// Simple input dialog for rename operations.
    /// </summary>
    internal static class EditorInputDialog
    {
        public static string Show(string title, string message, string defaultValue)
        {
            string result = defaultValue;
            // Unity doesn't have a built-in text input dialog,
            // so we use a simple approach via EditorUtility + a temp ScriptableObject isn't ideal.
            // For now, cycle through a simple prompt fallback.
            bool ok = EditorUtility.DisplayDialog(title,
                $"{message}\n\nCurrent: \"{defaultValue}\"\n\n(Type in the console or use the Inspector to rename.)",
                "OK", "Cancel");
            return ok ? result : null;
        }
    }
}
