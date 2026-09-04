using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Feed;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

public sealed class MarkdownLinkInvokedEventArgs(
    int blockIndex,
    MarkdownInlineTokenKind kind,
    string target) : EventArgs
{
    public int BlockIndex { get; } = blockIndex;

    public MarkdownInlineTokenKind Kind { get; } = kind;

    public string Target { get; } = target;
}

public sealed class BrokenTaskReferenceActionEventArgs(
    int blockIndex,
    string taskId,
    FeedBrokenTaskReferenceAction action) : EventArgs
{
    public int BlockIndex { get; } = blockIndex;

    public string TaskId { get; } = taskId;

    public FeedBrokenTaskReferenceAction Action { get; } = action;
}

public sealed class MarkdownBlockPreviewControl : ContentControl
{
    private MarkdownLiveBlockViewModel? observedBlock;

    public static readonly StyledProperty<MarkdownLiveBlockViewModel?> BlockProperty =
        AvaloniaProperty.Register<MarkdownBlockPreviewControl, MarkdownLiveBlockViewModel?>(nameof(Block));

    public MarkdownLiveBlockViewModel? Block
    {
        get => GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    public event EventHandler<MarkdownLinkInvokedEventArgs>? LinkInvoked;

    public event EventHandler<BrokenTaskReferenceActionEventArgs>? BrokenTaskReferenceActionInvoked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BlockProperty)
        {
            ObserveBlock(Block);
            RenderBlock();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObserveBlock(Block);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ObserveBlock(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveBlock(MarkdownLiveBlockViewModel? block)
    {
        if (ReferenceEquals(observedBlock, block))
        {
            return;
        }

        if (observedBlock is not null)
        {
            observedBlock.PropertyChanged -= OnObservedBlockPropertyChanged;
        }

        observedBlock = block;
        if (observedBlock is not null)
        {
            observedBlock.PropertyChanged += OnObservedBlockPropertyChanged;
        }
    }

    private void OnObservedBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MarkdownLiveBlockViewModel.TaskReferencesVersion), StringComparison.Ordinal))
        {
            RenderBlock();
        }
    }

    private void RenderBlock()
    {
        var block = Block;
        if (block is null)
        {
            Content = null;
            return;
        }

        Margin = new Thickness(block.ListDepth * 16, 0, 0, 0);
        Content = block.RenderKind switch
        {
            MarkdownLiveBlockRenderKind.Blank => new Border { MinHeight = 6 },
            MarkdownLiveBlockRenderKind.Heading => CreateHeading(block),
            MarkdownLiveBlockRenderKind.Paragraph => CreateInlinePanel(block),
            MarkdownLiveBlockRenderKind.ListItem => CreateList(block, isTask: false),
            MarkdownLiveBlockRenderKind.TaskListItem => CreateList(block, isTask: true),
            MarkdownLiveBlockRenderKind.BlockQuote => CreateBlockQuote(block),
            MarkdownLiveBlockRenderKind.FencedCode => CreateCode(block),
            MarkdownLiveBlockRenderKind.HorizontalRule => new Separator { Margin = new Thickness(0, 8) },
            _ => CreateRawFallback(block)
        };
    }

    private Control CreateHeading(MarkdownLiveBlockViewModel block)
    {
        var panel = CreateInlinePanel(block);
        panel.Margin = new Thickness(0, block.HeadingLevel <= 2 ? 8 : 4, 0, 2);
        panel.VerticalAlignment = VerticalAlignment.Center;
        foreach (var textBlock in panel.Children.OfType<TextBlock>())
        {
            textBlock.FontWeight = block.HeadingLevel <= 2 ? FontWeight.SemiBold : FontWeight.Medium;
            textBlock.FontSize = block.HeadingLevel switch
            {
                <= 1 => 22,
                2 => 18,
                3 => 16,
                _ => 14
            };
        }

        return panel;
    }

    private Control CreateList(MarkdownLiveBlockViewModel block, bool isTask)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8
        };

        Control marker;
        if (isTask)
        {
            var checkBox = new CheckBox
            {
                IsChecked = block.IsTaskCompleted,
                IsHitTestVisible = true,
                Focusable = true,
                VerticalAlignment = VerticalAlignment.Top
            };
            checkBox.Click += async (_, args) =>
            {
                args.Handled = true;
                await block.Owner.ToggleTaskCompletionAsync(block);
            };
            marker = checkBox;
            AutomationProperties.SetName(
                marker,
                L10n.Get(block.IsTaskCompleted ? "MarkdownTaskCompleted" : "MarkdownTaskIncomplete"));
            AutomationProperties.SetAutomationId(marker, block.TaskCheckboxAutomationId);
        }
        else
        {
            marker = new TextBlock
            {
                Text = block.ListMarker,
                MinWidth = 16,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
        }

        var content = CreateInlinePanel(block);
        Grid.SetColumn(content, 1);
        grid.Children.Add(marker);
        grid.Children.Add(content);
        return grid;
    }

    private Control CreateBlockQuote(MarkdownLiveBlockViewModel block)
    {
        return new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Opacity = 0.9,
            Child = CreateInlinePanel(block)
        };
    }

    private static Control CreateCode(MarkdownLiveBlockViewModel block)
    {
        return new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Child = new SelectableTextBlock
            {
                Text = block.PreviewText,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static Control CreateRawFallback(MarkdownLiveBlockViewModel block)
    {
        var text = new SelectableTextBlock
        {
            Text = block.PreviewText,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetAutomationId(text, block.RawFallbackAutomationId);
        AutomationProperties.SetName(text, L10n.Get("MarkdownUnsupportedRawName"));

        return new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Opacity = 0.85,
            Child = text
        };
    }

    private WrapPanel CreateInlinePanel(MarkdownLiveBlockViewModel block)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (block.InlineTokens.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = block.PreviewText, TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        var linkIndex = 0;
        foreach (var token in block.InlineTokens)
        {
            panel.Children.Add(CreateInlineToken(block, token, linkIndex));
            if (token.Kind is MarkdownInlineTokenKind.Link or MarkdownInlineTokenKind.WikiLink)
            {
                linkIndex++;
            }
        }

        return panel;
    }

    private Control CreateInlineToken(
        MarkdownLiveBlockViewModel block,
        MarkdownInlineToken token,
        int linkIndex)
    {
        if (token.Kind is MarkdownInlineTokenKind.Link or MarkdownInlineTokenKind.WikiLink)
        {
            var target = token.Target;
            if (!token.IsSafeLink || string.IsNullOrWhiteSpace(target))
            {
                var blocked = new TextBlock
                {
                    Text = token.Text,
                    TextDecorations = CreateUnderlineDecoration(),
                    Opacity = 0.62,
                    TextWrapping = TextWrapping.Wrap
                };
                AutomationProperties.SetAutomationId(
                    blocked,
                    block.BlockedLinkAutomationId(linkIndex));
                AutomationProperties.SetName(blocked, L10n.Format("MarkdownBlockedLinkName", token.Text));
                AutomationProperties.SetHelpText(blocked, L10n.Get("MarkdownBlockedLinkHelp"));
                ToolTip.SetTip(blocked, L10n.Get("MarkdownBlockedLinkTooltip"));
                return blocked;
            }

            var taskReference = token.Kind == MarkdownInlineTokenKind.Link
                ? block.ResolveTaskReference(target)
                : null;
            if (taskReference is not null)
            {
                return CreateTaskReference(block, token, target, taskReference);
            }

            var label = new TextBlock
            {
                Text = token.Text,
                TextDecorations = CreateUnderlineDecoration(),
                TextWrapping = TextWrapping.Wrap
            };
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetAutomationId(button, block.LinkAutomationId(linkIndex));
            AutomationProperties.SetName(button, L10n.Format("MarkdownLinkName", token.Text));
            AutomationProperties.SetHelpText(button, target);
            button.Click += (_, _) => LinkInvoked?.Invoke(
                this,
                new MarkdownLinkInvokedEventArgs(block.Index, token.Kind, target));
            return button;
        }

        return new TextBlock
        {
            Text = token.Text,
            FontStyle = token.Kind == MarkdownInlineTokenKind.Emphasis ? FontStyle.Italic : FontStyle.Normal,
            FontWeight = token.Kind == MarkdownInlineTokenKind.Strong ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private Control CreateTaskReference(
        MarkdownLiveBlockViewModel block,
        MarkdownInlineToken token,
        string target,
        FeedTaskReferenceViewModel reference)
    {
        if (reference.IsBroken)
        {
            return CreateBrokenTaskReference(block, reference);
        }

        var status = new global::Unlimotion.TaskStatusPicker
        {
            Task = reference.Task,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(status, reference.StatusAutomationId);

        var label = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = CreateUnderlineDecoration(),
            VerticalAlignment = VerticalAlignment.Center
        };
        label.Bind(
            TextBlock.TextProperty,
            new Binding(nameof(FeedTaskReferenceViewModel.DisplayTitle))
            {
                Source = reference
            });

        var title = new Button
        {
            Content = label,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(title, reference.TitleAutomationId);
        AutomationProperties.SetName(title, reference.DisplayTitle);
        AutomationProperties.SetHelpText(title, target);
        title.Click += (_, _) => LinkInvoked?.Invoke(
            this,
            new MarkdownLinkInvokedEventArgs(block.Index, token.Kind, target));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(status);
        grid.Children.Add(title);
        return grid;
    }

    private Control CreateBrokenTaskReference(
        MarkdownLiveBlockViewModel block,
        FeedTaskReferenceViewModel reference)
    {
        var warning = new TextBlock
        {
            Text = "⚠",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.OrangeRed
        };
        AutomationProperties.SetAutomationId(warning, $"FeedTask-{reference.TaskId}-BrokenIcon");
        AutomationProperties.SetName(warning, L10n.Get("FeedBrokenTaskReference"));

        var title = new TextBlock
        {
            Text = reference.DisplayTitle,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        actions.Children.Add(CreateActionButton(
            "FeedBrokenTaskFind",
            "Find",
            FeedBrokenTaskReferenceAction.Find));
        actions.Children.Add(CreateActionButton(
            "FeedBrokenTaskUnlink",
            "Unlink",
            FeedBrokenTaskReferenceAction.Unlink));
        actions.Children.Add(CreateActionButton(
            "FeedBrokenTaskRestoreRevision",
            "RestoreRevision",
            FeedBrokenTaskReferenceAction.RestoreRevision));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(warning);
        grid.Children.Add(title);
        grid.Children.Add(actions);
        AutomationProperties.SetAutomationId(grid, $"FeedTask-{reference.TaskId}-BrokenReference");
        AutomationProperties.SetName(grid, L10n.Format("FeedBrokenTaskReferenceName", reference.DisplayTitle));
        return grid;

        Button CreateActionButton(
            string resourceKey,
            string automationSuffix,
            FeedBrokenTaskReferenceAction action)
        {
            var button = new Button
            {
                Content = L10n.Get(resourceKey),
                Padding = new Thickness(8, 3),
                MinHeight = 0
            };
            AutomationProperties.SetAutomationId(
                button,
                $"FeedTask-{reference.TaskId}-Broken{automationSuffix}Button");
            AutomationProperties.SetName(button, L10n.Get(resourceKey));
            button.Click += (_, _) => BrokenTaskReferenceActionInvoked?.Invoke(
                this,
                new BrokenTaskReferenceActionEventArgs(block.Index, reference.TaskId, action));
            return button;
        }
    }

    private static TextDecorationCollection CreateUnderlineDecoration() =>
        new()
        {
            new TextDecoration { Location = TextDecorationLocation.Underline }
        };
}
