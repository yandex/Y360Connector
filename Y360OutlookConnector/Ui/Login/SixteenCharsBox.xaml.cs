using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Y360OutlookConnector.Ui.Login
{
    /// <summary>
    /// Interaction logic for SixteenCharsBox.xaml
    /// </summary>
    public partial class SixteenCharsBox
    {
        public event EventHandler<TextEnteredArgs> TextEntered;
        public event EventHandler TextChanged;

        private readonly TextBox[] _charsBoxes;

        public SixteenCharsBox()
        {
            InitializeComponent();

            _charsBoxes = new[]
            {
                CharBox1,
                CharBox2,
                CharBox3,
                CharBox4,
                CharBox5,
                CharBox6,
                CharBox7,
                CharBox8,
                CharBox9,
                CharBox10,
                CharBox11,
                CharBox12,
                CharBox13,
                CharBox14,
                CharBox15,
                CharBox16
            };

            Array.ForEach(_charsBoxes, x => x.DataContext = new CharBoxData());
        }

        public void SetAlarmed(bool value) 
        { 
            foreach (var item in _charsBoxes)
            {
                if (item.DataContext is CharBoxData data)
                    data.IsAlarmed = value;
            }
        }

        private void SetCaretPos(int caretIndex)
        {
            if (caretIndex < 0) caretIndex = 0;
            if (caretIndex > _charsBoxes.Length) caretIndex = _charsBoxes.Length;

            if (caretIndex < _charsBoxes.Length)
            {
                var charBox = _charsBoxes[caretIndex];
                charBox.Focus();
                charBox.SelectAll();
            }
            else
            {
                var charBox = _charsBoxes[caretIndex - 1];
                charBox.Focus();
                if (!String.IsNullOrEmpty(charBox.Text))
                {
                    charBox.CaretIndex = charBox.Text.Length;
                }
                else
                {
                    charBox.SelectAll();
                }
            }
        }

        private int GetCaretPos(TextBox charBox)
        {
            int index = Array.IndexOf(_charsBoxes, charBox);
            if (index == _charsBoxes.Length - 1)
            {
                if (!String.IsNullOrEmpty(charBox.Text) 
                    && charBox.SelectionLength == 0)
                    index += 1;
            }
            return index;
        }

        private bool TryGetCode(out string str)
        {
            var stringBuilder = new StringBuilder();
            str = "";
            foreach (var item in _charsBoxes)
            {
                var text = item.Text.Trim();
                if (String.IsNullOrEmpty(text))
                    return false;
                stringBuilder.Append(text);
            }
            str = stringBuilder.ToString();
            return true;
        }

        private void CharBox_Paste(object sender, DataObjectPastingEventArgs e)
        {
            var sourceDataObject = e.SourceDataObject;
            if (!sourceDataObject.GetDataPresent(DataFormats.UnicodeText, true)) return;

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            text = text?.Trim();
            if (String.IsNullOrEmpty(text)) return;

            if (text.Length <= _charsBoxes.Length && text.All(Char.IsLetterOrDigit))
            {
                int i = (text.Length == _charsBoxes.Length) ? 0 : Array.IndexOf(_charsBoxes, sender);
                int j = 0;
                while (i < _charsBoxes.Length && j < text.Length)
                {
                    _charsBoxes[i].Text = Char.ToString(text[j]);
                    i += 1;
                    j += 1;
                }
                SetCaretPos(i);
            }

            e.CancelCommand();

            TextChanged?.Invoke(this, EventArgs.Empty);
            if (TryGetCode(out string code)) {
                TextEntered?.Invoke(this, new TextEnteredArgs{ Text = code });
            }
        }

        private void CharBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox charBox)) return;

            if (e.Key == Key.Left)
            {
                SetCaretPos(GetCaretPos(charBox) - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                SetCaretPos(GetCaretPos(charBox) + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                if (!String.IsNullOrEmpty(charBox.Text))
                {
                    charBox.Text = String.Empty;
                }
                else
                {
                    int index = Array.IndexOf(_charsBoxes, charBox);
                    if (index > 0)
                    {
                        _charsBoxes[index - 1].Text = String.Empty;
                        _charsBoxes[index - 1].Focus();
                    }
                }
                e.Handled = true;
                TextChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (e.Key == Key.Up || e.Key == Key.Down 
                || e.Key == Key.PageDown || e.Key == Key.PageUp
                || e.Key == Key.Home || e.Key == Key.End)
            {
                e.Handled = true;
            }
        }

        private void CharBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!(sender is TextBox textBox)) return;

            var caretPos = GetCaretPos(textBox);
            if (caretPos >= _charsBoxes.Length) return;

            if (e.Text.Length == 1 && Char.IsLetterOrDigit(e.Text[0]))
            {
                textBox.Text = e.Text;
                textBox.SelectAll();

                SetCaretPos(GetCaretPos(textBox) + 1);
            }

            e.Handled = true;

            TextChanged?.Invoke(this, EventArgs.Empty);
            if (TryGetCode(out var code)) {
                TextEntered?.Invoke(this, new TextEnteredArgs{ Text = code });
            }
        }

        private void CharBox_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                return;
            }

            textBox.SelectAll();
            textBox.Focus();
            e.Handled = true;
        }

        public class CharBoxData : INotifyPropertyChanged
        {
            private bool _isAlarmed = false;
            public bool IsAlarmed { 
                get => _isAlarmed;
                set
                {
                    _isAlarmed = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
