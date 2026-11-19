using System;
using System.Drawing;
using System.Windows.Forms;

namespace HalfGripModsInstaller
{
	/// <summary>
	/// The question before one mod is taken out of the game: what goes, a warning when other installed mods need it, and a box for its
	/// settings and saved data (off by default, so a reinstall finds them again).
	/// </summary>
	internal sealed class UninstallDialog : Form
	{
		private readonly CheckBox data;

		/// <summary>The player ticked "also delete its settings and saved data".</summary>
		internal bool AlsoData => data.Checked;

		internal UninstallDialog(string name, string[] dependents)
		{
			Text = "Uninstall " + name;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			BackColor = Theme.Panel;
			ForeColor = Theme.Ink;
			Font = Theme.Body(10f);
			AutoScaleMode = AutoScaleMode.Dpi;
			ClientSize = new Size(S(470), S(200));

			FlowLayoutPanel flow = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Padding = new Padding(S(18), S(16), S(18), 0)
			};
			int width = S(430);
			flow.Controls.Add(Line("Take " + name + " out of the game?", Font, Theme.White, width));
			flow.Controls.Add(Line("Its folder is deleted. Other mods, HalfGripTools and your saves are not touched. You can install it again from the list.", Font, Theme.Soft, width));
			if (dependents.Length > 0)
			{
				flow.Controls.Add(Line("Still installed and needing it: " + string.Join(", ", dependents) + ". They will not work without it.", Font, Theme.Amber, width));
			}
			data = new CheckBox
			{
				Text = "Also delete its settings and saved data",
				AutoSize = true,
				ForeColor = Theme.Ink,
				BackColor = Theme.Panel,
				Margin = new Padding(0, S(8), 0, S(4)),
				Checked = false
			};
			flow.Controls.Add(data);

			FlowLayoutPanel buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Bottom,
				FlowDirection = FlowDirection.RightToLeft,
				Height = S(54),
				Padding = new Padding(S(14), S(8), S(14), S(8))
			};
			Button cancel = Make("CANCEL", Theme.Row, DialogResult.Cancel);
			Button go = Make("UNINSTALL", Theme.Red, DialogResult.OK);
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(go);
			Controls.Add(flow);
			Controls.Add(buttons);
			AcceptButton = cancel;
			CancelButton = cancel;
		}

		private static int S(int value)
		{
			return (int)Math.Round(value * Theme.Scale);
		}

		private static Label Line(string text, Font font, Color color, int width)
		{
			return new Label
			{
				Text = text,
				Font = font,
				ForeColor = color,
				BackColor = Theme.Panel,
				AutoSize = true,
				MaximumSize = new Size(width, 0),
				Margin = new Padding(0, 0, 0, S(8))
			};
		}

		private static Button Make(string text, Color back, DialogResult result)
		{
			Button b = new Button
			{
				Text = text,
				DialogResult = result,
				FlatStyle = FlatStyle.Flat,
				BackColor = back,
				ForeColor = Theme.White,
				Size = new Size(S(120), S(36)),
				UseVisualStyleBackColor = false
			};
			b.FlatAppearance.BorderColor = Theme.Line;
			return b;
		}
	}
}
