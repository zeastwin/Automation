using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Automation
{
    public sealed partial class FrmAiAssistant
    {
        #region 自定义审核对话框

        // 预演确认对话框：以表格形式展示变更详情（操作类型/位置/对象/字段/原值/新值），让用户清晰看到改了什么。
        private DialogResult ShowPreviewApprovalDialog(string previewId, JArray changes, JArray messages)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "EW-AI 预演确认";
                dlg.StartPosition = FormStartPosition.CenterParent;
                bool hasChanges = changes != null && changes.Count > 0;
                dlg.Width = 820;
                dlg.Height = hasChanges ? 520 : 330;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.BackColor = UiPalette.Background;
                dlg.Font = new Font("微软雅黑", 9F);

                // 标题区：使用浅色层级，避免大色块压迫内容。
                Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = UiPalette.SurfaceStrong, Padding = new Padding(18, 10, 18, 8) };
                headerPanel.Controls.Add(new Label
                {
                    Text = "确认本次预演",
                    Font = new Font("微软雅黑", 14F, FontStyle.Bold),
                    ForeColor = UiPalette.TextPrimary,
                    Dock = DockStyle.Top,
                    Height = 30,
                    TextAlign = ContentAlignment.MiddleLeft
                });
                headerPanel.Controls.Add(new Label
                {
                    Text = hasChanges ? "请检查变更明细，确认后才会提交。" : "请确认以下操作，确认后才会提交。",
                    Font = new Font("微软雅黑", 9F),
                    ForeColor = UiPalette.TextMuted,
                    Dock = DockStyle.Bottom,
                    Height = 22,
                    TextAlign = ContentAlignment.MiddleLeft
                });

                // 信息行
                Panel infoPanel = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(18, 8, 18, 6), BackColor = UiPalette.Background };
                infoPanel.Controls.Add(new Label
                {
                    Text = $"预演编号  {previewId}      变更  {changes?.Count ?? 0} 项",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = UiPalette.TextSecondary,
                    Font = new Font("Consolas", 9F)
                });

                // 变更表格
                DataGridView dgv = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    ReadOnly = true,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = UiPalette.SurfaceStrong,
                    BorderStyle = BorderStyle.FixedSingle,
                    ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                    {
                        BackColor = UiPalette.SurfaceSubtle,
                        Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                        Alignment = DataGridViewContentAlignment.MiddleCenter
                    },
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Font = new Font("微软雅黑", 9F),
                        Alignment = DataGridViewContentAlignment.MiddleLeft,
                        WrapMode = DataGridViewTriState.True
                    },
                    RowHeadersVisible = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    GridColor = UiPalette.Stroke,
                    EnableHeadersVisualStyles = false,
                    ColumnHeadersHeight = 34,
                    RowTemplate = { Height = 32 }
                };
                dgv.Columns.Add("colType", "操作类型");
                dgv.Columns.Add("colLocation", "位置");
                dgv.Columns.Add("colObject", "对象");
                dgv.Columns.Add("colField", "字段");
                dgv.Columns.Add("colOld", "原值");
                dgv.Columns.Add("colNew", "新值");
                dgv.Columns["colOld"].DefaultCellStyle.ForeColor = UiPalette.Danger;
                dgv.Columns["colNew"].DefaultCellStyle.ForeColor = UiPalette.Success;

                if (changes != null)
                {
                    foreach (JToken change in changes)
                    {
                        string type = change["type"]?.Value<string>() ?? "";
                        string location = "";
                        string obj = "";
                        string field = "—";
                        string oldVal = "—";
                        string newVal = "—";
                        Color rowColor = UiPalette.SurfaceStrong;

                        switch (type)
                        {
                            case "field_change":
                                type = "修改字段";
                                location = change["target"]?.Value<string>() ?? "";
                                field = change["field"]?.Value<string>() ?? "";
                                oldVal = FormatJsonValue(change["oldValue"]);
                                newVal = FormatJsonValue(change["newValue"]);
                                rowColor = UiPalette.WarningSoft;
                                break;
                            case "insert_step":
                            case "append_step":
                                type = "新增步骤";
                                location = $"步骤{change["stepIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                rowColor = UiPalette.SuccessSoft;
                                break;
                            case "delete_step":
                                type = "删除步骤";
                                location = $"步骤{change["oldStepIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                rowColor = UiPalette.DangerSoft;
                                break;
                            case "move_step":
                                type = "移动步骤";
                                location = $"{change["oldStepIndex"]?.Value<int>() ?? 0} → {change["newStepIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                rowColor = UiPalette.InfoSoft;
                                break;
                            case "insert_operation":
                            case "append_operation":
                                type = "新增指令";
                                location = $"步骤{change["stepIndex"]?.Value<int>() ?? 0}/指令{change["opIndex"]?.Value<int>() ?? 0}";
                                obj = $"{change["name"]?.Value<string>() ?? ""}({change["operaType"]?.Value<string>() ?? ""})";
                                rowColor = UiPalette.SuccessSoft;
                                break;
                            case "delete_operation":
                                type = "删除指令";
                                location = $"步骤{change["oldStepIndex"]?.Value<int>() ?? 0}/指令{change["oldOpIndex"]?.Value<int>() ?? 0}";
                                obj = $"{change["name"]?.Value<string>() ?? ""}({change["operaType"]?.Value<string>() ?? ""})";
                                rowColor = UiPalette.DangerSoft;
                                break;
                            case "move_operation":
                                type = "移动指令";
                                location = $"{change["oldStepIndex"]?.Value<int>() ?? 0}-{change["oldOpIndex"]?.Value<int>() ?? 0} → {change["newStepIndex"]?.Value<int>() ?? 0}-{change["newOpIndex"]?.Value<int>() ?? 0}";
                                obj = $"{change["name"]?.Value<string>() ?? ""}({change["operaType"]?.Value<string>() ?? ""})";
                                rowColor = UiPalette.InfoSoft;
                                break;
                            case "goto_rewrite":
                                type = "跳转重写";
                                location = $"重写{change["rewrittenCount"]?.Value<int>() ?? 0}/失效{change["invalidatedCount"]?.Value<int>() ?? 0}";
                                break;
                            case "process.delete":
                                type = "删除流程";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "流程";
                                oldVal = obj;
                                newVal = "已删除";
                                rowColor = UiPalette.DangerSoft;
                                break;
                            case "process.create":
                                type = "创建流程";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "结构";
                                newVal = $"{change["stepCount"]?.Value<int>() ?? 0}步骤 / {change["operationCount"]?.Value<int>() ?? 0}指令";
                                rowColor = UiPalette.SuccessSoft;
                                break;
                            case "process.replace":
                                type = "替换流程";
                                location = $"流程{change["procIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "完整结构";
                                oldVal = change["oldName"]?.Value<string>() ?? "";
                                newVal = $"{change["stepCount"]?.Value<int>() ?? 0}步骤 / {change["operationCount"]?.Value<int>() ?? 0}指令";
                                rowColor = UiPalette.WarningSoft;
                                break;
                            case "process.modify":
                                type = "修改流程";
                                location = $"流程{change["procIndex"]?.Value<int>() ?? 0}";
                                obj = change["name"]?.Value<string>() ?? "";
                                field = "动作完成后结构";
                                newVal = $"{change["stepCount"]?.Value<int>() ?? 0}步骤 / {change["operationCount"]?.Value<int>() ?? 0}指令";
                                rowColor = UiPalette.WarningSoft;
                                break;
                            case "variable.create":
                                type = "创建变量";
                                obj = change["name"]?.Value<string>() ?? "";
                                location = string.Equals(
                                    change["scope"]?.Value<string>(), "process", StringComparison.Ordinal)
                                    ? $"process / {change["ownerProcName"]?.Value<string>() ?? change["ownerProcId"]?.Value<string>() ?? ""}"
                                    : change["scope"]?.Value<string>() ?? "";
                                field = $"{change["valueType"]?.Value<string>() ?? ""} / 槽位{change["index"]?.Value<int>() ?? -1}";
                                newVal = FormatJsonValue(change["newValue"]);
                                rowColor = UiPalette.SuccessSoft;
                                break;
                            case "variable.update":
                                type = "更新变量";
                                obj = change["name"]?.Value<string>() ?? "";
                                location = string.Equals(
                                    change["scope"]?.Value<string>(), "process", StringComparison.Ordinal)
                                    ? $"process / {change["ownerProcName"]?.Value<string>() ?? change["ownerProcId"]?.Value<string>() ?? ""}"
                                    : change["scope"]?.Value<string>() ?? "";
                                field = $"{change["valueType"]?.Value<string>() ?? ""} / 槽位{change["index"]?.Value<int>() ?? -1}";
                                oldVal = FormatJsonValue(change["oldValue"]);
                                newVal = FormatJsonValue(change["newValue"]);
                                rowColor = UiPalette.WarningSoft;
                                break;
                            case "variable.delete":
                                type = "删除变量";
                                obj = change["name"]?.Value<string>() ?? "";
                                location = string.Equals(
                                    change["scope"]?.Value<string>(), "process", StringComparison.Ordinal)
                                    ? $"process / {change["ownerProcName"]?.Value<string>() ?? change["ownerProcId"]?.Value<string>() ?? ""}"
                                    : change["scope"]?.Value<string>() ?? "";
                                field = $"{change["valueType"]?.Value<string>() ?? ""} / 槽位{change["index"]?.Value<int>() ?? -1}";
                                oldVal = "已定义";
                                newVal = "已删除";
                                rowColor = UiPalette.DangerSoft;
                                break;
                        }

                        int rowIndex = dgv.Rows.Add(type, location, obj, field, oldVal, newVal);
                        dgv.Rows[rowIndex].DefaultCellStyle.BackColor = rowColor;
                    }
                }

                // 消息区
                TextBox txtMessages = new TextBox
                {
                    Dock = hasChanges ? DockStyle.Bottom : DockStyle.Fill,
                    Height = hasChanges ? 82 : 0,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    ReadOnly = true,
                    BackColor = UiPalette.SurfaceStrong,
                    ForeColor = UiPalette.TextPrimary,
                    Font = new Font("微软雅黑", 10F),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(10)
                };
                if (messages != null && messages.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var msg in messages)
                    {
                        sb.AppendLine(msg.ToString());
                    }
                    txtMessages.Text = sb.ToString().TrimEnd();
                }

                // 按钮区
                Panel btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = UiPalette.SurfaceStrong };
                Button btnReject = new Button
                {
                    Text = "取消",
                    DialogResult = DialogResult.No,
                    Size = new Size(96, 36),
                    BackColor = UiPalette.SurfaceStrong,
                    ForeColor = UiPalette.TextSecondary,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F),
                    Anchor = AnchorStyles.Right
                };
                btnReject.FlatAppearance.BorderColor = UiPalette.Stroke;
                Button btnConfirm = new Button
                {
                    Text = "确认并继续",
                    DialogResult = DialogResult.Yes,
                    Size = new Size(128, 36),
                    BackColor = UiPalette.Success,
                    ForeColor = UiPalette.TextInverse,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                    Anchor = AnchorStyles.Right
                };
                btnConfirm.FlatAppearance.BorderSize = 0;
                btnPanel.Controls.Add(btnConfirm);
                btnPanel.Controls.Add(btnReject);

                // 按顺序添加控件（WinForms docking: 后添加的先停靠）
                if (hasChanges)
                {
                    dlg.Controls.Add(dgv);
                }
                dlg.Controls.Add(txtMessages);
                dlg.Controls.Add(btnPanel);
                dlg.Controls.Add(infoPanel);
                dlg.Controls.Add(headerPanel);

                dlg.Resize += (s, e) =>
                {
                    btnConfirm.Location = new Point(btnPanel.Width - btnConfirm.Width - 18, 13);
                    btnReject.Location = new Point(btnConfirm.Left - btnReject.Width - 10, 13);
                };
                // 初始定位按钮
                dlg.Shown += (s, e) =>
                {
                    btnConfirm.Location = new Point(btnPanel.Width - btnConfirm.Width - 18, 13);
                    btnReject.Location = new Point(btnConfirm.Left - btnReject.Width - 10, 13);
                    dlg.BringToFront();
                    dlg.Activate();
                };

                return dlg.ShowDialog(this);
            }
        }

        // 工具调用权限对话框：展示工具名和参数，让用户决定是否允许执行。
        private DialogResult ShowPermissionApprovalDialog(string toolName, string toolTitle, JObject arguments)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "EW-AI 工具调用确认";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Width = 600;
                dlg.Height = 400;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = UiPalette.SurfaceStrong;
                dlg.Font = new Font("微软雅黑", 9F);

                // 标题栏
                Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = UiPalette.BrandPressed };
                headerPanel.Controls.Add(new Label
                {
                    Text = "  EW-AI 请求执行工具",
                    Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                    ForeColor = UiPalette.TextInverse,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                });

                // 工具信息
                Panel infoPanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(12, 8, 12, 8) };
                Label lblToolName = new Label
                {
                    Text = $"工具：{toolName}",
                    Font = new Font("Consolas", 10F, FontStyle.Bold),
                    ForeColor = UiPalette.TextPrimary,
                    Dock = DockStyle.Top,
                    Height = 22
                };
                Label lblToolTitle = new Label
                {
                    Text = string.IsNullOrWhiteSpace(toolTitle) ? "" : $"说明：{toolTitle}",
                    Font = new Font("微软雅黑", 9F),
                    ForeColor = UiPalette.TextSecondary,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                infoPanel.Controls.Add(lblToolTitle);
                infoPanel.Controls.Add(lblToolName);

                // 参数表格
                DataGridView dgv = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    ReadOnly = true,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = UiPalette.SurfaceStrong,
                    BorderStyle = BorderStyle.None,
                    ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                    {
                        BackColor = UiPalette.SurfaceSubtle,
                        Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                        Alignment = DataGridViewContentAlignment.MiddleCenter
                    },
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Font = new Font("Consolas", 9F),
                        WrapMode = DataGridViewTriState.True
                    },
                    RowHeadersVisible = false,
                    GridColor = UiPalette.Stroke
                };
                dgv.Columns.Add("colKey", "参数名");
                dgv.Columns.Add("colVal", "值");
                dgv.Columns["colKey"].DefaultCellStyle.BackColor = UiPalette.Input;

                if (arguments != null)
                {
                    FlattenArguments(dgv, arguments, "");
                }

                // 按钮区
                Panel btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = UiPalette.Background };
                Button btnReject = new Button
                {
                    Text = "✗ 拒绝",
                    DialogResult = DialogResult.No,
                    Size = new Size(100, 32),
                    BackColor = UiPalette.Danger,
                    ForeColor = UiPalette.TextInverse,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F),
                    Anchor = AnchorStyles.Right
                };
                btnReject.FlatAppearance.BorderSize = 0;
                Button btnAllow = new Button
                {
                    Text = "✓ 允许执行",
                    DialogResult = DialogResult.Yes,
                    Size = new Size(120, 32),
                    BackColor = UiPalette.Success,
                    ForeColor = UiPalette.TextInverse,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                    Anchor = AnchorStyles.Right
                };
                btnAllow.FlatAppearance.BorderSize = 0;
                btnPanel.Controls.Add(btnAllow);
                btnPanel.Controls.Add(btnReject);

                dlg.Controls.Add(dgv);
                dlg.Controls.Add(btnPanel);
                dlg.Controls.Add(infoPanel);
                dlg.Controls.Add(headerPanel);

                dlg.Resize += (s, e) =>
                {
                    btnAllow.Location = new Point(btnPanel.Width - btnAllow.Width - 16, 7);
                    btnReject.Location = new Point(btnAllow.Left - btnReject.Width - 10, 7);
                };
                dlg.Shown += (s, e) =>
                {
                    btnAllow.Location = new Point(btnPanel.Width - btnAllow.Width - 16, 7);
                    btnReject.Location = new Point(btnAllow.Left - btnReject.Width - 10, 7);
                };

                return dlg.ShowDialog(this);
            }
        }

        // 将嵌套的 JSON 参数展平到 DataGridView 中（递归）。
        private static void FlattenArguments(DataGridView dgv, JToken token, string prefix)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    string key = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
                    if (prop.Value is JObject || prop.Value is JArray)
                    {
                        FlattenArguments(dgv, prop.Value, key);
                    }
                    else
                    {
                        dgv.Rows.Add(key, FormatJsonValue(prop.Value));
                    }
                }
            }
            else if (token is JArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    FlattenArguments(dgv, arr[i], $"{prefix}[{i}]");
                }
            }
            else
            {
                dgv.Rows.Add(prefix, FormatJsonValue(token));
            }
        }

        // 格式化 JSON 值为可读字符串。
        private static string FormatJsonValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "—";
            if (token.Type == JTokenType.String) return token.Value<string>() ?? "";
            if (token.Type == JTokenType.Integer) return token.Value<long>().ToString();
            if (token.Type == JTokenType.Float) return token.Value<double>().ToString("G");
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        #endregion
    }
}
