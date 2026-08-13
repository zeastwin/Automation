using System.Drawing;
using System.Windows.Forms;

namespace Automation.Hmi;

internal sealed class LegacyValueInputDialog : Form
{
	private readonly TextBox input;

	internal string Value => input.Text;

	internal LegacyValueInputDialog(string name, string value)
	{
		Text = name;
		base.ClientSize = new Size(460, 130);
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MinimizeBox = false;
		base.MaximizeBox = false;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			ColumnCount = 1,
			RowCount = 2,
			Dock = DockStyle.Fill,
			Padding = new Padding(12)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		input = new TextBox
		{
			Dock = DockStyle.Fill,
			Text = (value ?? string.Empty)
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.RightToLeft
		};
		Button button = new Button
		{
			DialogResult = DialogResult.Cancel,
			Text = "取消"
		};
		Button button2 = new Button
		{
			DialogResult = DialogResult.OK,
			Text = "确定"
		};
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(button2);
		tableLayoutPanel.Controls.Add(input, 0, 0);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 1);
		base.Controls.Add(tableLayoutPanel);
		base.AcceptButton = button2;
		base.CancelButton = button;
	}
}


