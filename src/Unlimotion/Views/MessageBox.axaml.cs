using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Unlimotion.Views;

public class MessageBox : UserControl
{
    public delegate void CloseHandler();

    public MessageBox()
    {
        InitializeComponent();
    }

    public Action YesAction { get; set; }
    public Action NoAction { get; set; }
    public event CloseHandler Notify;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ButtonYesClick(object sender, RoutedEventArgs e)
    {
        YesAction();

        IsVisible = false;
        Notify();
    }

    private void ButtonCancelClick(object sender, RoutedEventArgs e)
    {
        if (NoAction != null)
            NoAction();

        IsVisible = false;
        Notify();
    }
}

public class MessageBoxBuilder
{
    public MessageBoxBuilder(MessageBox userControl)
    {
        UserControl = userControl;
    }

    private MessageBox UserControl { get; }

    public MessageBoxBuilder SetBackgroundBrush(IBrush brush)
    {
        UserControl.Background = brush;
        return this;
    }

    public MessageBoxBuilder SetHeader(string header)
    {
        UserControl.Find<TextBlock>("Header")!.Text = header;
        return this;
    }

    public MessageBoxBuilder SetMessage(string msg)
    {
        UserControl.Find<TextBlock>("Message")!.Text = msg;
        return this;
    }

    public MessageBoxBuilder SetYesAction(Action action)
    {
        UserControl.YesAction = action;
        return this;
    }

    public MessageBoxBuilder SetNoAction(Action action)
    {
        UserControl.NoAction = action;
        return this;
    }

    public void Build()
    {
        UserControl.IsVisible = true;
    }
}