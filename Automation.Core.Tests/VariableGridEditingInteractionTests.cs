// 模块：核心测试 / 变量表。
// 职责范围：验证变量名称、当前值的鼠标编辑入口和关键列宽。

using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class VariableGridEditingInteractionTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void EditableNameAndCurrentValueCells_LeftDoubleClick_ImmediatelyBeginEdit()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmValue
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(900, 520)
                })
                {
                    DataGridView grid = form.dgvValue;
                    form.CreateControl();
                    grid.CreateControl();
                    grid.Rows.Add(1, "测试变量", "double", "12", "");

                    Assert.AreEqual(88, grid.Columns[2].Width,
                        "类型列应使用加宽后的运行时列宽。");
                    foreach (int columnIndex in new[] { 1, 3 })
                    {
                        grid.CurrentCell = grid.Rows[0].Cells[0];
                        Assert.IsFalse(grid.IsCurrentCellInEditMode);

                        InvokeCellMouseDoubleClick(
                            form,
                            grid,
                            columnIndex,
                            0,
                            MouseButtons.Left);

                        Assert.AreSame(grid.Rows[0].Cells[columnIndex], grid.CurrentCell);
                        Assert.IsTrue(grid.IsCurrentCellInEditMode,
                            $"第{columnIndex}列应在标准双击后立即进入编辑态。");
                        Assert.IsNotNull(grid.EditingControl);
                        grid.CancelEdit();
                        grid.CurrentCell = null;
                    }

                    grid.Rows[0].Cells[1].ReadOnly = true;
                    grid.CurrentCell = grid.Rows[0].Cells[0];
                    InvokeCellMouseDoubleClick(form, grid, 1, 0, MouseButtons.Left);
                    Assert.AreSame(grid.Rows[0].Cells[0], grid.CurrentCell,
                        "只读变量名称不应进入编辑态。");
                    Assert.IsFalse(grid.IsCurrentCellInEditMode);
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void VariableToolbar_UsesCompactAlignedButtonsAndSidePanel()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmValue
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000),
                    ClientSize = new Size(1680, 720)
                })
                {
                    form.CreateControl();
                    InvokePrivateMethod(form, "SetDefaultStructPanelRatio");

                    Panel toolbar = ReadPrivateField<Panel>(form, "panel1");
                    SplitContainer split = ReadPrivateField<SplitContainer>(form, "splitContainerMain");
                    Assert.AreEqual(44, toolbar.Height,
                        "变量工具栏应压缩垂直空白，避免占用表格空间。");
                    Assert.AreEqual(320, split.Panel2.Width,
                        "右侧常用变量区域默认只保留实际需要的紧凑宽度。");

                    foreach (string fieldName in new[]
                    {
                        "btnMonitorAdd",
                        "btnAddCommon",
                        "btnMonitor",
                        "btnCopy",
                        "btnPaste",
                        "btnClearData",
                        "btnSearch",
                        "btnShowCommon",
                        "btnShowDataStruct"
                    })
                    {
                        Button button = ReadPrivateField<Button>(form, fieldName);
                        Assert.AreEqual(32, button.Height,
                            $"{button.Text}按钮应使用统一高度并在工具栏内对齐。");
                    }

                    Button clearSearch = ReadPrivateField<Button>(form, "btnClearSearch");
                    Assert.IsFalse(clearSearch.Visible,
                        "搜索内容为空时不应显示清除按钮或遗留背景色块。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        private static T ReadPrivateField<T>(FrmValue form, string fieldName)
            where T : class
        {
            FieldInfo field = typeof(FrmValue).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"未找到字段：{fieldName}");
            return field.GetValue(form) as T
                ?? throw new InvalidOperationException($"字段类型不正确：{fieldName}");
        }

        private static void InvokePrivateMethod(FrmValue form, string methodName)
        {
            MethodInfo method = typeof(FrmValue).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"未找到方法：{methodName}");
            method.Invoke(form, null);
        }

        private static void InvokeCellMouseDoubleClick(
            FrmValue form,
            DataGridView grid,
            int columnIndex,
            int rowIndex,
            MouseButtons button)
        {
            MethodInfo handler = typeof(FrmValue).GetMethod(
                "dgvValue_CellMouseDoubleClick",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("未找到变量表双击处理器。");
            handler.Invoke(form, new object[]
            {
                grid,
                new DataGridViewCellMouseEventArgs(
                    columnIndex,
                    rowIndex,
                    4,
                    4,
                    new MouseEventArgs(button, 2, 4, 4, 0))
            });
        }
    }
}
