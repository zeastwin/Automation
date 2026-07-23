// 模块：核心测试 / 数据结构。
// 职责范围：验证数据结构树刷新时保留用户展开状态并更新字段数据。

using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class DataStructTreeRefreshTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshTree_PreservesExpandedNodesAndReloadsFieldValues()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    DataStructStore store = main.Runtime.Stores.DataStructures;
                    Assert.IsTrue(store.AddStruct("测试结构", out string addStructError), addStructError);
                    Assert.IsTrue(
                        store.CreateItem(0, "测试项", 0, out int itemIndex, out string createItemError),
                        createItemError);
                    Assert.IsTrue(
                        store.AddField(
                            0,
                            itemIndex,
                            "状态",
                            DataStructValueType.Text,
                            "刷新前",
                            -1,
                            out int fieldIndex,
                            out string addFieldError),
                        addFieldError);

                    FrmDataStruct form = main.frmdataStruct;
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-10000, -10000);
                    form.CreateControl();
                    form.treeView1.CreateControl();
                    form.Show();
                    Application.DoEvents();
                    form.RefreshDataSturctTree();

                    TreeNode structNode = form.treeView1.Nodes[0];
                    TreeNode itemNode = structNode.Nodes[0];
                    structNode.Expand();
                    itemNode.Expand();
                    form.treeView1.SelectedNode = itemNode;
                    TreeNode fieldNode = itemNode.Nodes[0];
                    Assert.IsTrue(structNode.IsExpanded);
                    Assert.IsTrue(itemNode.IsExpanded);

                    Assert.IsTrue(
                        store.SetFieldValue(
                            0,
                            itemIndex,
                            fieldIndex,
                            DataStructValueType.Text,
                            "刷新后",
                            out string setValueError),
                        setValueError);
                    form.RefreshDataSturctTree();

                    TreeNode refreshedStructNode = form.treeView1.Nodes[0];
                    TreeNode refreshedItemNode = refreshedStructNode.Nodes[0];
                    Assert.AreSame(
                        structNode,
                        refreshedStructNode,
                        "结构内容变化时也应原位更新已有结构节点。");
                    Assert.AreSame(
                        itemNode,
                        refreshedItemNode,
                        "结构内容变化时也应原位更新已有数据项节点。");
                    Assert.AreSame(
                        fieldNode,
                        refreshedItemNode.Nodes[0],
                        "字段值变化时应原位更新字段节点，避免树整体闪烁。");
                    Assert.IsTrue(refreshedStructNode.IsExpanded, "结构体节点刷新后应保持展开。");
                    Assert.IsTrue(refreshedItemNode.IsExpanded, "数据项节点刷新后应保持展开。");
                    Assert.AreSame(
                        refreshedItemNode,
                        form.treeView1.SelectedNode,
                        "刷新后应恢复原选中数据项。");
                    Assert.AreEqual(1, refreshedItemNode.Nodes.Count);
                    StringAssert.Contains(
                        refreshedItemNode.Nodes[0].Text,
                        "刷新后",
                        "已展开数据项应显示最新字段值。");
                }
            }, TimeSpan.FromSeconds(20));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void RefreshTree_WhenDataIsUnchanged_KeepsExistingNodes()
        {
            StaTestRunner.Run(() =>
            {
                using (var directory = new TemporaryDirectory())
                using (var main = new FrmMain(new PlatformRuntime(directory.FullPath)))
                {
                    DataStructStore store = main.Runtime.Stores.DataStructures;
                    Assert.IsTrue(
                        store.AddStruct("稳定结构", out string error),
                        error);

                    FrmDataStruct form = main.frmdataStruct;
                    form.CreateControl();
                    form.treeView1.CreateControl();
                    form.RefreshDataSturctTree();
                    TreeNode originalNode = form.treeView1.Nodes[0];
                    originalNode.Expand();
                    form.treeView1.SelectedNode = originalNode;

                    form.RefreshDataSturctTree();

                    Assert.AreSame(
                        originalNode,
                        form.treeView1.Nodes[0],
                        "数据版本未变化时不应重建树节点，避免展开树闪动或跳位。");
                    Assert.AreSame(originalNode, form.treeView1.SelectedNode);
                    Assert.IsTrue(originalNode.IsExpanded);
                }
            }, TimeSpan.FromSeconds(20));
        }
    }
}
