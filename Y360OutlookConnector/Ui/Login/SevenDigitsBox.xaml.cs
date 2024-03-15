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
    /// Interaction logic for SevenDigitsBox.xaml
    /// </summary>
    public partial class SevenDigitsBox
    {
        public class TextEnteredArgs : EventArgs
        {
            public string Text { get; set; }
        }

        public event EventHandler<TextEnteredArgs> TextEntered;
        public event EventHandler TextChanged;

        private readonly TextBox[] _digitsBoxes;

        public SevenDigitsBox()
        {
            InitializeComponent();

            _digitsBoxes = new[]
            {
                DigitBox1,
                DigitBox2,
                DigitBox3,
                DigitBox4,
                DigitBox5,
                DigitBox6,
                DigitBox7,
            };

            Array.ForEach(_digitsBoxes, x => x.DataContext = new DigitBoxData());
        }

        public void SetAlarmed(bool value) 
        { 
            foreach (var item in _digitsBoxes)
            {
                if (item.DataContext is DigitBoxData data)
                    data.IsAlarmed = value;
            }
        }

        private void SetCaretPos(int caretIndex)
        {
            if (caretIndex < 0) caretIndex = 0;
            if (caretIndex > _digitsBoxes.Length) caretIndex = _digitsBoxes.Length;

            if (caretIndex < _digitsBoxes.Length)
            {
                var numberBox = _digitsBoxes[caretIndex];
                numberBox.Focus();
                numberBox.SelectAll();
            }
            else
            {
                var numberBox = _digitsBoxes[caretIndex - 1];
                numberBox.Focus();
                if (!String.IsNullOrEmpty(numberBox.Text))
                {
                    numberBox.CaretIndex = numberBox.Text.Length;
                }
                else
                {
                    numberBox.SelectAll();
                }
            }
        }

        private int GetCaretPos(TextBox numberBox)
        {
            int index = Array.IndexOf(_digitsBoxes, numberBox);
            if (index == _digitsBoxes.Length - 1)
            {
                if (!String.IsNullOrEmpty(numberBox.Text) 
                    && numberBox.SelectionLength == 0)
                    index += 1;
            }
            return index;
        }

        private bool TryGetCode(out string str)
        {
            var stringBuilder = new StringBuilder();
            str = "";
            foreach (var item in _digitsBoxes)
            {
                var text = item.Text.Trim();
                if (String.IsNullOrEmpty(text))
                    return false;
                stringBuilder.Append(text);
            }
            str = stringBuilder.ToString();
            return true;
        }

        private void DigitBox_Paste(object sender, DataObjectPastingEventArgs e)
        {
            var sourceDataObject = e.SourceDataObject;
            if (!sourceDataObject.GetDataPresent(DataFormats.UnicodeText, true)) return;

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            text = text?.Trim();
            if (String.IsNullOrEmpty(text)) return;

            if (text.Length <= _digitsBoxes.Length && text.All(Char.IsDigit))
            {
                int i = (text.Length == _digitsBoxes.Length) ? 0 : Array.IndexOf(_digitsBoxes, sender);
                int j = 0;
                while (i < _digitsBoxes.Length && j < text.Length)
                {
                    _digitsBoxes[i].Text = Char.ToString(text[j]);
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

        private void DigitBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox numberBox)) return;

            if (e.Key == Key.Left)
            {
                SetCaretPos(GetCaretPos(numberBox) - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                SetCaretPos(GetCaretPos(numberBox) + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                if (!String.IsNullOrEmpty(numberBox.Text))
                {
                    numberBox.Text = String.Empty;
                }
                else
                {
                    int index = Array.IndexOf(_digitsBoxes, numberBox);
                    if (index > 0)
                    {
                        _digitsBoxes[index - 1].Text = String.Empty;
                        _digitsBoxes[index - 1].Focus();
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

        private void DigitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!(sender is TextBox textBox)) return;

            var caretPos = GetCaretPos(textBox);
            if (caretPos >= _digitsBoxes.Length) return;

            if (e.Text.Length == 1 && Char.IsDigit(e.Text[0]))
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

        private void DigitBox_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            var textBox = (sender as TextBox);
            if (textBox == null) return;

            textBox.SelectAll();
            textBox.Focus();
            e.Handled = true;
        }

        public class DigitBoxData : INotifyPropertyChanged
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
