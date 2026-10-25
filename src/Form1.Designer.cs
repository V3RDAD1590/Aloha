using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aloha
{
    partial class Form1
    {

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int LPAR);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HT_CAPTION = 0x2;

        private void move_window(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.newToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.editToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.findInThisPageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.proxyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.networkToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.browserConfigToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.liveHeadersToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.consoleToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.devToolsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.navigateToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.networkMapToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.siteMapToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.infoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.titleBar = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.button5 = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.button7 = new System.Windows.Forms.Button();
            this.button8 = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.web = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.web)).BeginInit();
            this.SuspendLayout();

            this.titleBar.BackColor = System.Drawing.Color.Transparent;
            this.titleBar.ForeColor = System.Drawing.SystemColors.ControlText;
            this.titleBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.titleBar.Height = 22;
            this.titleBar.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            this.titleBar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.titleBar.Name = "titleBar";
            this.titleBar.Text = "Aloha Browser";
            this.titleBar.MouseDown += new System.Windows.Forms.MouseEventHandler(this.move_window);

            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.fileToolStripMenuItem,
                this.editToolStripMenuItem,
                this.optionsToolStripMenuItem,
                this.navigateToolStripMenuItem,
                this.helpToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 22);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1323, 24);
            this.menuStrip1.TabIndex = 0;

            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.newToolStripMenuItem, this.exitToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "File";

            this.newToolStripMenuItem.Name = "newToolStripMenuItem";
            this.newToolStripMenuItem.Size = new System.Drawing.Size(131, 22);
            this.newToolStripMenuItem.Text = "Open file...";
            this.newToolStripMenuItem.Click += new System.EventHandler(this.button8_Click);

            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(131, 22);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);

            this.editToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.findInThisPageToolStripMenuItem});
            this.editToolStripMenuItem.Name = "editToolStripMenuItem";
            this.editToolStripMenuItem.Size = new System.Drawing.Size(39, 20);
            this.editToolStripMenuItem.Text = "Edit";

            this.findInThisPageToolStripMenuItem.Name = "findInThisPageToolStripMenuItem";
            this.findInThisPageToolStripMenuItem.Size = new System.Drawing.Size(161, 22);
            this.findInThisPageToolStripMenuItem.Text = "Find in this page";

            this.optionsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.proxyToolStripMenuItem,
                this.networkToolStripMenuItem,
                this.browserConfigToolStripMenuItem,
                this.liveHeadersToolStripMenuItem,
                this.consoleToolStripMenuItem,
                this.devToolsToolStripMenuItem});
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            this.optionsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            this.optionsToolStripMenuItem.Text = "Options";

            this.proxyToolStripMenuItem.Name = "proxyToolStripMenuItem";
            this.proxyToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.proxyToolStripMenuItem.Text = "Proxy...";
            this.proxyToolStripMenuItem.Click += new System.EventHandler(this.proxyToolStripMenuItem_Click);

            this.networkToolStripMenuItem.Name = "networkToolStripMenuItem";
            this.networkToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.networkToolStripMenuItem.Text = "Network...";
            this.networkToolStripMenuItem.Click += new System.EventHandler(this.networkToolStripMenuItem_Click);

            this.browserConfigToolStripMenuItem.Name = "browserConfigToolStripMenuItem";
            this.browserConfigToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.browserConfigToolStripMenuItem.Text = "Browser Configuration";
            this.browserConfigToolStripMenuItem.Click += new System.EventHandler(this.browserConfigToolStripMenuItem_Click);

            this.liveHeadersToolStripMenuItem.Name = "liveHeadersToolStripMenuItem";
            this.liveHeadersToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.liveHeadersToolStripMenuItem.Text = "Live headers...";
            this.liveHeadersToolStripMenuItem.Click += new System.EventHandler(this.liveHeadersToolStripMenuItem_Click);

            this.consoleToolStripMenuItem.Name = "consoleToolStripMenuItem";
            this.consoleToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.consoleToolStripMenuItem.Text = "Console...";
            this.consoleToolStripMenuItem.Click += new System.EventHandler(this.consoleToolStripMenuItem_Click);

            this.devToolsToolStripMenuItem.Name = "devToolsToolStripMenuItem";
            this.devToolsToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.devToolsToolStripMenuItem.Text = "DevTools (F12)";
            this.devToolsToolStripMenuItem.Click += new System.EventHandler(this.devToolsToolStripMenuItem_Click);

            this.navigateToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.networkMapToolStripMenuItem, this.siteMapToolStripMenuItem});
            this.navigateToolStripMenuItem.Name = "navigateToolStripMenuItem";
            this.navigateToolStripMenuItem.Size = new System.Drawing.Size(66, 20);
            this.navigateToolStripMenuItem.Text = "Navigate";

            this.networkMapToolStripMenuItem.Name = "networkMapToolStripMenuItem";
            this.networkMapToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
            this.networkMapToolStripMenuItem.Text = "Network map";

            this.siteMapToolStripMenuItem.Name = "siteMapToolStripMenuItem";
            this.siteMapToolStripMenuItem.Size = new System.Drawing.Size(195, 22);
            this.siteMapToolStripMenuItem.Text = "Site map";

            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.infoToolStripMenuItem});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
            this.helpToolStripMenuItem.Text = "Help";

            this.infoToolStripMenuItem.Name = "infoToolStripMenuItem";
            this.infoToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.infoToolStripMenuItem.Text = "info";
            this.infoToolStripMenuItem.Click += new System.EventHandler(this.infoToolStripMenuItem_Click);

            this.button1.Location = new System.Drawing.Point(0, 47);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(39, 28);
            this.button1.TabIndex = 1;
            this.button1.Text = "<";
            this.button1.Click += new System.EventHandler(this.button1_Click);
            this.button1.UseVisualStyleBackColor = true;

            this.button2.Location = new System.Drawing.Point(36, 47);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(44, 28);
            this.button2.TabIndex = 2;
            this.button2.Text = ">";
            this.button2.Click += new System.EventHandler(this.button2_Click);
            this.button2.UseVisualStyleBackColor = true;

            this.button3.Location = new System.Drawing.Point(77, 47);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(72, 28);
            this.button3.TabIndex = 3;
            this.button3.Text = "Home";
            this.button3.Click += new System.EventHandler(this.button3_Click);
            this.button3.UseVisualStyleBackColor = true;

            this.button4.Location = new System.Drawing.Point(146, 47);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(74, 28);
            this.button4.TabIndex = 4;
            this.button4.Text = "Localhost";
            this.button4.Click += new System.EventHandler(this.button4_Click);
            this.button4.UseVisualStyleBackColor = true;

            this.button5.Location = new System.Drawing.Point(217, 47);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(84, 28);
            this.button5.TabIndex = 5;
            this.button5.Text = "Reload URL";
            this.button5.Click += new System.EventHandler(this.button5_Click);
            this.button5.UseVisualStyleBackColor = true;

            this.button6.Location = new System.Drawing.Point(297, 47);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(111, 28);
            this.button6.TabIndex = 6;
            this.button6.Text = "Get Current URL";
            this.button6.Click += new System.EventHandler(this.button6_Click);
            this.button6.UseVisualStyleBackColor = true;

            this.button7.Location = new System.Drawing.Point(617, 47);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(129, 28);
            this.button7.TabIndex = 7;
            this.button7.Text = "Change Network IP";
            this.button7.Click += new System.EventHandler(this.button7_Click);
            this.button7.UseVisualStyleBackColor = true;

            this.button8.BackColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.button8.Font = new System.Drawing.Font("Consolas", 9F);
            this.button8.ForeColor = System.Drawing.Color.FromArgb(0xB4, 0xDC, 0xBA);
            this.button8.Location = new System.Drawing.Point(405, 47);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(216, 26);
            this.button8.TabIndex = 9;
            this.button8.Text = "New File...";
            this.button8.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
            this.button8.UseVisualStyleBackColor = false;
            this.button8.Click += new System.EventHandler(this.fileToolStripMenuItem_Click);

            this.textBox1.Location = new System.Drawing.Point(0, 73);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(1192, 20);
            this.textBox1.TabIndex = 8;
            this.textBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox1.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox1_KeyUp);

            this.textBox2.BackColor = System.Drawing.SystemColors.WindowText;
            this.textBox2.Font = new System.Drawing.Font("Consolas", 11F);
            this.textBox2.ForeColor = System.Drawing.Color.DarkRed;
            this.textBox2.Location = new System.Drawing.Point(1196, 50);
            this.textBox2.Multiline = true;
            this.textBox2.Name = "textBox2";
            this.textBox2.ReadOnly = true;
            this.textBox2.ScrollBars = System.Windows.Forms.ScrollBars.Horizontal;
            this.textBox2.Size = new System.Drawing.Size(127, 43);
            this.textBox2.TabIndex = 10;
            this.textBox2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));

            this.web.Location = new System.Drawing.Point(0, 93);
            this.web.Name = "web";
            this.web.Size = new System.Drawing.Size(1323, 608);
            this.web.TabIndex = 11;
            this.web.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));

            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1323, 719);
            this.Controls.Add(this.web);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.button8);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.button7);
            this.Controls.Add(this.button6);
            this.Controls.Add(this.button5);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.menuStrip1);
            this.Controls.Add(this.titleBar);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "Form1";
            this.Text = "Aloha";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.move_window);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.web)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem newToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem editToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem findInThisPageToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem proxyToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem networkToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem browserConfigToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem liveHeadersToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem consoleToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem devToolsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem navigateToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem networkMapToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem siteMapToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem infoToolStripMenuItem;
        private System.Windows.Forms.Label titleBar;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button button7;
        private System.Windows.Forms.Button button8;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.TextBox textBox2;
        private Microsoft.Web.WebView2.WinForms.WebView2 web;
    }
}
