using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Xml.Linq;

namespace StudyDesk
{
    public class Module
    {
        public string Name, Folder;
        public bool Deleted;
        public List<Module> Children = new List<Module>();
        public override string ToString() { return Name + (Deleted ? "（已删除，可恢复）" : ""); }
        public XElement ToXml() { return new XElement("模块", new XAttribute("名称", Name), new XAttribute("目录", Folder), new XAttribute("已删除", Deleted), Children.Select(m => m.ToXml())); }
        public static Module Read(XElement element)
        {
            string name = (string)element.Attribute("名称"), folder = (string)element.Attribute("目录");
            Store.ValidateName(name); Store.ValidateName(folder);
            Module m = new Module { Name = name, Folder = folder, Deleted = (bool?)element.Attribute("已删除") ?? false };
            m.Children = element.Elements("模块").Select(Read).ToList(); return m;
        }
    }
    public class Store
    {
        public readonly string Root, Config, ArchiveRoot;
        public List<Module> Modules;
        static readonly string[] Terms = { "研一上", "研一下", "研二上", "研二下", "研三" };
        static readonly string[] Categories = { "专业", "课程", "其他" };
        public Store(string directory)
        {
            Root = Path.Combine(directory, "学习资料"); Config = Path.Combine(directory, "模块设置.xml"); ArchiveRoot = Path.Combine(directory, "移出资料");
            if (File.Exists(Config)) Modules = XDocument.Load(Config).Root.Elements("模块").Select(Module.Read).ToList();
            else
            {
                Directory.CreateDirectory(Root);
                Modules = Scan(Root, Terms);
                foreach (Module m in Modules) m.Children = Scan(Path.Combine(Root, m.Folder), Categories);
                Save();
            }
        }
        static List<Module> Scan(string path, string[] defaults)
        {
            Directory.CreateDirectory(path);
            string[] existing = Directory.GetDirectories(path).Select(Path.GetFileName).ToArray();
            IEnumerable<string> names = defaults.Concat(existing.OrderBy(x => x)).Distinct(StringComparer.OrdinalIgnoreCase);
            List<Module> result = new List<Module>();
            foreach (string name in names) { Directory.CreateDirectory(Path.Combine(path, name)); result.Add(new Module { Name = name, Folder = name }); }
            return result;
        }
        public void Save()
        {
            string temp = Config + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new XDocument(new XElement("资料管理", new XAttribute("版本", "2"), Modules.Select(m => m.ToXml()))).Save(temp);
                if (File.Exists(Config)) File.Replace(temp, Config, null); else File.Move(temp, Config);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void ValidateName(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim() || name.EndsWith(".") || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new Exception("请输入有效名称（不超过 80 个字，不能包含 \\ / : * ? \" < > |）。");
            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) throw new Exception("此名称是系统保留名称，请换一个。");
        }
        public Module Add(List<Module> list, string parent, string name, bool term)
        {
            ValidateName(name);
            if (list.Any(m => String.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))) throw new Exception("已有同名模块，请换个名字，或恢复已删除的模块。");
            string folder = name;
            if (list.Any(m => String.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase)) || Directory.Exists(Path.Combine(parent, folder)) || File.Exists(Path.Combine(parent, folder))) folder = name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Module module = new Module { Name = name, Folder = folder };
            Directory.CreateDirectory(Path.Combine(parent, folder));
            if (term) module.Children = Scan(Path.Combine(parent, folder), Categories);
            list.Add(module);
            try { Save(); } catch { list.Remove(module); throw; } return module;
        }
        public void Rename(List<Module> list, Module module, string name)
        {
            ValidateName(name);
            if (list.Any(m => m != module && String.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))) throw new Exception("已有同名模块。");
            string old = module.Name; module.Name = name; try { Save(); } catch { module.Name = old; throw; }
        }
        public void SetDeleted(Module module, bool deleted)
        {
            bool old = module.Deleted; module.Deleted = deleted; try { Save(); } catch { module.Deleted = old; throw; }
        }
        public string RemovePermanently(List<Module> list, Module module, string parent, string label)
        {
            int index = list.IndexOf(module);
            if (index < 0) throw new Exception("找不到此模块，请重新打开管理窗口。");
            ValidateName(module.Folder); ValidateName(label);
            string parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            string rootPath = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
            string source = Path.GetFullPath(Path.Combine(parentPath, module.Folder));
            if ((!String.Equals(parentPath, rootPath, StringComparison.OrdinalIgnoreCase) && !parentPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ||
                !String.Equals(Path.GetDirectoryName(source), parentPath, StringComparison.OrdinalIgnoreCase))
                throw new Exception("资料路径不在学习资料目录内，操作已停止。");
            bool sourceExists = true;
            try
            {
                FileAttributes attributes = File.GetAttributes(source);
                if ((attributes & FileAttributes.Directory) == 0) throw new IOException("原资料路径是文件而不是文件夹，模块未删除，请检查：\n" + source);
            }
            catch (FileNotFoundException) { sourceExists = false; }
            catch (DirectoryNotFoundException) { sourceExists = false; }
            catch (UnauthorizedAccessException e) { throw new Exception("没有权限访问原资料文件夹，模块未删除。请检查访问权限：\n" + source, e); }
            if (!sourceExists)
            {
                list.RemoveAt(index);
                try { Save(); } catch { list.Insert(index, module); throw; }
                return null;
            }
            string shortLabel = label.Substring(0, Math.Min(label.Length, 50)).TrimEnd(' ', '.');
            string destination = Path.GetFullPath(Path.Combine(ArchiveRoot, shortLabel + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            if (!String.Equals(Path.GetDirectoryName(destination), Path.GetFullPath(ArchiveRoot), StringComparison.OrdinalIgnoreCase)) throw new Exception("移出资料路径无效。");
            Directory.CreateDirectory(ArchiveRoot);
            Directory.Move(source, destination);
            list.RemoveAt(index);
            try { Save(); }
            catch (Exception saveError)
            {
                list.Insert(index, module);
                try { Directory.Move(destination, source); }
                catch (Exception moveError) { throw new Exception("设置未能保存，模块仍保留。资料已移至：\n" + destination + "\n请勿重复操作，请先检查此位置。", new AggregateException(saveError, moveError)); }
                throw new Exception("设置未能保存，资料已移回原位置，模块未删除。\n" + saveError.Message, saveError);
            }
            return destination;
        }
    }
    static class Program
    {
        [STAThread] static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show("操作未完成：" + e.Exception.Message, "资料管理"); };
            try { Application.Run(new MainForm(new Store(AppDomain.CurrentDomain.BaseDirectory))); }
            catch (Exception e) { MessageBox.Show("无法启动：" + e.Message + "\n请检查程序目录的访问权限及“模块设置.xml”文件。", "资料管理"); }
        }
    }
    public class MainForm : Form
    {
        readonly Store store;
        readonly Color ink = Color.FromArgb(57, 43, 34), muted = Color.FromArgb(132, 114, 101), accent = Color.FromArgb(197, 85, 22), side = Color.FromArgb(255, 253, 249), pale = Color.FromArgb(255, 232, 211);
        Panel main;
        FlowLayoutPanel navigation;
        Label title, subtitle, footer;
        Button manage;
        Module term, category;
        string current;
        ListView files;
        public MainForm(Store data)
        {
            store = data; Text = "资料管理"; AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 13.33F, FontStyle.Regular, GraphicsUnit.Pixel);
            BackColor = Color.FromArgb(255, 249, 243); ForeColor = ink; ClientSize = new Size(1120, 740); MinimumSize = new Size(1040, 680); StartPosition = FormStartPosition.CenterScreen;
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 196, BackColor = side };
            sidebar.Paint += delegate(object s, PaintEventArgs e) { using (Pen p = new Pen(Color.FromArgb(239, 228, 216))) e.Graphics.DrawLine(p, 195, 0, 195, sidebar.Height); };
            Label brand = Label("资料管理", 23, ink); brand.SetBounds(23, 32, 160, 43); sidebar.Controls.Add(brand);
            Label tagline = Label("资料管理", 9, muted); tagline.SetBounds(25, 82, 150, 28); sidebar.Controls.Add(tagline);
            navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 12, 12, 0) };
            Panel spacer = new Panel { Dock = DockStyle.Top, Height = 130 }; sidebar.Controls.Add(navigation); sidebar.Controls.Add(spacer); spacer.SendToBack(); brand.BringToFront(); tagline.BringToFront();
            Label local = Label("本地存储 · 自定义版 2.3", 9, muted); local.Dock = DockStyle.Bottom; local.Height = 40; local.Padding = new Padding(20, 0, 0, 0); sidebar.Controls.Add(local); local.BringToFront();
            Controls.Add(sidebar);
            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(34, 26, 34, 12) }; Controls.Add(body); body.BringToFront();
            Panel header = new Panel { Dock = DockStyle.Top, Height = 118 };
            Label eyebrow = Label("学习 · 整理 · 成长", 8, accent); eyebrow.SetBounds(0, 0, 400, 24); header.Controls.Add(eyebrow);
            title = Label("", 24, ink); title.SetBounds(0, 28, 600, 46); title.AutoEllipsis = true; title.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; header.Controls.Add(title);
            subtitle = Label("", 10, muted); subtitle.SetBounds(0, 82, 760, 28); subtitle.AutoEllipsis = true; subtitle.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; header.Controls.Add(subtitle);
            manage = Button("管理模块", true); manage.Width = 114; manage.Dock = DockStyle.Right; Panel manageHolder = new Panel { Dock = DockStyle.Right, Width = 124, Padding = new Padding(0, 32, 0, 48) }; manageHolder.Controls.Add(manage); header.Controls.Add(manageHolder); manageHolder.BringToFront();
            manage.Click += delegate { ManageModules(); };
            main = new Panel { Dock = DockStyle.Fill }; footer = Label("", 9, muted); footer.Dock = DockStyle.Bottom; footer.Height = 32;
            body.Controls.Add(main); body.Controls.Add(footer); body.Controls.Add(header); ShowHome();
            Activated += delegate { if (files != null && !files.IsDisposed) RefreshFiles(); };
        }
        Label Label(string text, float size, Color color) { return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size * 1.333F, size >= 18 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel), ForeColor = color, TextAlign = ContentAlignment.MiddleLeft }; }
        Button Button(string text, bool primary)
        {
            Button b = new Button { Text = text, Size = new Size(116, 38), FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : Color.White, ForeColor = primary ? Color.White : ink, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = Color.FromArgb(233, 214, 198); return b;
        }
        void Nav(string name, Module module)
        {
            Button b = Button(name, false); b.Size = new Size(148, 46); b.Margin = new Padding(0, 0, 0, 9); b.Padding = new Padding(10, 0, 0, 0); b.TextAlign = ContentAlignment.MiddleLeft; b.AutoEllipsis = true;
            bool selected = module == term; b.BackColor = selected ? pale : side; b.ForeColor = selected ? accent : ink; b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(251, 241, 230);
            b.Click += delegate { if (module == null) ShowHome(); else ShowCategories(module); }; navigation.Controls.Add(b);
        }
        void Begin(string heading, string description)
        {
            foreach (Control c in main.Controls.Cast<Control>().ToArray()) c.Dispose(); files = null;
            foreach (Control c in navigation.Controls.Cast<Control>().ToArray()) c.Dispose();
            title.Text = heading; subtitle.Text = description; footer.Text = "资料保存在程序旁的“学习资料”文件夹中";
            manage.Text = term == null ? "管理模块" : "管理分类";
            Nav("总览", null); foreach (Module m in store.Modules.Where(x => !x.Deleted)) Nav(m.Name, m);
        }
        FlowLayoutPanel Cards() { FlowLayoutPanel p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 10, 0, 0) }; main.Controls.Add(p); return p; }
        void Card(FlowLayoutPanel owner, string name, string action, Action click)
        {
            Panel p = new Panel { Size = new Size(244, 175), BackColor = Color.White, Margin = new Padding(0, 0, 18, 20) };
            p.Paint += delegate(object s, PaintEventArgs e) { using (Pen pen = new Pen(Color.FromArgb(239, 224, 211))) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); };
            Label label = Label(name, 22, ink); label.SetBounds(20, 32, 210, 43); label.AutoEllipsis = true; p.Controls.Add(label);
            Button b = Button(action + "  →", false); b.SetBounds(20, 110, 202, 36); b.BackColor = pale; b.ForeColor = accent; b.FlatAppearance.BorderSize = 0; p.Controls.Add(b);
            b.Click += delegate { click(); }; p.Click += delegate { click(); }; label.Click += delegate { click(); }; owner.Controls.Add(p);
        }
        void ShowHome()
        {
            term = null; category = null; current = null; Begin("我的资料库", "点击“管理模块”，自由添加、重命名或删除学期模块。");
            FlowLayoutPanel p = Cards(); foreach (Module m in store.Modules.Where(x => !x.Deleted)) { Module selected = m; Card(p, m.Name, "进入模块", delegate { ShowCategories(selected); }); }
            if (p.Controls.Count == 0) footer.Text = "暂无模块，点击右上角“管理模块”添加或恢复。";
        }
        void ShowCategories(Module selected)
        {
            term = selected; category = null; current = null; Begin(term.Name, "选择分类整理资料，也可以点击“管理分类”自定义。");
            FlowLayoutPanel p = Cards(); foreach (Module m in term.Children.Where(x => !x.Deleted)) { Module cat = m; Card(p, m.Name, "打开分类", delegate { category = cat; current = Path.Combine(store.Root, term.Folder, cat.Folder); Directory.CreateDirectory(current); ShowFiles(); }); }
            if (p.Controls.Count == 0) footer.Text = "暂无分类，点击右上角“管理分类”添加或恢复。";
        }
        string NameInput(string caption, string value)
        {
            using (Form f = new Form { Text = caption, AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(430, 166), Font = Font, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false })
            {
                Label label = Label("请输入名称", 11, ink); label.SetBounds(24, 12, 380, 30); TextBox input = new TextBox { Text = value, MaxLength = 80 }; input.SetBounds(24, 50, 380, 28);
                Button ok = Button("确定", true); ok.SetBounds(288, 110, 116, 34); Button cancel = Button("取消", false); cancel.SetBounds(160, 110, 116, 34); cancel.DialogResult = DialogResult.Cancel;
                ok.Click += delegate { try { Store.ValidateName(input.Text.Trim()); f.DialogResult = DialogResult.OK; } catch (Exception e) { MessageBox.Show(f, e.Message, "名称无效"); } };
                f.Controls.AddRange(new Control[] { label, input, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel; f.Shown += delegate { input.Focus(); input.SelectAll(); };
                return f.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }
        void ManageModules()
        {
            bool atHome = term == null; Module selectedTerm = term; List<Module> list = atHome ? store.Modules : term.Children;
            string parent = atHome ? store.Root : Path.Combine(store.Root, term.Folder);
            using (Form f = new Form { Text = atHome ? "管理模块" : "管理分类 · " + term.Name, Font = Font, AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(610, 450), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false })
            {
                Label hint = Label("删除后可恢复；切换到“查看已删除模块”后，可彻底删除并移出资料。", 10, muted); hint.SetBounds(22, 10, 566, 46);
                ListBox items = new ListBox { IntegralHeight = false }; items.SetBounds(22, 64, 420, 310);
                CheckBox deleted = new CheckBox { Text = "查看已删除模块", AutoSize = true, Location = new Point(22, 393) };
                Button add = Button("＋ 新增", true), rename = Button("重命名", false), remove = Button("删除", false), restore = Button("恢复", false), done = Button("完成", true);
                Button[] actions = { add, rename, remove, restore, done }; for (int i = 0; i < actions.Length; i++) actions[i].SetBounds(460, 64 + i * 62, 128, 38);
                Action updateActions = delegate {
                    Module m = items.SelectedItem as Module;
                    add.Enabled = !deleted.Checked;
                    rename.Enabled = m != null && !m.Deleted;
                    remove.Enabled = m != null;
                    remove.Text = deleted.Checked ? "彻底删除" : "删除";
                    remove.ForeColor = deleted.Checked ? Color.FromArgb(158, 57, 35) : ink;
                    restore.Enabled = m != null && m.Deleted;
                };
                Action refresh = delegate {
                    Module selection = items.SelectedItem as Module; items.Items.Clear(); foreach (Module m in list.Where(x => x.Deleted == deleted.Checked)) items.Items.Add(m);
                    if (selection != null && items.Items.Contains(selection)) items.SelectedItem = selection; else if (items.Items.Count > 0) items.SelectedIndex = 0;
                    updateActions();
                };
                items.SelectedIndexChanged += delegate { updateActions(); };
                deleted.CheckedChanged += delegate { refresh(); };
                add.Click += delegate { string name = NameInput(atHome ? "新增模块" : "新增分类", ""); if (name == null) return; try { Module m = store.Add(list, parent, name, atHome); refresh(); items.SelectedItem = m; } catch (Exception e) { MessageBox.Show(f, e.Message, "添加失败"); } };
                rename.Click += delegate { Module m = items.SelectedItem as Module; if (m == null) return; string name = NameInput("重命名", m.Name); if (name == null) return; try { store.Rename(list, m, name); refresh(); } catch (Exception e) { MessageBox.Show(f, e.Message, "重命名失败"); } };
                restore.Click += delegate { Module m = items.SelectedItem as Module; if (m == null) return; try { store.SetDeleted(m, false); refresh(); } catch (Exception e) { MessageBox.Show(f, e.Message, "保存失败"); } };
                remove.Click += delegate {
                    Module m = items.SelectedItem as Module; if (m == null) return;
                    if (!m.Deleted)
                    {
                        if (MessageBox.Show(f, "从界面删除“" + m.Name + "”？\n其中的文件会保留，之后可在“查看已删除模块”中恢复。", "删除模块", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        try { store.SetDeleted(m, true); refresh(); } catch (Exception e) { MessageBox.Show(f, e.Message, "保存失败"); }
                        return;
                    }
                    if (MessageBox.Show(f, "彻底删除“" + m.Name + "”？\n模块将从列表中移除，不能通过“恢复”撤销。\n原文件夹存在时，全部资料会移动到：\n" + store.ArchiveRoot + "\n原文件夹已不存在时，仅清理模块记录。", "彻底删除并移出资料", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try {
                        string archive = store.RemovePermanently(list, m, parent, m.Name); refresh();
                        if (archive == null) MessageBox.Show(f, "模块已彻底删除。原资料文件夹已不存在，无文件需要移动。", "模块已清理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        else if (MessageBox.Show(f, "模块已彻底删除，资料已移至：\n" + archive + "\n\n是否打开此文件夹？", "资料已移出", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Open(archive);
                    }
                    catch (Exception e) { MessageBox.Show(f, e.Message, "操作未完成"); }
                };
                done.Click += delegate { f.Close(); };
                f.Controls.AddRange(new Control[] { hint, items, deleted, add, rename, remove, restore, done }); refresh(); f.ShowDialog(this);
            }
            if (atHome) ShowHome(); else ShowCategories(selectedTerm);
        }
        void ShowFiles()
        {
            Begin(category.Name, term.Name + "  /  " + category.Name + "    ·    双击打开文件夹或文件");
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, WrapContents = false };
            Button back = Button("← 返回上级", false), create = Button("＋ 新建文件夹", true), import = Button("导入文件", false), open = Button("在资源管理器打开", false), refresh = Button("刷新", false); create.Width = 134; open.Width = 170; refresh.Width = 70;
            back.Click += delegate { string catRoot = Path.Combine(store.Root, term.Folder, category.Folder); if (String.Equals(current, catRoot, StringComparison.OrdinalIgnoreCase)) ShowCategories(term); else { current = Directory.GetParent(current).FullName; ShowFiles(); } };
            create.Click += delegate { string name = NameInput("新建文件夹", ""); if (name == null) return; string target = Path.Combine(current, name); if (Directory.Exists(target) || File.Exists(target)) { MessageBox.Show(this, "已有同名内容。", "提示"); return; } Directory.CreateDirectory(target); RefreshFiles(); };
            import.Click += delegate { ImportFiles(); }; open.Click += delegate { Open(current); }; refresh.Click += delegate { RefreshFiles(); }; toolbar.Controls.AddRange(new Control[] { back, create, import, open, refresh });
            Panel pathHolder = new Panel { Dock = DockStyle.Top, Height = 44 }; TextBox path = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = current, BackColor = Color.FromArgb(250, 239, 229) }; pathHolder.Controls.Add(path);
            files = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 14.67F, FontStyle.Regular, GraphicsUnit.Pixel) };
            files.Columns.Add("名称", 345); files.Columns.Add("类型", 115); files.Columns.Add("修改时间", 190);
            ImageList icons = new ImageList { ImageSize = new Size(28, 28), ColorDepth = ColorDepth.Depth32Bit }; Bitmap folder = new Bitmap(28, 28); using (Graphics g = Graphics.FromImage(folder)) { g.FillRectangle(Brushes.Goldenrod, 2, 6, 12, 7); g.FillRectangle(Brushes.Goldenrod, 2, 9, 24, 15); g.FillRectangle(Brushes.Moccasin, 2, 12, 24, 12); } icons.Images.Add(folder); folder.Dispose(); using (Bitmap bitmap = SystemIcons.Application.ToBitmap()) icons.Images.Add(bitmap); files.SmallImageList = icons;
            files.Disposed += delegate { icons.Dispose(); }; files.DoubleClick += delegate { OpenSelection(); }; files.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { OpenSelection(); e.Handled = true; } };
            main.Controls.Add(files); main.Controls.Add(pathHolder); main.Controls.Add(toolbar); RefreshFiles();
        }
        void RefreshFiles()
        {
            if (files == null || files.IsDisposed) return;
            files.BeginUpdate(); try
            {
                FileSystemInfo[] entries = new DirectoryInfo(current).GetFileSystemInfos().OrderBy(x => x is DirectoryInfo ? 0 : 1).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(); files.Items.Clear();
                foreach (FileSystemInfo entry in entries) { bool dir = entry is DirectoryInfo; ListViewItem row = new ListViewItem(entry.Name, dir ? 0 : 1); row.Tag = entry.FullName; row.SubItems.Add(dir ? "文件夹" : "文件"); row.SubItems.Add(entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm")); files.Items.Add(row); }
                footer.Text = entries.Length == 0 ? "这里还是空的，可以新建文件夹或导入文件。" : "共 " + entries.Count(x => x is DirectoryInfo) + " 个文件夹，" + entries.Count(x => x is FileInfo) + " 个文件";
            } catch (Exception e) { footer.Text = "读取失败：" + e.Message; } finally { files.EndUpdate(); }
        }
        void OpenSelection() { if (files.SelectedItems.Count == 0) return; string path = (string)files.SelectedItems[0].Tag; if (Directory.Exists(path)) { current = path; ShowFiles(); } else Open(path); }
        void Open(string path) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(this, "无法打开：" + e.Message, "提示"); } }
        void ImportFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择要复制到当前文件夹的文件", Multiselect = true, Filter = "所有文件|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return; int count = 0; List<string> errors = new List<string>();
                foreach (string source in dialog.FileNames) { try { string target = Path.Combine(current, Path.GetFileName(source)); int n = 1; while (File.Exists(target) || Directory.Exists(target)) target = Path.Combine(current, Path.GetFileNameWithoutExtension(source) + " (" + n++ + ")" + Path.GetExtension(source)); File.Copy(source, target, false); count++; } catch (Exception e) { errors.Add(Path.GetFileName(source) + "：" + e.Message); } }
                RefreshFiles(); MessageBox.Show(this, "已导入 " + count + " 个文件。" + (errors.Count == 0 ? "" : "\n" + String.Join("\n", errors)), "导入完成");
            }
        }
    }
}
