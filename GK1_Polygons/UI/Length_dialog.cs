using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PolygonEditor.UI
{
    public sealed class LengthDialog : Form
    {
        private readonly TextBox _inputBox;
        public double SelectedLength { get; private set; }

        public LengthDialog(double currentLength)
        {
            Text = "Zadana długość krawędzi";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(320, 135);

            Label lbl = new Label
            {
                Text = "Podaj nową długość krawędzi (w pikselach):",
                Location = new Point(15, 15),
                AutoSize = true
            };

            _inputBox = new TextBox
            {
                Text = Math.Round(currentLength, 1).ToString(CultureInfo.InvariantCulture),
                Location = new Point(15, 42),
                Width = 285
            };

            Button btnOk = new Button
            {
                Text = "Zatwierdź",
                DialogResult = DialogResult.OK,
                Location = new Point(135, 85),
                Width = 80
            };

            Button btnCancel = new Button
            {
                Text = "Anuluj",
                DialogResult = DialogResult.Cancel,
                Location = new Point(220, 85),
                Width = 80
            };

            btnOk.Click += (s, e) =>
            {
                string raw = _inputBox.Text.Trim().Replace(',', '.');
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double val) && val >= 10.0 && val <= 3000.0)
                {
                    SelectedLength = val;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        "Podaj poprawną dodatnią liczbę z zakresu [10 .. 3000].",
                        "Błąd walidacji",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lbl, _inputBox, btnOk, btnCancel });
        }
    }
}