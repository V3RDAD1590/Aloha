using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{
    public static class AlohaContextMenu
    {

        public static void Attach(CoreWebView2 core, Control host)
        {
            core.ContextMenuRequested += (s, e) =>
            {
                try { Show(core, host, e); }
                catch (Exception ex) { ActivityLog.General("context menu: " + ex.Message); e.Handled = false; }   
            };
        }

        private static void Show(CoreWebView2 core, Control host, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            var items = e.MenuItems;
            if (items == null || items.Count == 0) return;

            var deferral = e.GetDeferral();
            e.Handled = true;
            var menu = new ContextMenuStrip { Renderer = new AlohaMenuRenderer(), ShowImageMargin = false, Font = new Font("Tahoma", 8.25f), Padding = new Padding(2) };
            Fill(menu.Items, items, e);
            menu.Closed += (s2, e2) =>
            {

                host.BeginInvoke((Action)(() => { try { deferral.Complete(); } catch { } menu.Dispose(); }));
            };
            menu.Show(host, new Point(e.Location.X, e.Location.Y));
        }

        private static void Fill(ToolStripItemCollection into, IList<CoreWebView2ContextMenuItem> items, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            ToolStripItem last = null;
            foreach (var it in items)
            {
                if (it.Kind == CoreWebView2ContextMenuItemKind.Separator)
                {
                    if (last != null && !(last is ToolStripSeparator)) into.Add(last = new ToolStripSeparator());
                    continue;
                }
                var mi = new ToolStripMenuItem(it.Label)
                {
                    Enabled = it.IsEnabled,
                    ShortcutKeyDisplayString = string.IsNullOrEmpty(it.ShortcutKeyDescription) ? null : it.ShortcutKeyDescription,
                    Checked = (it.Kind == CoreWebView2ContextMenuItemKind.CheckBox || it.Kind == CoreWebView2ContextMenuItemKind.Radio) && it.IsChecked,
                    Padding = new Padding(4, 1, 4, 1)
                };
                if (it.Kind == CoreWebView2ContextMenuItemKind.Submenu)
                {
                    mi.DropDown = new ToolStripDropDownMenu { Renderer = new AlohaMenuRenderer(), ShowImageMargin = false, Font = new Font("Tahoma", 8.25f), Padding = new Padding(2) };
                    Fill(mi.DropDownItems, it.Children, e);
                }
                else
                {
                    int id = it.CommandId;
                    mi.Click += (s, a) => { try { e.SelectedCommandId = id; } catch { } };
                }
                into.Add(last = mi);
            }

            if (into.Count > 0 && into[into.Count - 1] is ToolStripSeparator) into.RemoveAt(into.Count - 1);
        }
    }

    public sealed class AlohaMenuRenderer : ToolStripRenderer
    {
        private static readonly Color NavTop = Color.FromArgb(252, 252, 253);
        private static readonly Color NavBot = Color.FromArgb(216, 216, 220);
        private static readonly Color Dk     = Color.FromArgb(0x80, 0x80, 0x80);
        private static readonly Color Darker = Color.FromArgb(0x40, 0x40, 0x40);
        private static readonly Color Face   = Color.FromArgb(0xDF, 0xDF, 0xDF);
        private static readonly Color Press  = Color.FromArgb(0xE8, 0xE8, 0xEA);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var r = new Rectangle(Point.Empty, e.ToolStrip.Size);
            using (var br = new LinearGradientBrush(r, NavTop, NavBot, LinearGradientMode.Vertical)) e.Graphics.FillRectangle(br, r);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics; int w = e.ToolStrip.Width - 1, h = e.ToolStrip.Height - 1;
            using (var p = new Pen(Face))         { g.DrawLine(p, 0, 0, w, 0); g.DrawLine(p, 0, 0, 0, h); }
            using (var p = new Pen(Darker))       { g.DrawLine(p, 0, h, w, h); g.DrawLine(p, w, 0, w, h); }
            using (var p = new Pen(Color.White))  { g.DrawLine(p, 1, 1, w - 1, 1); g.DrawLine(p, 1, 1, 1, h - 1); }
            using (var p = new Pen(Dk))           { g.DrawLine(p, 1, h - 1, w - 1, h - 1); g.DrawLine(p, w - 1, 1, w - 1, h - 1); }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height - 1);
            using (var b = new SolidBrush(Press)) g.FillRectangle(b, r);

            using (var p = new Pen(Dk))          { g.DrawLine(p, r.Left, r.Top, r.Right, r.Top); g.DrawLine(p, r.Left, r.Top, r.Left, r.Bottom); }
            using (var p = new Pen(Color.White)) { g.DrawLine(p, r.Left, r.Bottom, r.Right, r.Bottom); g.DrawLine(p, r.Right, r.Top, r.Right, r.Bottom); }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var item = e.Item;
            Color c = item.Enabled ? Color.Black : Dk;
            var r = e.TextRectangle;
            if (item.Selected && item.Enabled) r.Offset(1, 1);
            if (!item.Enabled)
            {

                var r2 = r; r2.Offset(1, 1);
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r2, Color.White, e.TextFormat);
            }
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, c, e.TextFormat);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2, w = e.Item.Width;
            using (var p = new Pen(Dk))          e.Graphics.DrawLine(p, 4, y, w - 5, y);
            using (var p = new Pen(Color.White)) e.Graphics.DrawLine(p, 4, y + 1, w - 5, y + 1);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Color.Black : Dk;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var p = new Pen(Color.Black, 2f)) e.Graphics.DrawLines(p, new[] { new Point(cx - 4, cy), new Point(cx - 1, cy + 3), new Point(cx + 4, cy - 3) });
        }
    }
}
