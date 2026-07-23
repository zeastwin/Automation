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
