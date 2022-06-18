using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Unlimotion.Views;

public class MessageBox : Window
{
    public MessageBox()
    {
        InitializeComponent();
    }

    public Action YesAction { get; set; }
    public Action NoAction { get; set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ButtonYesClick(object sender, RoutedEventArgs e)
    {
        YesAction();
        Close();
    }

    private void ButtonCancelClick(object sender, RoutedEventArgs e)
    {
        if (NoAction != null)
            NoAction();
        Close();
    }
}

public class MessageBoxBuilder
{
    private readonly MessageBox _window = new();

    public MessageBoxBuilder SetBackgroundBrush(IBrush brush)
    {
        _window.Background = brush;
        return this;
    }
    public MessageBoxBuilder SetHeader(string header)
    {
        _window.Find<TextBlock>("Header")!.Text = header;
        return this;
    }
    public MessageBoxBuilder SetMessage(string msg)
    {
        _window.Find<TextBlock>("Message")!.Text = msg;
        return this;
    }
    public MessageBoxBuilder SetYesAction(Action action)
    {
        _window.YesAction = action;
        return this;
    }
    public MessageBoxBuilder SetNoAction(Action action)
    {
        _window.NoAction = action;
        return this;
    }
    public MessageBox Build()
    {
        return _window;
    }
}