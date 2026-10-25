using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class CockpitPanel : LiteFrame
    {
        private readonly NetConfig cfg;
        private readonly Action onApply;

        private CheckBox cEnable;
        private TrackBar tSampleRate;
        private Label lblRateValue;
        private Label lblStatus;
        private RoundButton btnStart, btnStop, btnOpen;

        private CockpitWindow cockpitWindow;

        public CockpitPanel(NetConfig config, Action applyCallback)
            : base("Instruction Cockpit")
        {
            cfg = config;
            onApply = applyCallback;

            Size = new Size(560, 480);
            Font = new Font("Tahoma", 8.25f);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(245, 245, 246),
                Padding = new Padding(14, 10, 14, 10)
            };
            ClientArea.Controls.Add(scroll);

            int y = 6;

            y = Group(scroll, "Frida tracing", y);
            cEnable = Check(scroll, "Enable instruction-level tracing (attaches to Chromium)",
                cfg.EnableFridaTracing, ref y);

            var desc = new Label
            {
                Text = "Captures CPU instructions from the running browser engine and renders\n" +
                       "them as a live 3D topological surface. Loops, branches, recursion, and\n" +
                       "memory writes deform the geometry.",
                Left = 6, Top = y, Width = 500, Height = 50,
                ForeColor = Color.FromArgb(0x60, 0x60, 0x60),
                Font = new Font("Tahoma", 8f)
            };
            scroll.Controls.Add(desc);
            y += 56;

            y = Group(scroll, "Sample rate", y);

            var rateRow = new Panel { Left = 6, Top = y, Width = 500, Height = 40, BackColor = Color.Transparent };
            var rateLabel = new Label
            {
                Text = "Capture every Nth instruction:",
                Left = 0, Top = 8, Width = 180, AutoSize = false,
                ForeColor = Color.Black, Font = new Font("Tahoma", 8.25f)
            };
            rateRow.Controls.Add(rateLabel);

            tSampleRate = new TrackBar
            {
                Left = 180, Top = 0, Width = 220, Minimum = 1, Maximum = 100,
                Value = Math.Max(1, Math.Min(cfg.FridaSampleRate, 100)),
                TickFrequency = 10, BackColor = Color.FromArgb(245, 245, 246)
            };
            tSampleRate.ValueChanged += (s, e) =>
            {
                lblRateValue.Text = tSampleRate.Value.ToString();
            };
            rateRow.Controls.Add(tSampleRate);

            lblRateValue = new Label
            {
                Text = tSampleRate.Value.ToString(),
                Left = 405, Top = 8, Width = 40, AutoSize = false,
                ForeColor = Color.Black, Font = new Font("Tahoma", 8.25f, FontStyle.Bold)
            };
            rateRow.Controls.Add(lblRateValue);
            scroll.Controls.Add(rateRow);
            y += 46;

            y = Group(scroll, "Control", y);

            var ctrlRow = new Panel { Left = 6, Top = y, Width = 500, Height = 36, BackColor = Color.Transparent };
            btnStart = new RoundButton { Text = "Start tracing", Width = 110, Height = 26, Left = 0, Top = 4 };
            btnStart.Click += (s, e) => StartTracing();
            ctrlRow.Controls.Add(btnStart);

            btnStop = new RoundButton { Text = "Stop", Width = 70, Height = 26, Left = 118, Top = 4, Enabled = false };
            btnStop.Click += (s, e) => StopTracing();
            ctrlRow.Controls.Add(btnStop);

            btnOpen = new RoundButton { Text = "Open cockpit window", Width = 150, Height = 26, Left = 196, Top = 4 };
            btnOpen.Click += (s, e) => OpenCockpitWindow();
            ctrlRow.Controls.Add(btnOpen);
            scroll.Controls.Add(ctrlRow);
            y += 42;

            lblStatus = new Label
            {
                Text = "Status: idle",
                Left = 6, Top = y, Width = 500, AutoSize = false, Height = 20,
                ForeColor = Color.FromArgb(0x30, 0x70, 0x30),
                Font = new Font("Tahoma", 8.25f)
            };
            scroll.Controls.Add(lblStatus);
            y += 26;

            BuildButtonBar();
        }

        private void BuildButtonBar()
        {
            var btnApply = new RoundButton { Text = "Apply", Width = 90, Height = 26 };
            var btnMax = new RoundButton { Text = "Maximize", Width = 80, Height = 26 };
            var btnClose = new RoundButton { Text = "Close", Width = 80, Height = 26 };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Color.Transparent };
            btnApply.Click += (s, e) => Apply();
            btnMax.Click += (s, e) => WindowState = (WindowState == FormWindowState.Maximized)
                                          ? FormWindowState.Normal : FormWindowState.Maximized;
            btnClose.Click += (s, e) => Close();
            bar.Controls.Add(btnApply);
            bar.Controls.Add(btnMax);
            bar.Controls.Add(btnClose);
            void LayoutBar()
            {
                btnClose.Left = bar.ClientSize.Width - btnClose.Width - 10;
                btnClose.Top = 6;
                btnMax.Left = btnClose.Left - btnMax.Width - 8;
                btnMax.Top = 6;
                btnApply.Left = btnMax.Left - btnApply.Width - 8;
                btnApply.Top = 6;
            }
            bar.Resize += (s, e) => LayoutBar();
            ClientArea.Controls.Add(bar);
            this.Shown += (s, e) => LayoutBar();
        }

        private int Group(Panel host, string title, int y)
        {
            if (y > 6) y += 8;
            var l = new Label
            {
                Text = title, Left = 0, Top = y, AutoSize = true,
                Font = new Font("Tahoma", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0x60, 0x00, 0x10)
            };
            host.Controls.Add(l);
            y += 20;
            var rule = new Panel { Left = 0, Top = y - 2, Width = 500, Height = 1, BackColor = Color.FromArgb(0x88, 0x88, 0x88) };
            host.Controls.Add(rule);
            return y + 4;
        }

        private CheckBox Check(Panel host, string text, bool val, ref int y)
        {
            var c = new CheckBox
            {
                Text = text, Left = 6, Top = y, Width = 500, Checked = val,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            host.Controls.Add(c);
            y += 24;
            return c;
        }

        private void StartTracing()
        {
            OpenCockpitWindow();
            if (cockpitWindow != null)
            {
                cockpitWindow.StartTracing(tSampleRate.Value);
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                lblStatus.Text = "Status: tracing active";
                lblStatus.ForeColor = Color.FromArgb(0x10, 0x80, 0x10);
            }
        }

        private void StopTracing()
        {
            if (cockpitWindow != null)
                cockpitWindow.StopTracing();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            lblStatus.Text = "Status: stopped";
            lblStatus.ForeColor = Color.FromArgb(0x30, 0x70, 0x30);
        }

        private void OpenCockpitWindow()
        {
            if (cockpitWindow == null || cockpitWindow.IsDisposed)
            {
                cockpitWindow = new CockpitWindow();
            }
            cockpitWindow.Show();
            cockpitWindow.BringToFront();
        }

        private void Apply()
        {
            cfg.EnableFridaTracing = cEnable.Checked;
            cfg.FridaSampleRate = tSampleRate.Value;
            cfg.Save();
            onApply?.Invoke();
        }
    }
}
