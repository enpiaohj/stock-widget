using System.Windows;
using System.Windows.Input;

namespace StockWidget.App.Views;

public partial class InputDialog : Window
{
    /// <summary>用户输入内容（确定后有效）。</summary>
    public string Input => InputBox.Text.Trim();

    public InputDialog(string title, string prompt, string? hint = null)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        HintText.Text = hint ?? "";
        Loaded += (_, _) => InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => CloseWithResult();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CloseWithResult();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void CloseWithResult()
    {
        if (Input.Length == 0) return;
        DialogResult = true;
        Close();
    }
}
