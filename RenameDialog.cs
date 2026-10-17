using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindicatorPlus
{
    public class RenameDialog : Form
    {
        private TextBox _txtInput;
        private Button _btnOk;
        private Button _btnCancel;
        public string DesktopName { get; private set; }

        public RenameDialog(int desktopIndex, string currentName)
        {
            Text = string.Format("重命名桌面 {0}", desktopIndex);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Width = 360;
            Height = 175;
            TopMost = true;

            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular);

            Label lbl = new Label
            {
                Text = string.Format("请输入虚拟桌面 {0} 的显示别名：", desktopIndex),
                Location = new Point(20, 18),
                AutoSize = true
            };
            Controls.Add(lbl);

            _txtInput = new TextBox
            {
                Text = currentName ?? "",
                Location = new Point(22, 48),
                Width = 300,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular)
            };
            Controls.Add(_txtInput);

            _btnOk = new Button
            {
                Text = "保存",
                DialogResult = DialogResult.OK,
                Location = new Point(155, 92),
                Width = 80,
                Height = 30
            };
            _btnOk.Click += (s, e) =>
            {
                DesktopName = _txtInput.Text.Trim();
                Close();
            };
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(242, 92),
                Width = 80,
                Height = 30
            };
            _btnCancel.Click += (s, e) => Close();
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _txtInput.Focus();
            _txtInput.SelectAll();
        }
    }
}
