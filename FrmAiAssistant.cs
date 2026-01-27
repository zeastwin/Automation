using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Automation.AIFlow;

namespace Automation
{
    public partial class FrmAiAssistant : Form
    {
        private AiCoreFlow baseCore;
        private AiCoreFlow currentCore;
        private AiFlowDelta currentDelta;
        private AiFlowDiffResult currentDiff;
        private AiFlowTrace lastTrace;
        private AiFlowTelemetryRecorder telemetryRecorder;
        private Timer telemetryTimer;
        private string lastWorkDir;
        private string verifyState = "未执行";
        private string simState = "未执行";
        private int diffCount;
        private AiFlowAiConfig aiConfig;

        public FrmAiAssistant()
        {
            InitializeComponent();
            WireEvents();
        }

        private void btnNavPropose_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPropose;
        }

        private void btnNavVerify_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabVerify;
        }

        private void btnNavSim_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabSim;
        }

        private void btnNavDiff_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabDiff;
        }

        private void btnNavTelemetry_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabTelemetry;
        }

        private void WireEvents()
        {
            btnLoadCore.Click += BtnLoadCore_Click;
            btnLoadDelta.Click += BtnLoadDelta_Click;
            btnVerifyRun.Click += BtnVerifyRun_Click;
            btnSimRun.Click += BtnSimRun_Click;
            btnApply.Click += BtnApply_Click;
            btnRollback.Click += BtnRollback_Click;
            btnExportTrace.Click += BtnExportTrace_Click;
            btnAiLoadConfig.Click += BtnAiLoadConfig_Click;
            btnAiSaveConfig.Click += BtnAiSaveConfig_Click;
            btnAiGenerate.Click += BtnAiGenerate_Click;
            chkAiShowKey.CheckedChanged += ChkAiShowKey_CheckedChanged;
            Load += FrmAiAssistant_Load;
            FormClosing += FrmAiAssistant_FormClosing;
        }

        private void FrmAiAssistant_Load(object sender, EventArgs e)
        {
            lastWorkDir = SF.workPath;
            TryStartTelemetry();
            InitAiConfigUi();
            LoadAiConfigToUi(false);
            UpdateQuickInfo();
            SetStatus("就绪");
        }

        private void FrmAiAssistant_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (telemetryTimer != null)
            {
                telemetryTimer.Stop();
                telemetryTimer.Tick -= TelemetryTimer_Tick;
                telemetryTimer.Dispose();
                telemetryTimer = null;
            }
            telemetryRecorder?.Dispose();
            telemetryRecorder = null;
        }

        private void TryStartTelemetry()
        {
            if (SF.DR == null)
            {
                return;
            }
            telemetryRecorder = new AiFlowTelemetryRecorder(SF.DR);
            telemetryRecorder.Start();
            UpdateTelemetryView();
            telemetryTimer = new Timer();
            telemetryTimer.Interval = 500;
            telemetryTimer.Tick += TelemetryTimer_Tick;
            telemetryTimer.Start();
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            UpdateTelemetryView();
            UpdateQuickInfo();
        }

        private void BtnLoadCore_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "JSON|*.json|All|*.*";
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                string path = dialog.FileName;
                if (!TryLoadCoreOrSpec(path, out string message))
                {
                    ShowIssues("加载失败", message);
                    return;
                }
                lblRevision.Text = "Revision: 未加载";
                verifyState = "未执行";
                simState = "未执行";
                diffCount = 0;
                UpdateQuickInfo();
                txtContext.Text = JsonConvert.SerializeObject(currentCore, Formatting.Indented);
                ClearDiff();
                if (cmbAiOutputKind.Items.Count > 0)
                {
                    cmbAiOutputKind.SelectedIndex = 0;
                }
                LogInfo("加载 Core/Spec 完成。", FrmInfo.Level.Normal);
                SetStatus("已加载 Core/Spec");
            }
        }

        private void BtnLoadDelta_Click(object sender, EventArgs e)
        {
            if (baseCore == null)
            {
                ShowIssues("加载Delta失败", "请先加载 Core/Spec");
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "JSON|*.json|All|*.*";
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                currentDelta = AiFlowIo.ReadDelta(dialog.FileName, out List<AiFlowIssue> issues);
                if (issues.Count > 0 || currentDelta == null)
                {
                    ShowIssues("加载Delta失败", FormatIssues(issues));
                    return;
                }
                currentCore = AiFlowDeltaApplier.Apply(baseCore, currentDelta, out issues);
                if (issues.Count > 0 || currentCore == null)
                {
                    ShowIssues("应用Delta失败", FormatIssues(issues));
                    return;
                }
                currentDiff = AiFlowDiff.Build(baseCore, currentCore);
                RenderDiff(currentDiff);
                diffCount = CountDiffOps(currentDiff);
                UpdateQuickInfo();
                txtOutput.Text = JsonConvert.SerializeObject(currentDelta, Formatting.Indented);
                txtContext.Text = JsonConvert.SerializeObject(currentCore, Formatting.Indented);
                LogInfo("加载 Delta 完成。", FrmInfo.Level.Normal);
                SetStatus("已加载 Delta");
            }
        }

        private void BtnVerifyRun_Click(object sender, EventArgs e)
        {
            if (currentCore == null)
            {
                ShowIssues("验证失败", "未加载 Core/Spec");
                return;
            }
            List<AiFlowIssue> issues = AiFlowVerifier.VerifyCore(currentCore);
            RenderVerifyResult(issues);
            verifyState = issues.Count == 0 ? "通过" : "失败";
            UpdateQuickInfo();
            LogInfo(issues.Count == 0 ? "验证通过。" : "验证失败。", issues.Count == 0 ? FrmInfo.Level.Normal : FrmInfo.Level.Error);
            SetStatus(issues.Count == 0 ? "验证通过" : "验证失败");
        }

        private void BtnSimRun_Click(object sender, EventArgs e)
        {
            if (currentCore == null)
            {
                ShowIssues("仿真失败", "未加载 Core/Spec");
                return;
            }
            if (!TryParseScenario(txtScenario.Text, out AiFlowScenario scenario, out string error))
            {
                ShowIssues("仿真失败", error);
                return;
            }
            AiFlowTrace trace = AiFlowSimulator.Simulate(currentCore, scenario);
            lastTrace = trace;
            txtTrace.Text = JsonConvert.SerializeObject(trace, Formatting.Indented);
            if (trace.Issues.Count > 0)
            {
                simState = "失败";
                UpdateQuickInfo();
                LogInfo("仿真失败。", FrmInfo.Level.Error);
                SetStatus("仿真失败");
            }
            else
            {
                simState = "完成";
                UpdateQuickInfo();
                LogInfo("仿真完成。", FrmInfo.Level.Normal);
                SetStatus("仿真完成");
            }
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (currentCore == null)
            {
                ShowIssues("落盘失败", "未加载 Core/Spec");
                return;
            }
            string configRoot;
            try
            {
                configRoot = Path.GetFullPath(SF.ConfigPath ?? string.Empty)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                ShowIssues("落盘失败", $"Config 路径无效:{ex.Message}");
                return;
            }
            if (string.IsNullOrWhiteSpace(configRoot))
            {
                ShowIssues("落盘失败", "Config 路径为空");
                return;
            }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 Config 目录";
                dialog.SelectedPath = configRoot;
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                string selected;
                try
                {
                    selected = Path.GetFullPath(dialog.SelectedPath ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch (Exception ex)
                {
                    ShowIssues("落盘失败", $"选择路径无效:{ex.Message}");
                    return;
                }
                if (!string.Equals(selected, configRoot, StringComparison.OrdinalIgnoreCase))
                {
                    ShowIssues("落盘失败", $"只能选择 Config 目录:{configRoot}");
                    return;
                }
                lastWorkDir = Path.Combine(configRoot, "Work");
            }

            AiFlowCompileResult compile = AiFlowCompiler.CompileCore(currentCore);
            if (!compile.Success)
            {
                ShowIssues("落盘失败", FormatIssues(compile.Issues));
                return;
            }
            if (!AiFlowRevision.SaveRevision(lastWorkDir, "AI Apply", out string revisionId, out List<AiFlowIssue> revIssues))
            {
                ShowIssues("回滚点创建失败", FormatIssues(revIssues));
                return;
            }
            lblRevision.Text = $"Revision: {revisionId}";
            if (!AiFlowCompiler.ApplyToWorkPath(compile.Procs, lastWorkDir, out List<AiFlowIssue> applyIssues))
            {
                ShowIssues("落盘失败", FormatIssues(applyIssues));
                return;
            }
            RefreshRevisionList(lastWorkDir);
            LogInfo($"落盘完成，Work={lastWorkDir}。", FrmInfo.Level.Normal);
            SetStatus("落盘完成");
        }

        private void BtnRollback_Click(object sender, EventArgs e)
        {
            if (listRevisions.SelectedItem == null)
            {
                ShowIssues("回滚失败", "请选择回滚点");
                return;
            }
            string revisionId = listRevisions.SelectedItem.ToString();
            if (string.IsNullOrWhiteSpace(lastWorkDir))
            {
                string configRoot;
                try
                {
                    configRoot = Path.GetFullPath(SF.ConfigPath ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch (Exception ex)
                {
                    ShowIssues("回滚失败", $"Config 路径无效:{ex.Message}");
                    return;
                }
                if (string.IsNullOrWhiteSpace(configRoot))
                {
                    ShowIssues("回滚失败", "Config 路径为空");
                    return;
                }
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择 Config 目录";
                    dialog.SelectedPath = configRoot;
                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                    string selected;
                    try
                    {
                        selected = Path.GetFullPath(dialog.SelectedPath ?? string.Empty)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                    catch (Exception ex)
                    {
                        ShowIssues("回滚失败", $"选择路径无效:{ex.Message}");
                        return;
                    }
                    if (!string.Equals(selected, configRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        ShowIssues("回滚失败", $"只能选择 Config 目录:{configRoot}");
                        return;
                    }
                    lastWorkDir = Path.Combine(configRoot, "Work");
                }
            }
            if (!AiFlowRevision.Rollback(lastWorkDir, revisionId, out List<AiFlowIssue> issues))
            {
                ShowIssues("回滚失败", FormatIssues(issues));
                return;
            }
            LogInfo($"回滚完成，Revision={revisionId}。", FrmInfo.Level.Normal);
            SetStatus("回滚完成");
        }

        private void BtnExportTrace_Click(object sender, EventArgs e)
        {
            AiFlowTrace trace = lastTrace ?? telemetryRecorder?.Trace;
            if (trace == null)
            {
                ShowIssues("导出失败", "暂无 Trace 可导出");
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "JSON|*.json|All|*.*";
                dialog.FileName = "trace.json";
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                AiFlowIo.WriteTrace(dialog.FileName, trace, out List<AiFlowIssue> issues);
                if (issues.Count > 0)
                {
                    ShowIssues("导出失败", FormatIssues(issues));
                    return;
                }
                LogInfo($"Trace 已导出:{dialog.FileName}", FrmInfo.Level.Normal);
                SetStatus("已导出 Trace");
            }
        }

        private void BtnAiLoadConfig_Click(object sender, EventArgs e)
        {
            LoadAiConfigToUi(true);
        }

        private void BtnAiSaveConfig_Click(object sender, EventArgs e)
        {
            if (!TryBuildAiConfigFromUi(out AiFlowAiConfig config, out string message))
            {
                ShowIssues("保存配置失败", message);
                return;
            }
            if (!AiFlowAiConfigStore.TrySave(config, out List<AiFlowIssue> issues))
            {
                ShowIssues("保存配置失败", FormatIssues(issues));
                return;
            }
            aiConfig = config;
            LogInfo("AI配置已保存。", FrmInfo.Level.Normal);
            SetStatus("AI配置已保存");
        }

        private async void BtnAiGenerate_Click(object sender, EventArgs e)
        {
            if (!TryBuildAiConfigFromUi(out AiFlowAiConfig config, out string message))
            {
                ShowIssues("生成失败", message);
                return;
            }
            string outputKind = GetSelectedOutputKind();
            if (string.IsNullOrWhiteSpace(outputKind))
            {
                ShowIssues("生成失败", "未选择输出类型");
                return;
            }
            if (string.Equals(outputKind, "FlowDelta", StringComparison.Ordinal) && baseCore == null)
            {
                ShowIssues("生成失败", "FlowDelta 需要先加载 Core/Spec 作为基线");
                return;
            }

            btnAiGenerate.Enabled = false;
            SetStatus("AI生成中...");
            LogInfo("开始调用AI生成。", FrmInfo.Level.Normal);

            AiFlowAiResult result = await AiFlowAiClient.RequestAsync(config, txtRequest.Text, txtContext.Text, outputKind);
            if (!result.Success)
            {
                ShowIssues("生成失败", FormatIssues(result.Issues));
                if (!string.IsNullOrWhiteSpace(result.RawResponse))
                {
                    txtOutput.Text = result.RawResponse;
                }
                btnAiGenerate.Enabled = true;
                SetStatus("生成失败");
                return;
            }

            txtOutput.Text = result.Content;
            if (!TryApplyAiOutput(outputKind, result.Content, out string applyMessage))
            {
                ShowIssues("生成成功但解析失败", applyMessage);
                btnAiGenerate.Enabled = true;
                SetStatus("生成完成(解析失败)");
                return;
            }

            btnAiGenerate.Enabled = true;
            LogInfo("AI生成完成。", FrmInfo.Level.Normal);
            SetStatus("生成完成");
        }

        private void ChkAiShowKey_CheckedChanged(object sender, EventArgs e)
        {
            txtAiKey.UseSystemPasswordChar = !chkAiShowKey.Checked;
        }

        private void InitAiConfigUi()
        {
            cmbAiOutputKind.Items.Clear();
            cmbAiOutputKind.Items.Add("FlowDelta");
            cmbAiOutputKind.Items.Add("Core");
            cmbAiOutputKind.SelectedIndex = baseCore == null ? 1 : 0;
        }

        private void LoadAiConfigToUi(bool showMessage)
        {
            if (AiFlowAiConfigStore.TryLoad(out AiFlowAiConfig config, out List<AiFlowIssue> issues))
            {
                aiConfig = config;
                ApplyAiConfigToUi(config);
                if (showMessage)
                {
                    LogInfo("AI配置已加载。", FrmInfo.Level.Normal);
                    SetStatus("AI配置已加载");
                }
                return;
            }

            aiConfig = null;
            if (showMessage)
            {
                ShowIssues("加载配置失败", FormatIssues(issues));
            }
            else
            {
                AiFlowAiConfig defaults = AiFlowAiConfigStore.CreateDefault();
                ApplyAiConfigToUi(defaults);
            }
        }

        private void ApplyAiConfigToUi(AiFlowAiConfig config)
        {
            if (config == null)
            {
                return;
            }
            txtAiEndpoint.Text = config.Endpoint ?? string.Empty;
            txtAiKey.Text = config.ApiKey ?? string.Empty;
            txtAiModel.Text = config.Model ?? string.Empty;
            txtAiAuthHeader.Text = config.AuthHeader ?? string.Empty;
            txtAiAuthPrefix.Text = config.AuthPrefix ?? string.Empty;
            if (config.TimeoutSeconds > 0 && config.TimeoutSeconds <= (int)numAiTimeout.Maximum)
            {
                numAiTimeout.Value = config.TimeoutSeconds;
            }
            if (cmbAiOutputKind.SelectedIndex < 0)
            {
                cmbAiOutputKind.SelectedIndex = baseCore == null ? 1 : 0;
            }
        }

        private bool TryBuildAiConfigFromUi(out AiFlowAiConfig config, out string message)
        {
            config = new AiFlowAiConfig
            {
                Version = AiFlowAiConfig.ConfigVersion,
                Endpoint = txtAiEndpoint.Text?.Trim(),
                ApiKey = txtAiKey.Text,
                Model = txtAiModel.Text?.Trim(),
                TimeoutSeconds = (int)numAiTimeout.Value,
                AuthHeader = txtAiAuthHeader.Text?.Trim(),
                AuthPrefix = txtAiAuthPrefix.Text?.Trim(),
                Temperature = aiConfig?.Temperature ?? 0.2
            };
            List<AiFlowIssue> issues = AiFlowAiConfigStore.Validate(config);
            if (issues.Count > 0)
            {
                message = FormatIssues(issues);
                return false;
            }
            message = null;
            return true;
        }

        private string GetSelectedOutputKind()
        {
            if (cmbAiOutputKind.SelectedItem == null)
            {
                return null;
            }
            return cmbAiOutputKind.SelectedItem.ToString();
        }

        private bool TryApplyAiOutput(string outputKind, string content, out string message)
        {
            message = null;
            if (string.Equals(outputKind, "FlowDelta", StringComparison.Ordinal))
            {
                if (!AiFlowAiClient.TryParseDelta(content, out AiFlowDelta delta, out List<AiFlowIssue> issues))
                {
                    message = FormatIssues(issues);
                    return false;
                }
                currentDelta = delta;
                currentCore = AiFlowDeltaApplier.Apply(baseCore, currentDelta, out issues);
                if (issues.Count > 0 || currentCore == null)
                {
                    message = FormatIssues(issues);
                    return false;
                }
                currentDiff = AiFlowDiff.Build(baseCore, currentCore);
                RenderDiff(currentDiff);
                diffCount = CountDiffOps(currentDiff);
                txtContext.Text = JsonConvert.SerializeObject(currentCore, Formatting.Indented);
                UpdateQuickInfo();
                return true;
            }

            if (string.Equals(outputKind, "Core", StringComparison.Ordinal))
            {
                if (!AiFlowAiClient.TryParseCore(content, out AiCoreFlow core, out List<AiFlowIssue> issues))
                {
                    message = FormatIssues(issues);
                    return false;
                }
                baseCore = core;
                currentCore = core;
                currentDelta = null;
                currentDiff = null;
                txtContext.Text = JsonConvert.SerializeObject(currentCore, Formatting.Indented);
                ClearDiff();
                UpdateQuickInfo();
                return true;
            }
            message = "输出类型未知";
            return false;
        }

        private bool TryLoadCoreOrSpec(string path, out string message)
        {
            message = null;
            baseCore = null;
            currentCore = null;
            currentDelta = null;
            currentDiff = null;

            AiCoreFlow core = AiFlowIo.ReadCore(path, out List<AiFlowIssue> issues);
            if (issues.Count == 0 && core != null)
            {
                baseCore = core;
                currentCore = core;
                return true;
            }

            AiSpecFlow spec = AiFlowIo.ReadSpec(path, out issues);
            if (issues.Count == 0 && spec != null)
            {
                AiFlowCompileResult result = AiFlowCompiler.CompileSpec(spec);
                if (!result.Success)
                {
                    message = FormatIssues(result.Issues);
                    return false;
                }
                baseCore = spec.Core;
                currentCore = spec.Core;
                return true;
            }

            message = FormatIssues(issues);
            return false;
        }

        private void RenderVerifyResult(List<AiFlowIssue> issues)
        {
            listRules.Items.Clear();
            txtDiagnostics.Clear();
            if (issues == null || issues.Count == 0)
            {
                listRules.Items.Add("全部规则通过");
                txtDiagnostics.Text = "无错误";
                return;
            }
            foreach (AiFlowIssue issue in issues)
            {
                listRules.Items.Add($"{issue.Code} {issue.Location}");
            }
            txtDiagnostics.Text = FormatIssues(issues);
        }

        private void RenderDiff(AiFlowDiffResult diff)
        {
            txtDiff.Text = diff == null ? string.Empty : JsonConvert.SerializeObject(diff, Formatting.Indented);
            listSummary.Items.Clear();
            if (diff == null)
            {
                listSummary.Items.Add("无变更");
                return;
            }
            listSummary.Items.Add($"新增流程: {diff.AddedProcs.Count}");
            listSummary.Items.Add($"删除流程: {diff.RemovedProcs.Count}");
            listSummary.Items.Add($"修改流程: {diff.ModifiedProcs.Count}");
            listSummary.Items.Add($"新增步骤: {diff.AddedSteps.Count}");
            listSummary.Items.Add($"删除步骤: {diff.RemovedSteps.Count}");
            listSummary.Items.Add($"新增操作: {diff.AddedOps.Count}");
            listSummary.Items.Add($"删除操作: {diff.RemovedOps.Count}");
            listSummary.Items.Add($"修改操作: {diff.ModifiedOps.Count}");
            lblQuick.Text = $"当前流程：已加载\n校验状态：未执行\n仿真状态：未执行\n变更：{diff.AddedOps.Count + diff.RemovedOps.Count + diff.ModifiedOps.Count}";
        }

        private void ClearDiff()
        {
            txtDiff.Clear();
            listSummary.Items.Clear();
            listRevisions.Items.Clear();
            currentDiff = null;
        }

        private bool TryParseScenario(string text, out AiFlowScenario scenario, out string error)
        {
            error = null;
            scenario = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                scenario = new AiFlowScenario { Version = AiFlowSimulator.ScenarioVersion, Start = new AiFlowScenarioStart() };
                if (TryGetSelectedIndices(out int procIndex, out int stepIndex, out int opIndex))
                {
                    if (procIndex >= 0)
                    {
                        scenario.Start.ProcIndex = procIndex;
                    }
                    if (stepIndex >= 0)
                    {
                        scenario.Start.StepIndex = stepIndex;
                    }
                    if (opIndex >= 0)
                    {
                        scenario.Start.OpIndex = opIndex;
                    }
                }
                return true;
            }
            try
            {
                scenario = JsonConvert.DeserializeObject<AiFlowScenario>(text);
                if (scenario == null)
                {
                    error = "scenario 解析失败";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void RefreshRevisionList(string workDir)
        {
            listRevisions.Items.Clear();
            if (string.IsNullOrWhiteSpace(workDir))
            {
                return;
            }
            string root = Path.Combine(Path.GetDirectoryName(workDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty, "Work_revisions");
            if (!Directory.Exists(root))
            {
                return;
            }
            foreach (string dir in Directory.GetDirectories(root))
            {
                listRevisions.Items.Add(Path.GetFileName(dir));
            }
        }

        private void UpdateTelemetryView()
        {
            if (telemetryRecorder?.Trace == null)
            {
                return;
            }
            txtTelemetry.Text = JsonConvert.SerializeObject(telemetryRecorder.Trace, Formatting.Indented);
        }

        private void UpdateQuickInfo()
        {
            string procText = GetSelectedProcDisplay();
            string workText = string.IsNullOrWhiteSpace(lastWorkDir) ? SF.workPath : lastWorkDir;
            lblQuick.Text = $"当前流程：{procText}\n校验状态：{verifyState}\n仿真状态：{simState}\n变更：{diffCount}\nWork：{workText}";
        }

        private string GetSelectedProcDisplay()
        {
            if (SF.frmProc?.procsList == null)
            {
                return "未选择";
            }
            int index = SF.frmProc.SelectedProcNum;
            if (index < 0 || index >= SF.frmProc.procsList.Count)
            {
                return "未选择";
            }
            string name = SF.frmProc.procsList[index]?.head?.Name;
            return string.IsNullOrWhiteSpace(name) ? $"索引{index}" : $"{index}:{name}";
        }

        private bool TryGetSelectedIndices(out int procIndex, out int stepIndex, out int opIndex)
        {
            procIndex = SF.frmProc?.SelectedProcNum ?? -1;
            stepIndex = SF.frmProc?.SelectedStepNum ?? -1;
            opIndex = SF.frmDataGrid?.iSelectedRow ?? -1;
            if (opIndex < 0 && SF.frmDataGrid?.dataGridView1?.CurrentCell != null)
            {
                opIndex = SF.frmDataGrid.dataGridView1.CurrentCell.RowIndex;
            }
            return procIndex >= 0;
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = $"状态：{text}";
        }

        private void ShowIssues(string title, string content)
        {
            MessageBox.Show(content, title);
            LogInfo($"{title} {content}", FrmInfo.Level.Error);
        }

        private string FormatIssues(List<AiFlowIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return "无错误";
            }
            var lines = new List<string>();
            foreach (AiFlowIssue issue in issues)
            {
                lines.Add($"[{issue.Code}] {issue.Location} {issue.Message}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private int CountDiffOps(AiFlowDiffResult diff)
        {
            if (diff == null)
            {
                return 0;
            }
            return diff.AddedOps.Count + diff.RemovedOps.Count + diff.ModifiedOps.Count;
        }

        private void LogInfo(string message, FrmInfo.Level level)
        {
            if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo($"AI助手：{message}", level);
            }
        }
    }
}
