using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi;

internal sealed class LegacyFileBrowserControl : UserControl
{
	private const string LazyNodeMarker = "__lazy__";

	private readonly string rootVariableName;

	private readonly string filterVariableName;

	private readonly string defaultRoot;

	private readonly bool openBinaryFiles;

	private readonly TreeView directoryTree;

	private readonly TreeView fileTree;

	private readonly RichTextBox preview;

	private readonly ContextMenuStrip directoryMenu;

	private IAutomationPlatform platform;

	private bool loaded;

	private string extensionFilter = string.Empty;

	internal LegacyFileBrowserControl(string rootVariableName, string filterVariableName, string defaultRoot, bool openBinaryFiles)
	{
		this.rootVariableName = rootVariableName;
		this.filterVariableName = filterVariableName;
		this.defaultRoot = defaultRoot;
		this.openBinaryFiles = openBinaryFiles;
		BackColor = Color.White;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 2,
			RowCount = 1,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			ColumnCount = 2,
			RowCount = 1,
			Dock = DockStyle.Fill
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		directoryTree = CreateTree();
		fileTree = CreateTree();
		preview = new RichTextBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 12f),
			ReadOnly = true,
			WordWrap = false
		};
		directoryMenu = new ContextMenuStrip();
		directoryMenu.Items.Add("更新", null, delegate
		{
			Reload();
		});
		directoryTree.ContextMenuStrip = directoryMenu;
		directoryTree.BeforeExpand += DirectoryTree_BeforeExpand;
		directoryTree.AfterSelect += DirectoryTree_AfterSelect;
		directoryTree.NodeMouseDoubleClick += DirectoryTree_NodeMouseDoubleClick;
		fileTree.AfterSelect += FileTree_AfterSelect;
		fileTree.NodeMouseDoubleClick += FileTree_NodeMouseDoubleClick;
		tableLayoutPanel2.Controls.Add(directoryTree, 0, 0);
		tableLayoutPanel2.Controls.Add(fileTree, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(preview, 1, 0);
		base.Controls.Add(tableLayoutPanel);
	}

	internal void AttachPlatform(IAutomationPlatform platform)
	{
		this.platform = platform;
		Reload();
	}

	internal void EnsureLoaded()
	{
		if (!loaded)
		{
			Reload();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			directoryMenu.Dispose();
		}
		base.Dispose(disposing);
	}

	private void Reload()
	{
		directoryTree.Nodes.Clear();
		fileTree.Nodes.Clear();
		preview.Clear();
		extensionFilter = ReadConfiguredValue(filterVariableName);
		string text = ReadConfiguredValue(rootVariableName);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = defaultRoot;
		}
		foreach (string item in (from path in text.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries)
			select path.Trim() into path
			where path.Length > 0
			select path).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			AddRoot(item);
		}
		loaded = true;
		if (directoryTree.Nodes.Count == 0)
		{
			preview.Text = "未找到可用目录：" + text;
		}
	}

	private void AddRoot(string rootPath)
	{
		try
		{
			string fullPath = Path.GetFullPath(rootPath);
			TreeNode treeNode = new TreeNode(fullPath)
			{
				Tag = fullPath
			};
			if (Directory.Exists(fullPath) && HasSubdirectories(fullPath))
			{
				treeNode.Nodes.Add(new TreeNode("__lazy__"));
			}
			directoryTree.Nodes.Add(treeNode);
		}
		catch (Exception ex)
		{
			directoryTree.Nodes.Add(new TreeNode(rootPath + "（" + ex.Message + "）"));
		}
	}

	private void DirectoryTree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
	{
		if (e.Node.Nodes.Count != 1 || !string.Equals(e.Node.Nodes[0].Text, "__lazy__", StringComparison.Ordinal) || !(e.Node.Tag is string path))
		{
			return;
		}
		e.Node.Nodes.Clear();
		try
		{
			foreach (string item in Directory.GetDirectories(path).OrderBy((string item) => item, StringComparer.OrdinalIgnoreCase))
			{
				TreeNode treeNode = new TreeNode(Path.GetFileName(item))
				{
					Tag = item
				};
				if (HasSubdirectories(item))
				{
					treeNode.Nodes.Add(new TreeNode("__lazy__"));
				}
				e.Node.Nodes.Add(treeNode);
			}
		}
		catch (Exception ex)
		{
			e.Node.Nodes.Add(new TreeNode("无法读取：" + ex.Message));
		}
	}

	private void DirectoryTree_AfterSelect(object sender, TreeViewEventArgs e)
	{
		fileTree.Nodes.Clear();
		if (!(e.Node.Tag is string text) || !Directory.Exists(text))
		{
			return;
		}
		try
		{
			foreach (string item in Directory.GetFiles(text).Where(IsVisibleFile).OrderBy((string item) => item, StringComparer.OrdinalIgnoreCase)
				.Take(5000))
			{
				fileTree.Nodes.Add(new TreeNode(Path.GetFileName(item))
				{
					Tag = item
				});
			}
			preview.Text = $"目录：{text}\r\n文件数：{fileTree.Nodes.Count}";
		}
		catch (Exception ex)
		{
			preview.Text = ex.Message;
		}
	}

	private void FileTree_AfterSelect(object sender, TreeViewEventArgs e)
	{
		if (e.Node.Tag is string path)
		{
			PreviewFile(path);
		}
	}

	private void DirectoryTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
	{
		if (e.Node.Tag is string path && Directory.Exists(path))
		{
			OpenPath(path);
		}
	}

	private void FileTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
	{
		if (e.Node.Tag is string path && File.Exists(path) && (openBinaryFiles || !IsTextFile(path)))
		{
			OpenPath(path);
		}
	}

	private void PreviewFile(string path)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			if (!IsTextFile(path))
			{
				preview.Text = $"文件：{fileInfo.FullName}\r\n大小：{fileInfo.Length:N0} Bytes\r\n修改时间：{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\r\n\r\n双击文件可使用系统程序打开。";
				return;
			}
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
			char[] array = new char[300000];
			int length = streamReader.ReadBlock(array, 0, array.Length);
			preview.Text = new string(array, 0, length);
			if (!streamReader.EndOfStream)
			{
				preview.AppendText("\r\n\r\n……文件较大，仅显示前 300000 个字符……");
			}
		}
		catch (Exception ex)
		{
			preview.Text = ex.Message;
		}
	}

	private string ReadConfiguredValue(string variableName)
	{
		if (platform == null || string.IsNullOrWhiteSpace(variableName) || !platform.Values.TryGet(variableName, out var value, out var _) || value == null)
		{
			return string.Empty;
		}
		return value.Value ?? string.Empty;
	}

	private bool IsVisibleFile(string path)
	{
		if (string.IsNullOrWhiteSpace(extensionFilter))
		{
			return true;
		}
		string extension = Path.GetExtension(path).TrimStart('.');
		return (from item in extensionFilter.Split(new char[4] { ';', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
			select item.Trim().TrimStart('*', '.')).Any((string item) => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase));
	}

	private static bool HasSubdirectories(string path)
	{
		try
		{
			return Directory.EnumerateDirectories(path).Take(1).Any();
		}
		catch
		{
			return false;
		}
	}

	private static bool IsTextFile(string path)
	{
		string extension = Path.GetExtension(path);
		return new string[8] { ".txt", ".log", ".csv", ".json", ".xml", ".md", ".ini", ".config" }.Contains(extension, StringComparer.OrdinalIgnoreCase);
	}

	private static void OpenPath(string path)
	{
		try
		{
			Process.Start(path);
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private static TreeView CreateTree()
	{
		return new TreeView
		{
			Dock = DockStyle.Fill,
			Font = new Font("宋体", 12f),
			HideSelection = false
		};
	}
}


