using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockWidget.Core.Models;

namespace StockWidget.App.Views;

public partial class InputDialog : Window
{
    /// <summary>用户输入内容（确定后有效）。</summary>
    public string Input => InputBox.Text.Trim();

    /// <summary>提交的候选；若非空，Input 为其已归一化代码。</summary>
    public StockSearchMatch? SelectedMatch { get; private set; }

    private readonly Func<string, Task<IReadOnlyList<StockSearchMatch>>>? _searchSource;
    private CancellationTokenSource? _searchCts;
    private readonly object _searchLock = new();

    public InputDialog(string title, string prompt, string? hint = null,
        Func<string, Task<IReadOnlyList<StockSearchMatch>>>? searchSource = null)
    {
        _searchSource = searchSource;
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        HintText.Text = hint ?? "";
        Loaded += (_, _) => InputBox.Focus();
    }

    private async void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchSource is null) { MatchList.Visibility = Visibility.Collapsed; return; }
        var keyword = InputBox.Text.Trim();
        if (keyword.Length < 1)
        {
            CancelPendingSearch();
            MatchList.Visibility = Visibility.Collapsed;
            return;
        }

        lock (_searchLock)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var cts = _searchCts;
            _ = DebouncedSearchAsync(keyword, cts.Token);
        }
    }

    private async Task DebouncedSearchAsync(string keyword, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token).ConfigureAwait(true); // 防抖
            var matches = await _searchSource!(keyword).ConfigureAwait(true);
            if (token.IsCancellationRequested || keyword != InputBox.Text.Trim()) return;

            if (matches.Count == 0)
            {
                MatchList.Visibility = Visibility.Collapsed;
                return;
            }

            MatchList.ItemsSource = matches;
            MatchList.SelectedIndex = -1;
            MatchList.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            // 被新输入取消，忽略
        }
        catch (Exception)
        {
            if (token.IsCancellationRequested) return;
            MatchList.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelPendingSearch()
    {
        lock (_searchLock) _searchCts?.Cancel();
    }

    private void MatchList_Click(object sender, MouseButtonEventArgs e)
    {
        if (MatchList.SelectedItem is StockSearchMatch m) AcceptMatch(m);
    }

    private void AcceptMatch(StockSearchMatch m)
    {
        SelectedMatch = m;
        DialogResult = true;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (MatchList.Visibility == Visibility.Visible && MatchList.SelectedItem is StockSearchMatch m)
            AcceptMatch(m);
        else
            CloseWithResult();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingSearch();
        DialogResult = false;
        Close();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (MatchList.Visibility == Visibility.Visible && MatchList.Items.Count > 0)
        {
            switch (e.Key)
            {
                case Key.Down:
                    e.Handled = true;
                    MoveSel(1);
                    return;
                case Key.Up:
                    e.Handled = true;
                    MoveSel(-1);
                    return;
                case Key.Enter:
                    e.Handled = true;
                    if (MatchList.SelectedIndex >= 0 && MatchList.SelectedItem is StockSearchMatch m)
                        AcceptMatch(m);
                    else
                        CloseWithResult();
                    return;
                case Key.Escape:
                    MatchList.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                    return;
            }
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CloseWithResult();
        }
        else if (e.Key == Key.Escape)
        {
            CancelPendingSearch();
            DialogResult = false;
            Close();
        }
    }

    private void MoveSel(int delta)
    {
        var count = MatchList.Items.Count;
        if (count == 0) return;
        var idx = MatchList.SelectedIndex;
        var next = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, count - 1);
        MatchList.SelectedIndex = next;
        MatchList.ScrollIntoView(MatchList.SelectedItem);
    }

    private void CloseWithResult()
    {
        if (Input.Length == 0) return;
        CancelPendingSearch();
        DialogResult = true;
        Close();
    }
}