using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AStockTicker
{
    public class SettingsForm : Form
    {
        private readonly AppConfig _draft;
        private readonly List<StockItem> _stocks = new List<StockItem>();
        private readonly float _scale;

        private ListBox _list;
        private TextBox _codeBox;
        private Button _addButton;
        private Button _removeButton;
        private Button _upButton;
        private Button _downButton;
        private NumericUpDown _refresh;
        private NumericUpDown _fontSize;
        private NumericUpDown _carousel;
        private CheckBox _hideClosed;
        private CheckBox _autoStart;
        private ListBox _suggestList;
        private Timer _suggestTimer;
        private Timer _suggestHideTimer;
        private readonly List<StockSuggestion> _suggestions = new List<StockSuggestion>();
        private int _suggestSeq;

        public SettingsForm(AppConfig config)
        {
            _draft = config;
            foreach (StockItem s in config.Stocks) _stocks.Add(s.Clone());
            _scale = DetectDpiScale();

            Text = "自选股设置 - A股行情条";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = Sz(600, 500);

            BuildUi();
            LoadValues();
            ReloadList();
        }

        private static float DetectDpiScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1f, Math.Min(2.5f, g.DpiX / 96f));
            }
            catch { return 1f; }
        }

        private int S(int value) { return (int)Math.Round(value * _scale); }
        private Point P(int x, int y) { return new Point(S(x), S(y)); }
        private Size Sz(int w, int h) { return new Size(S(w), S(h)); }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = "自选股与轮播设置";
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = P(22, 12);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "任务栏一次显示两只股票（上下各一只），按下面的顺序成组轮播；悬停时会暂停。";
            subtitle.ForeColor = Color.FromArgb(95, 105, 120);
            subtitle.AutoSize = true;
            subtitle.Location = P(24, 44);
            Controls.Add(subtitle);

            _list = new ListBox();
            _list.Location = P(22, 68);
            _list.Size = Sz(556, 150);
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = Math.Max(S(24), _list.Font.Height + S(10));
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.DrawItem += ListDrawItem;
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.Add(_list);

            _codeBox = new TextBox();
            _codeBox.Location = P(24, 228);
            _codeBox.Size = Sz(208, 28);
            _codeBox.Font = new Font("Microsoft YaHei UI", 9.5F);
            _codeBox.TextChanged += delegate { OnCodeTextChanged(); };
            _codeBox.Leave += delegate { StartSuggestHide(); };
            Controls.Add(_codeBox);

            _suggestList = new ListBox();
            _suggestList.Location = P(24, 258);
            _suggestList.Size = Sz(300, 0);
            _suggestList.Font = new Font("Microsoft YaHei UI", 9.5F);
            _suggestList.DrawMode = DrawMode.OwnerDrawFixed;
            _suggestList.ItemHeight = Math.Max(S(22), _suggestList.Font.Height + S(6));
            _suggestList.BorderStyle = BorderStyle.FixedSingle;
            _suggestList.IntegralHeight = false;
            _suggestList.Visible = false;
            _suggestList.DrawItem += SuggestDrawItem;
            _suggestList.MouseDown += OnSuggestionMouseDown;
            _suggestList.KeyDown += OnSuggestionKeyDown;
            _suggestList.Leave += delegate { StartSuggestHide(); };
            Controls.Add(_suggestList);

            _addButton = MakeButton("添加", P(240, 227), Sz(86, 31));
            _addButton.Click += delegate { AddFromBox(); };
            Controls.Add(_addButton);

            _removeButton = MakeButton("删除", P(334, 227), Sz(86, 31));
            _removeButton.Click += delegate { RemoveSelected(); };
            Controls.Add(_removeButton);

            _upButton = MakeButton("上移", P(24, 266), Sz(78, 29));
            _upButton.Click += delegate { MoveSelected(-1); };
            Controls.Add(_upButton);

            _downButton = MakeButton("下移", P(110, 266), Sz(78, 29));
            _downButton.Click += delegate { MoveSelected(1); };
            Controls.Add(_downButton);

            Label codeHint = new Label();
            codeHint.Text = "支持代码、中文名或拼音搜索：600519、茅台、maotai（最多 12 只）";
            codeHint.ForeColor = Color.FromArgb(120, 130, 145);
            codeHint.AutoSize = true;
            codeHint.Location = P(202, 272);
            Controls.Add(codeHint);

            GroupBox group = new GroupBox();
            group.Text = "显示与轮播";
            group.Location = P(22, 308);
            group.Size = Sz(556, 145);
            Controls.Add(group);

            Label refreshLabel = new Label();
            refreshLabel.Text = "刷新间隔";
            refreshLabel.AutoSize = true;
            refreshLabel.Location = P(16, 31);
            group.Controls.Add(refreshLabel);

            _refresh = new NumericUpDown();
            _refresh.Minimum = 1;
            _refresh.Maximum = 30;
            _refresh.Location = P(84, 27);
            _refresh.Size = Sz(64, 27);
            group.Controls.Add(_refresh);

            Label secLabel = new Label();
            secLabel.Text = "秒";
            secLabel.AutoSize = true;
            secLabel.Location = P(152, 31);
            group.Controls.Add(secLabel);

            Label fontLabel = new Label();
            fontLabel.Text = "字号";
            fontLabel.AutoSize = true;
            fontLabel.Location = P(204, 31);
            group.Controls.Add(fontLabel);

            _fontSize = new NumericUpDown();
            _fontSize.Minimum = 8;
            _fontSize.Maximum = 14;
            _fontSize.Location = P(244, 27);
            _fontSize.Size = Sz(58, 27);
            group.Controls.Add(_fontSize);

            Label carouselLabel = new Label();
            carouselLabel.Text = "轮播间隔";
            carouselLabel.AutoSize = true;
            carouselLabel.Location = P(326, 31);
            group.Controls.Add(carouselLabel);

            _carousel = new NumericUpDown();
            _carousel.Minimum = 2;
            _carousel.Maximum = 30;
            _carousel.Location = P(396, 27);
            _carousel.Size = Sz(58, 27);
            group.Controls.Add(_carousel);

            Label carouselSec = new Label();
            carouselSec.Text = "秒";
            carouselSec.AutoSize = true;
            carouselSec.Location = P(458, 31);
            group.Controls.Add(carouselSec);

            _hideClosed = new CheckBox();
            _hideClosed.Text = "休市 / 午休时隐藏";
            _hideClosed.AutoSize = true;
            _hideClosed.Location = P(16, 70);
            group.Controls.Add(_hideClosed);

            _autoStart = new CheckBox();
            _autoStart.Text = "开机自动启动";
            _autoStart.AutoSize = true;
            _autoStart.Location = P(326, 70);
            group.Controls.Add(_autoStart);

            Label groupHint = new Label();
            groupHint.Text = "字号 8–14 可调，两行自动适配任务栏高度；盘中约每 3 秒刷新。";
            groupHint.ForeColor = Color.FromArgb(120, 130, 145);
            groupHint.AutoSize = true;
            groupHint.Location = P(16, 105);
            group.Controls.Add(groupHint);

            Button save = MakeButton("保存", P(376, 458), Sz(96, 34));
            save.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            save.Click += delegate { SaveAndClose(); };
            Controls.Add(save);
            AcceptButton = save;

            Button cancel = MakeButton("取消", P(482, 458), Sz(96, 34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            CancelButton = cancel;

            Label path = new Label();
            path.Text = "配置：" + ConfigPathShort();
            path.ForeColor = Color.FromArgb(150, 158, 170);
            path.AutoSize = true;
            path.Location = P(24, 468);
            Controls.Add(path);

            _suggestTimer = new Timer();
            _suggestTimer.Interval = 300;
            _suggestTimer.Tick += OnSuggestTimerTick;

            _suggestHideTimer = new Timer();
            _suggestHideTimer.Interval = 150;
            _suggestHideTimer.Tick += OnSuggestHideTick;

            FormClosed += delegate
            {
                _suggestTimer.Dispose();
                _suggestHideTimer.Dispose();
            };
        }

        private string ConfigPathShort()
        {
            string p = _draft.ConfigPath;
            if (p != null && p.Length > 38) return "..." + p.Substring(p.Length - 35);
            return p;
        }

        private Button MakeButton(string text, Point location, Size size)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = location;
            b.Size = size;
            b.UseVisualStyleBackColor = true;
            return b;
        }

        private void LoadValues()
        {
            _refresh.Value = _draft.RefreshSeconds;
            _fontSize.Value = _draft.FontSize;
            _carousel.Value = _draft.CarouselSeconds;
            _hideClosed.Checked = _draft.HideWhenClosed;
            _autoStart.Checked = _draft.AutoStart;
        }

        private void ReloadList()
        {
            int selected = _list.SelectedIndex;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (StockItem s in _stocks) _list.Items.Add(s);
            _list.EndUpdate();
            if (_stocks.Count > 0)
                _list.SelectedIndex = Math.Max(0, Math.Min(selected, _stocks.Count - 1));
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            int i = _list.SelectedIndex;
            _removeButton.Enabled = i >= 0;
            _upButton.Enabled = i > 0;
            _downButton.Enabled = i >= 0 && i < _stocks.Count - 1;
        }

        private void ListDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _stocks.Count) return;
            DrawCodeNameItem(e, _stocks[e.Index].Code, _stocks[e.Index].Name);
        }

        private void SuggestDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _suggestions.Count) return;
            DrawCodeNameItem(e, _suggestions[e.Index].Code, _suggestions[e.Index].Name);
        }

        private void DrawCodeNameItem(DrawItemEventArgs e, string code, string name)
        {
            e.DrawBackground();
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color codeColor = selected ? Color.White : Color.FromArgb(120, 132, 148);
            Color nameColor = selected ? Color.White : Color.FromArgb(32, 38, 46);
            int x = e.Bounds.X + S(10);
            int codeWidth = Math.Max(S(80), e.Bounds.Width / 3);
            Rectangle codeRect = new Rectangle(x, e.Bounds.Y, codeWidth, e.Bounds.Height);
            Rectangle nameRect = new Rectangle(x + codeWidth, e.Bounds.Y,
                Math.Max(S(10), e.Bounds.Width - codeWidth - S(12)), e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, code, e.Font, codeRect, codeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (Font bold = new Font(e.Font, FontStyle.Bold))
            {
                TextRenderer.DrawText(e.Graphics, name, bold, nameRect, nameColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);
            }
            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
                ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds);
        }

        private void OnCodeTextChanged()
        {
            _suggestTimer.Stop();
            HideSuggestions();
            string raw = _codeBox.Text.Trim();
            if (raw.Length == 0) return;
            if (QuoteService.NormalizeCode(raw) != null) return;
            _suggestTimer.Start();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && (_codeBox.Focused || _suggestList.Focused))
            {
                AddFromBox();
                return true;
            }
            if (keyData == Keys.Escape && _suggestList.Visible)
            {
                HideSuggestions();
                _codeBox.Focus();
                return true;
            }
            if ((keyData == Keys.Down || keyData == Keys.Up) && _codeBox.Focused &&
                _suggestList.Visible && _suggestList.Items.Count > 0)
            {
                _suggestList.SelectedIndex = keyData == Keys.Down ? 0 : _suggestList.Items.Count - 1;
                _suggestList.Focus();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnSuggestionMouseDown(object sender, MouseEventArgs e)
        {
            int index = _suggestList.IndexFromPoint(e.Location);
            if (index < 0 || index >= _suggestList.Items.Count) return;
            AddSuggestion((StockSuggestion)_suggestList.Items[index]);
        }

        private void OnSuggestionKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && _suggestList.SelectedIndex <= 0)
            {
                e.SuppressKeyPress = true;
                _codeBox.Focus();
            }
        }

        private void OnSuggestTimerTick(object sender, EventArgs e)
        {
            _suggestTimer.Stop();
            string raw = _codeBox.Text.Trim();
            if (raw.Length == 0 || QuoteService.NormalizeCode(raw) != null)
            {
                HideSuggestions();
                return;
            }

            int seq = ++_suggestSeq;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<StockSuggestion> found = null;
                try { found = QuoteService.Search(raw, 8); }
                catch { }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(new Action(delegate { OnSuggestions(seq, raw, found); }));
                }
                catch { }
            });
        }

        private void OnSuggestions(int seq, string query, List<StockSuggestion> found)
        {
            if (IsDisposed || seq != _suggestSeq) return;
            if (!string.Equals(_codeBox.Text.Trim(), query, StringComparison.Ordinal)) return;

            _suggestions.Clear();
            if (found != null) _suggestions.AddRange(found);
            ShowSuggestions();
        }

        private void ShowSuggestions()
        {
            if (_suggestions.Count == 0)
            {
                HideSuggestions();
                return;
            }

            int count = Math.Min(_suggestions.Count, 8);
            _suggestList.BeginUpdate();
            _suggestList.Items.Clear();
            for (int i = 0; i < count; i++) _suggestList.Items.Add(_suggestions[i]);
            _suggestList.EndUpdate();

            _suggestList.Width = S(300);
            _suggestList.Height = count * _suggestList.ItemHeight + S(6);
            _suggestList.Visible = true;
            _suggestList.BringToFront();
            _suggestList.SelectedIndex = -1;
        }

        private void HideSuggestions()
        {
            _suggestSeq++;
            _suggestions.Clear();
            if (_suggestList == null) return;
            _suggestList.Visible = false;
            _suggestList.Items.Clear();
        }

        private void StartSuggestHide()
        {
            _suggestHideTimer.Stop();
            _suggestHideTimer.Start();
        }

        private void OnSuggestHideTick(object sender, EventArgs e)
        {
            _suggestHideTimer.Stop();
            if (_codeBox.Focused || _suggestList.Focused) return;
            HideSuggestions();
        }

        private bool EnsureCanAdd(string code)
        {
            foreach (StockItem s in _stocks)
            {
                if (string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "该股票已经在自选列表中。", "重复添加",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            }
            if (_stocks.Count >= AppConfig.MaxStocks)
            {
                MessageBox.Show(this, "最多只能添加 " + AppConfig.MaxStocks + " 只股票。", "数量已满",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        private void AddStock(string code, string name)
        {
            _stocks.Add(new StockItem(code, name));
            ReloadList();
            _list.SelectedIndex = _stocks.Count - 1;
            _codeBox.Clear();
            HideSuggestions();
            _codeBox.Focus();
        }

        private void AddSuggestion(StockSuggestion suggestion)
        {
            if (suggestion == null || !EnsureCanAdd(suggestion.Code)) return;
            AddStock(suggestion.Code, suggestion.Name);
        }

        private void AddFromBox()
        {
            string raw = _codeBox.Text.Trim();
            if (raw.Length == 0)
            {
                _codeBox.Focus();
                return;
            }

            if (_suggestList.Visible && _suggestList.Items.Count > 0)
            {
                int index = _suggestList.SelectedIndex;
                if (index < 0) index = 0;
                AddSuggestion((StockSuggestion)_suggestList.Items[index]);
                return;
            }

            string code = QuoteService.NormalizeCode(raw);
            if (code != null)
            {
                if (!EnsureCanAdd(code)) return;

                Cursor previous = Cursor;
                Cursor = Cursors.WaitCursor;
                _addButton.Enabled = false;
                Quote quote = null;
                try { quote = QuoteService.FetchOne(code); }
                catch { }
                finally
                {
                    Cursor = previous;
                    _addButton.Enabled = true;
                }

                if (quote == null)
                {
                    MessageBox.Show(this, "无法获取该代码的行情，请检查代码或网络后重试。", "添加失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                AddStock(code, quote.Name);
                return;
            }

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            List<StockSuggestion> found = null;
            try { found = QuoteService.Search(raw, 8); }
            catch { }
            finally { Cursor = previousCursor; }

            if (found == null || found.Count == 0)
            {
                MessageBox.Show(this,
                    "未找到与“" + raw + "”匹配的股票，或搜索服务暂时不可用。\r\n\r\n" +
                    "可输入代码、中文名或拼音，例如 600519、茅台、maotai。",
                    "未找到", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddSuggestion(found[0]);
        }

        private void RemoveSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            _stocks.RemoveAt(i);
            ReloadList();
            if (_stocks.Count > 0) _list.SelectedIndex = Math.Min(i, _stocks.Count - 1);
        }

        private void MoveSelected(int delta)
        {
            int i = _list.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _stocks.Count) return;
            StockItem tmp = _stocks[i];
            _stocks[i] = _stocks[j];
            _stocks[j] = tmp;
            ReloadList();
            _list.SelectedIndex = j;
        }

        private void SaveAndClose()
        {
            if (_stocks.Count == 0)
            {
                MessageBox.Show(this, "请至少保留一只股票。", "无法保存",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _draft.Stocks = new List<StockItem>();
            foreach (StockItem s in _stocks) _draft.Stocks.Add(s.Clone());
            _draft.RefreshSeconds = (int)_refresh.Value;
            _draft.CarouselSeconds = (int)_carousel.Value;
            _draft.FontSize = (int)_fontSize.Value;
            _draft.HideWhenClosed = _hideClosed.Checked;
            _draft.AutoStart = _autoStart.Checked;

            bool autoOk = StartupManager.Set(_draft.AutoStart);
            try
            {
                _draft.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存配置失败：" + ex.Message, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!autoOk && _draft.AutoStart)
            {
                MessageBox.Show(this, "配置已保存，但写入开机启动项失败。\r\n请尝试把程序放到普通用户目录后重试。",
                    "开机启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
