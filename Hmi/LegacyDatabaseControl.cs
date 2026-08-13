using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

// 模块：平台内置 HMI / 旧项目 Database 调试页。
// 职责范围：复用旧 DataBaseView 的数据库、表、字段查询和编辑逻辑。

namespace Automation.Hmi
{
    internal sealed partial class LegacyDatabaseControl : UserControl
    {
        private LegacyDatabaseService databaseService;
        private DataTable currentData;
        private bool profilesLoaded;
        private bool changingSelection;

        internal LegacyDatabaseControl()
        {
            InitializeComponent();
        }

        private void DataSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadTables();
        }

        private void TableSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadColumnsAndQuery();
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            QuerySelectedTable();
        }

        private void FilterGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0
                && filterGrid.Rows[e.RowIndex].DataBoundItem is LegacyDatabaseTableRow row)
            {
                tableSelector.SelectedItem = row.TableName;
            }
        }

        private void RejectButton_Click(object sender, EventArgs e)
        {
            QuerySelectedTable();
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            ApplyChanges();
        }

        internal void RefreshView()
        {
            if (databaseService == null)
            {
                status.Text = "数据库服务未装载";
                return;
            }
            if (!profilesLoaded)
            {
                LoadProfiles();
            }
        }

        internal void AttachDatabaseService(LegacyDatabaseService service)
        {
            databaseService = service;
            profilesLoaded = false;
            if (IsHandleCreated)
            {
                LoadProfiles();
            }
        }

        private void LoadProfiles()
        {
            profilesLoaded = true;
            changingSelection = true;
            try
            {
                dataSource.Items.Clear();
                if (databaseService == null)
                {
                    status.Text = "数据库服务未装载";
                    return;
                }
                IReadOnlyList<LegacyDatabaseProfile> profiles =
                    databaseService.GetProfiles();
                dataSource.Items.AddRange(profiles.Cast<object>().ToArray());
                if (dataSource.Items.Count == 0)
                {
                    tableSelector.Items.Clear();
                    queryField.Items.Clear();
                    dataGrid.DataSource = null;
                    filterGrid.DataSource = null;
                    status.Text =
                        "未配置数据库服务器/数据库名；旧项目默认变量为数据库服务器0、数据库名0。";
                    return;
                }
                dataSource.SelectedIndex = 0;
            }
            finally
            {
                changingSelection = false;
            }
            LoadTables();
        }

        private void LoadTables()
        {
            if (changingSelection
                || databaseService == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile))
            {
                return;
            }
            changingSelection = true;
            try
            {
                IReadOnlyList<string> tables = databaseService.GetTables(profile);
                tableSelector.Items.Clear();
                tableSelector.Items.AddRange(tables.Cast<object>().ToArray());
                filterGrid.DataSource = new BindingList<LegacyDatabaseTableRow>(
                    tables.Select(name => new LegacyDatabaseTableRow { TableName = name })
                        .ToList());
                tableSelector.SelectedIndex = tableSelector.Items.Count > 0 ? 0 : -1;
                status.Text = tables.Count == 0
                    ? "数据库中没有数据表。"
                    : "已连接：" + profile;
            }
            catch (Exception ex)
            {
                tableSelector.Items.Clear();
                queryField.Items.Clear();
                dataGrid.DataSource = null;
                status.Text = "数据库连接失败：" + ex.Message;
            }
            finally
            {
                changingSelection = false;
            }
            LoadColumnsAndQuery();
        }

        private void LoadColumnsAndQuery()
        {
            if (changingSelection
                || databaseService == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile)
                || !(tableSelector.SelectedItem is string table))
            {
                return;
            }
            changingSelection = true;
            try
            {
                IReadOnlyList<string> columns =
                    databaseService.GetColumns(profile, table);
                queryField.Items.Clear();
                queryField.Items.AddRange(columns.Cast<object>().ToArray());
                queryField.SelectedIndex = queryField.Items.Count > 0 ? 0 : -1;
            }
            catch (Exception ex)
            {
                status.Text = "字段读取失败：" + ex.Message;
                return;
            }
            finally
            {
                changingSelection = false;
            }
            QuerySelectedTable();
        }

        private void QuerySelectedTable()
        {
            if (databaseService == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile)
                || !(tableSelector.SelectedItem is string table))
            {
                return;
            }
            try
            {
                currentData = databaseService.Query(
                    profile,
                    table,
                    queryField.SelectedItem?.ToString(),
                    queryValue.Text.Trim());
                dataGrid.DataSource = currentData;
                status.Text =
                    $"已加载 {currentData.Rows.Count} 行；单次最多显示 500 行。";
            }
            catch (Exception ex)
            {
                dataGrid.DataSource = null;
                currentData = null;
                status.Text = "查询失败：" + ex.Message;
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "数据库查询失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ApplyChanges()
        {
            if (databaseService == null
                || currentData == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile)
                || !(tableSelector.SelectedItem is string table))
            {
                return;
            }
            if (MessageBox.Show(
                FindForm(),
                "确定将新增、修改和删除提交到数据库表“" + table + "”吗？",
                "应用数据库修改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                dataGrid.EndEdit();
                int affected = databaseService.ApplyChanges(
                    profile,
                    table,
                    currentData);
                status.Text =
                    "数据库修改已提交，影响 "
                    + affected.ToString(CultureInfo.InvariantCulture)
                    + " 行。";
                QuerySelectedTable();
            }
            catch (Exception ex)
            {
                status.Text = "提交失败：" + ex.Message;
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "数据库修改失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private sealed class LegacyDatabaseTableRow
        {
            [DisplayName("TableName")]
            public string TableName { get; set; }
        }
    }
}


