using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Aloha.Cutscenes
{
    public static class CutsceneMenu
    {
        public static void Wire(ToolStripMenuItem parent)
        {
            if (parent == null) return;

            var root = new ToolStripMenuItem("Cutscenes");

            var on = new ToolStripMenuItem("Enabled") { CheckOnClick = true };
            on.Checked = CutsceneDirector.Current.Book.Enabled;
            on.CheckedChanged += (s, e) => CutsceneDirector.Current.Enabled = on.Checked;

            var preview = new ToolStripMenuItem("Preview");
            preview.DropDownOpening += (s, e) =>
            {
                preview.DropDownItems.Clear();
                var rules = CutsceneDirector.Current.Book.Rules;
                if (rules == null || rules.Length == 0)
                {
                    preview.DropDownItems.Add(new ToolStripMenuItem("(no rules)") { Enabled = false });
                    return;
                }
                foreach (var r in rules)
                {
                    if (r == null) continue;
                    string id = r.Id;
                    string label = id + (r.Enabled ? "" : "  (off)");
                    var it = new ToolStripMenuItem(label);
                    it.Click += (a, b) => CutsceneDirector.Current.Preview(id);
                    preview.DropDownItems.Add(it);
                }
            };

            var reload = new ToolStripMenuItem("Reload rules");
            reload.Click += (s, e) =>
            {
                CutsceneDirector.Current.Load();
                on.Checked = CutsceneDirector.Current.Book.Enabled;
            };

            var reset = new ToolStripMenuItem("Forget cooldowns");
            reset.Click += (s, e) => CutsceneDirector.Current.ResetHistory();

            var open = new ToolStripMenuItem("Open folder...");
            open.Click += (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + CutsceneDirector.Dir + "\""); }
                catch (Exception ex) { ActivityLog.General("Cutscenes: " + ex.Message); }
            };

            root.DropDownItems.Add(on);
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(preview);
            root.DropDownItems.Add(reload);
            root.DropDownItems.Add(reset);
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(open);

            parent.DropDownItems.Add(root);
        }
    }
}
