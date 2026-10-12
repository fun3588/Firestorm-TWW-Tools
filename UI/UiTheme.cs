using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FirestormTwwMonitor.UI
{
    internal static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(241, 245, 249);  // Slate 100
        public static readonly Color Navy = Color.FromArgb(15, 23, 42);           // Slate 900
        public static readonly Color NavyCard = Color.FromArgb(30, 41, 59);       // Slate 800
        public static readonly Color Ink = Color.FromArgb(30, 41, 59);            // Slate 800
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);       // Slate 500
        public static readonly Color Border = Color.FromArgb(203, 213, 225);      // Slate 300
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);        // Blue 600
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);   // Blue 700
        public static readonly Color Success = Color.FromArgb(16, 185, 129);      // Emerald 500
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);        // Red 600
        public static readonly Color DangerHover = Color.FromArgb(185, 28, 28);   // Red 700
        public static readonly Color CardBg = Color.White;
        public static readonly Color RowAlt = Color.FromArgb(248, 250, 252);      // Slate 50
        public static readonly Color SelectedRow = Color.FromArgb(219, 234, 254); // Blue 100
        public static readonly Color SelectedText = Color.FromArgb(30, 64, 175);  // Blue 800

        public static void StyleButton(Button button, bool primary = false, bool danger = false)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.BackColor = primary ? Accent : danger ? Danger : Color.White;
            button.ForeColor = (primary || danger) ? Color.White : Ink;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : danger ? DangerHover : Color.FromArgb(226, 232, 240);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(30, 64, 175) : Color.FromArgb(203, 213, 225);
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Microsoft YaHei UI", 9f, (primary || danger) ? FontStyle.Bold : FontStyle.Regular);
            button.Height = Math.Max(button.Height, 32);
        }

        public static void StyleList(ListView list)
        {
            list.BackColor = Color.White;
            list.ForeColor = Ink;
            list.BorderStyle = BorderStyle.None;
            list.Font = new Font("Microsoft YaHei UI", 9f);
            list.OwnerDraw = true;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.View = View.Details;

            list.DrawColumnHeader += (_, e) =>
            {
                using var background = new SolidBrush(Color.FromArgb(241, 245, 249));
                e.Graphics.FillRectangle(background, e.Bounds);

                using var borderPen = new Pen(Border);
                e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

                TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                    Rectangle.Inflate(e.Bounds, -6, 0), Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };

            list.DrawItem += (_, _) => { };

            list.DrawSubItem += (_, e) =>
            {
                var selected = e.Item?.Selected == true;
                using var background = new SolidBrush(selected ? SelectedRow :
                    e.ItemIndex % 2 == 0 ? Color.White : RowAlt);
                e.Graphics.FillRectangle(background, e.Bounds);

                var color = selected ? SelectedText : Ink;
                string text = e.SubItem?.Text ?? "";

                // 若开启了 CheckBoxes 且处于第 0 列，绘制勾选框
                if (list.CheckBoxes && e.ColumnIndex == 0)
                {
                    bool isChecked = e.Item?.Checked == true;
                    var cbState = isChecked
                        ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                        : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
                    var glyphSize = CheckBoxRenderer.GetGlyphSize(e.Graphics, cbState);
                    var cbPoint = new Point(e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - glyphSize.Height) / 2);
                    CheckBoxRenderer.DrawCheckBox(e.Graphics, cbPoint, cbState);

                    var textRect = new Rectangle(e.Bounds.Left + glyphSize.Width + 8, e.Bounds.Top,
                        e.Bounds.Width - glyphSize.Width - 10, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, text, list.Font, textRect, color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    return;
                }

                // 状态高亮着色
                if (text.Contains("在线") || text.Contains("🟢"))
                {
                    color = Color.FromArgb(5, 150, 105);
                }
                else if (text.Contains("离线") || text.Contains("🔴"))
                {
                    color = Color.FromArgb(220, 38, 38);
                }
                else if (text.Contains("重连") || text.Contains("检测"))
                {
                    color = Color.FromArgb(217, 119, 6);
                }

                TextRenderer.DrawText(e.Graphics, text, list.Font,
                    Rectangle.Inflate(e.Bounds, -6, 0), color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }
    }
}
