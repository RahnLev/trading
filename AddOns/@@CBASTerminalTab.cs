using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Custom.CBASTerminal;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class CBASTerminalTab : NTTabPage
    {
		
		
        private ComboBox instanceCombo;
        private TextBox logText;
        private TextBox inputText;
        private Button sendBtn;
        private Button clearBtn;
        private CheckBox autoScrollChk;

        private readonly ConcurrentQueue<CBASTerminalLogEntry> queue = new ConcurrentQueue<CBASTerminalLogEntry>();
        private readonly DispatcherTimer pumpTimer;

        private const int MaxLines = 5000;

        public CBASTerminalTab()
        {


            // Layout grid
            var grid = new Grid { Margin = new Thickness(6) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // top controls
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // log
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // input

            // Top controls
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            top.Children.Add(new TextBlock { Text = "Instance:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            instanceCombo = new ComboBox { Width = 280, Margin = new Thickness(0, 0, 12, 0), IsEditable = false };
            var refreshBtn = new Button { Content = "Refresh", Margin = new Thickness(0, 0, 12, 0) };
            clearBtn = new Button { Content = "Clear", Margin = new Thickness(0, 0, 12, 0) };
            autoScrollChk = new CheckBox { Content = "Auto-scroll", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };

            refreshBtn.Click += (s, e) => RefreshInstances();
            clearBtn.Click += (s, e) => logText.Clear();

            top.Children.Add(instanceCombo);
            top.Children.Add(refreshBtn);
            top.Children.Add(clearBtn);
            top.Children.Add(autoScrollChk);

            // Log area
            logText = new TextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas")
            };

            // Input area
            var inputPanel = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            inputText = new TextBox { Margin = new Thickness(0, 0, 6, 0) };
            sendBtn = new Button { Content = "Send", Width = 100 };

            inputText.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    SendCommand();
                }
            };
            sendBtn.Click += (s, e) => SendCommand();

            inputPanel.Children.Add(inputText);
            inputPanel.Children.Add(sendBtn);
            Grid.SetColumn(inputText, 0);
            Grid.SetColumn(sendBtn, 1);

            // Compose layout
            grid.Children.Add(top);
            grid.Children.Add(logText);
            grid.Children.Add(inputPanel);
            Grid.SetRow(top, 0);
            Grid.SetRow(logText, 1);
            Grid.SetRow(inputPanel, 2);

            Content = grid;

            // Subscribe to bus
            CBASTerminalBus.OnLog += HandleLog;
            CBASTerminalRegistry.RegistryChanged += RefreshInstances;

            // Periodic UI pump
            pumpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            pumpTimer.Tick += PumpTimer_Tick;
            pumpTimer.Start();

            // Cleanup when tab is removed/closed
            Unloaded += OnUnloaded;

            RefreshInstances();
        }

        // Your NTTabPage expects a string return type. Returning null uses default parts.
        protected override string GetHeaderPart(string name)
        {
            return null;
        }

        // Required persistence hooks
        protected override void Save(XElement element)
        {
            try
            {
                var selectedId = instanceCombo?.SelectedItem as string ?? string.Empty;
                element.SetAttributeValue("SelectedInstanceId", selectedId);
                element.SetAttributeValue("AutoScroll", autoScrollChk?.IsChecked == true ? "true" : "false");
            }
            catch
            {
                // optional logging
            }
        }

        protected override void Restore(XElement element)
        {
            try
            {
                var sel = (string)element.Attribute("SelectedInstanceId") ?? string.Empty;
                var auto = (string)element.Attribute("AutoScroll");
                if (bool.TryParse(auto, out var autoVal))
                    autoScrollChk.IsChecked = autoVal;

                RefreshInstances();
                if (!string.IsNullOrEmpty(sel))
                {
                    var ids = CBASTerminalRegistry.GetActiveIds().ToList();
                    if (ids.Contains(sel))
                        instanceCombo.SelectedItem = sel;
                }
            }
            catch
            {
                // optional logging
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= OnUnloaded;

            try
            {
                try { pumpTimer.Stop(); } catch { }
                pumpTimer.Tick -= PumpTimer_Tick;

                CBASTerminalBus.OnLog -= HandleLog;
                CBASTerminalRegistry.RegistryChanged -= RefreshInstances;
            }
            catch
            {
                // optional logging
            }
        }

        private void PumpTimer_Tick(object sender, EventArgs e) => FlushQueueToUI();

        private void HandleLog(CBASTerminalLogEntry entry)
        {
            queue.Enqueue(entry);
        }

        private void FlushQueueToUI()
        {
            if (queue.IsEmpty) return;

            var sb = new StringBuilder();
            while (queue.TryDequeue(out var e))
                sb.AppendLine($"[{e.Timestamp:HH:mm:ss.fff}] [{e.Level}] [{e.InstanceId}] [{e.Instrument}] Bar={e.Bar} :: {e.Message}");

            if (sb.Length == 0) return;

            logText.AppendText(sb.ToString());

            // Trim lines if too long
            var lines = logText.LineCount;

if (lines > MaxLines)
{
    int removeLines = lines - MaxLines;

    // Start of text
    int startIndex = 0;

    // First char index of the first line we want to keep
    int keepStartIndex = logText.GetCharacterIndexFromLineIndex(removeLines);

    // Length to remove is up to keepStartIndex
    int lengthToRemove = keepStartIndex - startIndex;
    if (lengthToRemove > 0)
    {
        logText.Select(startIndex, lengthToRemove);
        logText.SelectedText = string.Empty;
    }

    // Put caret at end so ScrollToEnd works consistently
    logText.CaretIndex = logText.Text.Length;
}

            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();
        }

        private void RefreshInstances()
        {
            var sel = instanceCombo.SelectedItem as string;
            var ids = CBASTerminalRegistry.GetActiveIds().OrderBy(x => x).ToList();

            instanceCombo.ItemsSource = ids;
            if (!string.IsNullOrEmpty(sel) && ids.Contains(sel))
                instanceCombo.SelectedItem = sel;
            else if (ids.Count > 0)
                instanceCombo.SelectedIndex = 0;
        }

        private void SendCommand()
        {
            var id = instanceCombo.SelectedItem as string;
            var cmd = inputText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cmd))
                return;

            CBASTerminalBus.SendCommand(id, cmd);

            // Echo locally
            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();

            inputText.Clear();
            inputText.Focus();
        }
    }
}
