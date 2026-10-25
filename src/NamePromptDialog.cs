using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class NamePromptDialog : Form
    {
        private readonly TextBox txt;
        public string Value { get { return txt.Text.Trim(); } }

        public NamePromptDialog(string title, string prompt, string initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(324, 104);
            Font = new Font("Tahoma", 8.25f);
            BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB);

            var lbl = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
            txt = new TextBox { Left = 12, Top = 32, Width = 300, Text = initial ?? "" };

            var ok = new Button
            {
                Text = "OK", Left = 156, Top = 66, Width = 75, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0xEC, 0xEC, 0xEE),
                DialogResult = DialogResult.OK
            };
            var cancel = new Button
            {
                Text = "Cancel", Left = 237, Top = 66, Width = 75, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0xEC, 0xEC, 0xEE),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(lbl);
            Controls.Add(txt);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
