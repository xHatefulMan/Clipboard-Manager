using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClipboardManager
{
    public class ClipEntry
    {
        public string Text { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.Now;
    }

    public partial class Form1 : Form
    {
        private List<ClipEntry> entries = new();
        private NotifyIcon tray;
        private System.Windows.Forms.Timer clipWatcher;
        private string lastClip = "";
        private string saveFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardManager", "history.json");
        private string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardManager", "settings.json");
        private Panel pnlList;
        private TextBox txtSearch;
        private string searchText = "";
        private Label lblCount;
        private CheckBox chkLaunch, chkAutoDelete;

        public Form1()
        {
            InitializeComponent();
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "clipb.ico");
            if (File.Exists(iconPath)) this.Icon = new Icon(iconPath);
            BuildUI();
            SetupTray();
            LoadSettings();
            LoadHistory();
            SetupClipWatcher();
            RefreshList();
        }

        private void BuildUI()
        {
            this.Text = "Clipboard Manager";
            this.BackColor = Color.FromArgb(18, 18, 18);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(600, 500);
            this.Size = new Size(900, 950);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Width / 2 - 390,
                Screen.PrimaryScreen.WorkingArea.Height / 2 - 410);
            this.Font = new Font("Segoe UI", 10f);
            this.ShowInTaskbar = true;
            this.Resize += (s, e) => RefreshList();

            int pad = 20;

            // TITRE
            this.Controls.Add(new Label
            {
                Text = "CLIPBOARD MANAGER",
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                ForeColor = Color.FromArgb(91, 155, 213),
                Location = new Point(pad, pad),
                Size = new Size(this.ClientSize.Width - pad * 2, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });

            // RECHERCHE
            txtSearch = new TextBox
            {
                Location = new Point(pad, pad + 54),
                Size = new Size(this.ClientSize.Width - pad * 2 - 100, 36),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11f),
                PlaceholderText = "  🔍  Rechercher...",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            txtSearch.TextChanged += (s, e) => { searchText = txtSearch.Text; RefreshList(); };
            this.Controls.Add(txtSearch);

            // BOUTON VIDER
            var btnVider = new Button
            {
                Text = "🗑 Vider",
                Size = new Size(90, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(100, 25, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnVider.Location = new Point(this.ClientSize.Width - pad - 90, pad + 54);
            btnVider.FlatAppearance.BorderSize = 0;
            btnVider.Click += (s, e) =>
            {
                if (MessageBox.Show("Vider tout l'historique ?", "Confirmer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    entries.Clear();
                    SaveHistory();
                    RefreshList();
                }
            };
            this.Controls.Add(btnVider);

            // COMPTEUR
            lblCount = new Label
            {
                Text = "0 élément",
                Location = new Point(pad, pad + 100),
                Size = new Size(this.ClientSize.Width - pad * 2, 22),
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 9f),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(lblCount);

            // LISTE
            pnlList = new Panel
            {
                Location = new Point(pad, pad + 128),
                Size = new Size(this.ClientSize.Width - pad * 2, this.ClientSize.Height - 260),
                BackColor = Color.FromArgb(18, 18, 18),
                AutoScroll = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(pnlList);

            // SEPARATEUR
            var sep = new Panel
            {
                Size = new Size(this.ClientSize.Width - pad * 2, 1),
                BackColor = Color.FromArgb(45, 45, 45),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            sep.Location = new Point(pad, this.ClientSize.Height - 118);
            this.Controls.Add(sep);

            // CHECKBOX LANCER AVEC WINDOWS
            chkLaunch = new CheckBox
            {
                Text = "Lancer avec Windows au démarrage (en arrière-plan)",
                Location = new Point(pad, this.ClientSize.Height - 108),
                AutoSize = true,
                ForeColor = Color.FromArgb(190, 190, 190),
                BackColor = Color.FromArgb(18, 18, 18),
                Font = new Font("Segoe UI", 10f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            chkLaunch.CheckedChanged += (s, e) =>
            {
                var run = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (chkLaunch.Checked)
                    run?.SetValue("ClipboardManager", $"\"{Application.ExecutablePath}\" --startup");
                else
                    run?.DeleteValue("ClipboardManager", false);
                SaveSettings();
            };
            this.Controls.Add(chkLaunch);

            // CHECKBOX SUPPRESSION AUTO
            chkAutoDelete = new CheckBox
            {
                Text = "Supprimer automatiquement les éléments de plus de 30 jours",
                Location = new Point(pad, this.ClientSize.Height - 76),
                AutoSize = true,
                ForeColor = Color.FromArgb(190, 190, 190),
                BackColor = Color.FromArgb(18, 18, 18),
                Font = new Font("Segoe UI", 10f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            chkAutoDelete.CheckedChanged += (s, e) => SaveSettings();
            this.Controls.Add(chkAutoDelete);

            // BOUTON RÉDUIRE
            var btnReduire = new Button
            {
                Text = "Réduire",
                Size = new Size(110, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 55),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnReduire.Location = new Point(this.ClientSize.Width - pad - 110, this.ClientSize.Height - 90);
            btnReduire.FlatAppearance.BorderSize = 0;
            btnReduire.Click += (s, e) => HideWindow();
            this.Controls.Add(btnReduire);
        }

        private void RefreshList()
        {
            if (pnlList == null) return;
            pnlList.SuspendLayout();
            pnlList.Controls.Clear();

            var filtered = entries
                .Where(e => string.IsNullOrEmpty(searchText) ||
                            e.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lblCount != null)
                lblCount.Text = $"{entries.Count} élément{(entries.Count > 1 ? "s" : "")} — {filtered.Count} affiché{(filtered.Count > 1 ? "s" : "")}";

            int y = 0;
            foreach (var entry in filtered)
            {
                var card = MakeCard(entry);
                card.Location = new Point(0, y);
                pnlList.Controls.Add(card);
                y += card.Height + 8;
            }

            pnlList.ResumeLayout();
        }

        private Panel MakeCard(ClipEntry entry)
        {
            int W = Math.Max(pnlList.Width - 20, 300);
            bool isLong = entry.Text.Length > 120 || entry.Text.Contains('\n');
            string preview = entry.Text.Length > 120 ? entry.Text.Substring(0, 120) + "..." : entry.Text;
            string display = preview.Replace("\r\n", " ↵ ").Replace("\n", " ↵ ").Replace("\r", " ↵ ");
            int cardH = 88;

            var card = new Panel
            {
                Size = new Size(W, cardH),
                BackColor = Color.FromArgb(28, 28, 28)
            };

            // Boutons de droite à gauche
            int btnRight = W - 8;

            // ✕ Supprimer
            btnRight -= 46;
            var btnDel = new Button
            {
                Text = "✕",
                Location = new Point(btnRight, cardH / 2 - 17),
                Size = new Size(46, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 20, 20),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDel.FlatAppearance.BorderSize = 0;
            btnDel.Click += (s, e) => { entries.Remove(entry); SaveHistory(); RefreshList(); };
            card.Controls.Add(btnDel);

            // Copier
            btnRight -= 82;
            var btnCopy = new Button
            {
                Text = "Copier",
                Location = new Point(btnRight, cardH / 2 - 17),
                Size = new Size(76, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 100, 200),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCopy.FlatAppearance.BorderSize = 0;
            btnCopy.Click += (s, e) =>
            {
                Clipboard.SetText(entry.Text);
                btnCopy.Text = "✅";
                var t = new System.Windows.Forms.Timer { Interval = 1000 };
                t.Tick += (ts, te) => { btnCopy.Text = "Copier"; t.Stop(); };
                t.Start();
            };
            card.Controls.Add(btnCopy);

            // 👁 Voir (si long)
            if (isLong)
            {
                btnRight -= 50;
                var btnView = new Button
                {
                    Text = "👁",
                    Location = new Point(btnRight, cardH / 2 - 17),
                    Size = new Size(44, 34),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 11f),
                    Cursor = Cursors.Hand
                };
                btnView.FlatAppearance.BorderSize = 0;
                btnView.Click += (s, e) => ShowFullText(entry.Text, entry.Date);
                card.Controls.Add(btnView);
            }

            // Date + nb caractères
            card.Controls.Add(new Label
            {
                Text = $"{entry.Date:dd/MM/yyyy  HH:mm}  —  {entry.Text.Length} car.",
                Location = new Point(10, 8),
                Size = new Size(btnRight - 16, 18),
                ForeColor = Color.FromArgb(90, 90, 90),
                Font = new Font("Segoe UI", 8f)
            });

            // Texte
            card.Controls.Add(new Label
            {
                Text = display,
                Location = new Point(10, 28),
                Size = new Size(btnRight - 16, cardH - 36),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 10f),
                AutoEllipsis = true
            });

            // Séparateur
            card.Controls.Add(new Panel
            {
                Location = new Point(0, cardH - 1),
                Size = new Size(W, 1),
                BackColor = Color.FromArgb(40, 40, 40)
            });

            return card;
        }

        private void ShowFullText(string text, DateTime date)
        {
            var f = new Form
            {
                Text = $"Copié le {date:dd/MM/yyyy à HH:mm}",
                Size = new Size(750, 650),
                Icon = this.Icon,
                BackColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.White,
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("Segoe UI", 10f),
                MinimumSize = new Size(400, 400)
            };

            var tb = new RichTextBox
            {
                Text = text,
                Location = new Point(16, 16),
                Size = new Size(702, 520),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                Font = new Font("Segoe UI", 11f),
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            f.Controls.Add(tb);

            var btnCopy = new Button
            {
                Text = "📋 Copier le texte entier",
                Location = new Point(16, 550),
                Size = new Size(200, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 100, 200),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnCopy.FlatAppearance.BorderSize = 0;
            btnCopy.Click += (s, e) => { Clipboard.SetText(text); f.Close(); };
            f.Controls.Add(btnCopy);

            var btnClose = new Button
            {
                Text = "Fermer",
                Location = new Point(618, 550),
                Size = new Size(110, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 55),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => f.Close();
            f.Controls.Add(btnClose);

            f.ShowDialog(this);
        }

        private void SetupClipWatcher()
        {
            clipWatcher = new System.Windows.Forms.Timer { Interval = 500 };
            clipWatcher.Tick += (s, e) =>
            {
                try
                {
                    if (!Clipboard.ContainsText()) return;
                    var text = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text) || text == lastClip) return;
                    lastClip = text;
                    if (entries.Count > 0 && entries[0].Text == text) return;
                    entries.Insert(0, new ClipEntry { Text = text, Date = DateTime.Now });
                    if (entries.Count > 500) entries = entries.Take(500).ToList();
                    if (chkAutoDelete?.Checked == true)
                        entries.RemoveAll(e => (DateTime.Now - e.Date).TotalDays > 30);
                    SaveHistory();
                    if (this.Visible) RefreshList();
                }
                catch { }
            };
            clipWatcher.Start();
        }

        private void SaveHistory()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saveFile)!);
                File.WriteAllText(saveFile, JsonSerializer.Serialize(entries));
            }
            catch { }
        }

        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(saveFile)) return;
                entries = JsonSerializer.Deserialize<List<ClipEntry>>(File.ReadAllText(saveFile)) ?? new();
                if (chkAutoDelete?.Checked == true)
                    entries.RemoveAll(e => (DateTime.Now - e.Date).TotalDays > 30);
            }
            catch { entries = new(); }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new { LaunchWithWindows = chkLaunch.Checked, AutoDelete = chkAutoDelete.Checked };
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
                File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings));
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsFile)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsFile));
                chkLaunch.Checked = doc.RootElement.GetProperty("LaunchWithWindows").GetBoolean();
                chkAutoDelete.Checked = doc.RootElement.GetProperty("AutoDelete").GetBoolean();
            }
            catch { }
        }

        private void SetupTray()
        {
            tray = new NotifyIcon
            {
                Icon = this.Icon ?? SystemIcons.Application,
                Text = "Clipboard Manager",
                Visible = true
            };
            var m = new ContextMenuStrip();
            m.Items.Add("Ouvrir", null, (s, e) => ShowWindow());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Quitter", null, (s, e) => Quit());
            tray.ContextMenuStrip = m;
            tray.DoubleClick += (s, e) => ShowWindow();
        }

        public void ShowFromExternal()
        {
            if (this.InvokeRequired)
                this.Invoke(new Action(ShowWindow));
            else
                ShowWindow();
        }

        private void ShowWindow()
        {
            this.ShowInTaskbar = true;
            this.Visible = true;
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
            RefreshList();
        }

        private void HideWindow()
        {
            this.Visible = false;
            this.ShowInTaskbar = false;
        }

        private void Quit()
        {
            clipWatcher?.Stop();
            tray.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideWindow(); }
            else base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
        }
    }
}