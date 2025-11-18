using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Custom.CBASTerminal;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class CBASTerminalWindow : NTWindow
    {
        #region privates
        private TextBox trailAText;
        private TextBox trailBText;
        private TextBox stopTicksText;
        private TextBox targetATicksText;

        // Optional: bounds
        private const int TrailTicksMin = 0;
        private const int TrailTicksMax = 1000;
        private const int StopTicksMin = 1;
        private const int StopTicksMax = 20000;
        private const int TargetATicksMin = 1;
        private const int TargetATicksMax = 20000;

        // Right sidebar controls
        private TextBlock tpmText;
        private TextBox atrailText;
        private TextBlock barNumberText;
        private TextBlock signalText;
        private TextBlock barCloseText;
        private TextBlock metricsHeaderText;  // Fixed header text at top
        private RichTextBox metricsText;  // Scrollable values only
        private DateTime lastTpmUpdate = DateTime.MinValue;
        private DateTime lastMetricsUpdate = DateTime.MinValue;
        private bool metricsHeadersReceived = false;
        private string[] metricsColumnHeaders = null;
        private readonly DispatcherTimer tpmPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        private readonly DispatcherTimer trailPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DateTime lastTsUpdate = DateTime.MinValue;

        private bool paused;
        private Button pauseBtn;

        private ComboBox instanceCombo;
        private TextBox logText;
        private TextBox inputText;
        private Button sendBtn;
        private Button clearBtn;
        private CheckBox autoScrollChk;

        // NEW: test controls
        private Button testLongBtn;
        private Button testShortBtn;
        private Button reverseBtn;
        private TextBox qtyText;
        private Button closeAllBtn;

        private readonly ConcurrentQueue<CBASTerminalLogEntry> queue = new ConcurrentQueue<CBASTerminalLogEntry>();
        private readonly DispatcherTimer pumpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private const int MaxLines = 5000;
        private const int MaxQueueSize = 1000; // Drop old messages if queue grows beyond this
        private int droppedMessageCount = 0;
        #endregion

        public CBASTerminalWindow()
        {
            try { Caption = "CBAS Terminal"; } catch { }
            Width = 1000;
            Height = 500;

            // Main layout
            var grid = new Grid { Margin = new Thickness(6) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                         // top bar
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });    // log
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                         // input row

            // TOP BAR
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // left
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // middle
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // spacer
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // right

            // Signal state display
            var signalGroup = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 12, 0, 12) };
            
            // Signal header
            signalGroup.Children.Add(new TextBlock { Text = "Current Signal", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center });
            
            signalText = new TextBlock 
            { 
                Text = "--", 
                FontSize = 18, 
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray
            };
            signalGroup.Children.Add(signalText);
            
            // Bar number below signal
            barNumberText = new TextBlock 
            { 
                Text = "Bar: --", 
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.DarkCyan
            };
            signalGroup.Children.Add(barNumberText);
            
            // Bar close countdown
            signalGroup.Children.Add(new TextBlock { Text = "Bar Close In", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 6), HorizontalAlignment = HorizontalAlignment.Center });
            barCloseText = new TextBlock 
            { 
                Text = "--s", 
                FontSize = 16, 
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.DarkOrange
            };
            signalGroup.Children.Add(barCloseText);
            
            // Ticks/min
            signalGroup.Children.Add(new TextBlock { Text = "Ticks/min", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 6), HorizontalAlignment = HorizontalAlignment.Center });
            tpmText = new TextBlock 
            { 
                Text = "--", 
                FontSize = 16, 
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.DodgerBlue
            };
            signalGroup.Children.Add(tpmText);

            // Trailing ticks controls
            var trailGroup = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 12, 0, 0) };
            trailGroup.Children.Add(new TextBlock { Text = "Trailing ticks", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });

            // Row A
            var rowA = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            rowA.Children.Add(new TextBlock { Text = "A:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 65, Margin = new Thickness(0, 0, 6, 0) });

            var minusA = new Button
            {
                Content = "−",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var plusA = new Button
            {
                Content = "+",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var minusB = new Button
            {
                Content = "−",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var plusB = new Button
            {
                Content = "+",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            trailAText = new TextBox { Width = 60, Text = "--", Margin = new Thickness(0, 0, 4, 0), IsReadOnly = false, HorizontalContentAlignment = HorizontalAlignment.Center };
            trailAText.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; SetTrailValueFromTextBox("ATrailTicks", trailAText); } };

            minusA.Click += (s, e) => AdjustTrailAndSend("ATrailTicks", trailAText, -2);
            plusA.Click += (s, e) => AdjustTrailAndSend("ATrailTicks", trailAText, +2);

            rowA.Children.Add(minusA);
            rowA.Children.Add(trailAText);
            rowA.Children.Add(plusA);

            // Row B
            var rowB = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            rowB.Children.Add(new TextBlock { Text = "B:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 65, Margin = new Thickness(0, 0, 6, 0) });

            trailBText = new TextBox { Width = 60, Text = "--", Margin = new Thickness(0, 0, 4, 0), IsReadOnly = false, HorizontalContentAlignment = HorizontalAlignment.Center };
            trailBText.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; SetTrailValueFromTextBox("RunnerTrailTicks", trailBText); } };

            minusB.Click += (s, e) => AdjustTrailAndSend("RunnerTrailTicks", trailBText, -2);
            plusB.Click += (s, e) => AdjustTrailAndSend("RunnerTrailTicks", trailBText, +2);

            rowB.Children.Add(minusB);
            rowB.Children.Add(trailBText);
            rowB.Children.Add(plusB);

            trailGroup.Children.Add(rowA);
            trailGroup.Children.Add(rowB);

            // Right sidebar (column 1)
            var rightSidebar = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(12, 0, 0, 0),
                Width = 200
            };
            rightSidebar.Children.Add(signalGroup);
            rightSidebar.Children.Add(trailGroup);

            // A-trail row (moved to right panel, same look/size as trail A/B, with +/- buttons)
            var rowATrail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 6) };
            rowATrail.Children.Add(new TextBlock { Text = "A-trail:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 65, Margin = new Thickness(0, 0, 6, 0) });

            var minusATrail = new Button
            {
                Content = "−",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            atrailText = new TextBox
            {
                IsReadOnly = false,                  // Now editable for keyboard input
                Width = 60,                          // same width as trail A/B textboxes
                Text = "-- s",
                Margin = new Thickness(0, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            atrailText.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; SetATrailValueFromTextBox(); } };

            var plusATrail = new Button
            {
                Content = "+",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            // Handlers: adjust display locally for now; you can later replace with your own function/command
            minusATrail.Click += (s, e) => AdjustATrailDisplay(-1);
            plusATrail.Click += (s, e) => AdjustATrailDisplay(+1);

            rowATrail.Children.Add(minusATrail);
            rowATrail.Children.Add(atrailText);
            rowATrail.Children.Add(plusATrail);
            rightSidebar.Children.Add(rowATrail);

            // Stop Ticks row (similar to trail ticks A/B, with +/- buttons)
            var rowStopTicks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            rowStopTicks.Children.Add(new TextBlock { Text = "Stop:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 65, Margin = new Thickness(0, 0, 6, 0) });

            var minusStopTicks = new Button
            {
                Content = "−",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            stopTicksText = new TextBox
            {
                IsReadOnly = false,                  // Now editable for keyboard input
                Width = 60,
                Text = "--",
                Margin = new Thickness(0, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            stopTicksText.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; SetTrailValueFromTextBox("StopTicks", stopTicksText); } };

            var plusStopTicks = new Button
            {
                Content = "+",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            minusStopTicks.Click += (s, e) => AdjustTrailAndSend("StopTicks", stopTicksText, -5);
            plusStopTicks.Click += (s, e) => AdjustTrailAndSend("StopTicks", stopTicksText, +5);

            rowStopTicks.Children.Add(minusStopTicks);
            rowStopTicks.Children.Add(stopTicksText);
            rowStopTicks.Children.Add(plusStopTicks);
            rightSidebar.Children.Add(rowStopTicks);

            // Target A Ticks row (similar to Stop Ticks, with +/- buttons)
            var rowTargetATicks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            rowTargetATicks.Children.Add(new TextBlock { Text = "Target A:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 65, Margin = new Thickness(0, 0, 6, 0) });

            var minusTargetATicks = new Button
            {
                Content = "−",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            targetATicksText = new TextBox
            {
                IsReadOnly = false,                  // Editable for keyboard input
                Width = 60,
                Text = "--",
                Margin = new Thickness(0, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            targetATicksText.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; SetTrailValueFromTextBox("TargetATicks", targetATicksText); } };

            var plusTargetATicks = new Button
            {
                Content = "+",
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            minusTargetATicks.Click += (s, e) => AdjustTrailAndSend("TargetATicks", targetATicksText, -5);
            plusTargetATicks.Click += (s, e) => AdjustTrailAndSend("TargetATicks", targetATicksText, +5);

            rowTargetATicks.Children.Add(minusTargetATicks);
            rowTargetATicks.Children.Add(targetATicksText);
            rowTargetATicks.Children.Add(plusTargetATicks);
            rightSidebar.Children.Add(rowTargetATicks);

            // Left: Instance + combo + Refresh
            var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftPanel.Children.Add(new TextBlock { Text = "Instance:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            instanceCombo = new ComboBox { Width = 230, Margin = new Thickness(0, 0, 6, 0), IsEditable = false };

            instanceCombo.SelectionChanged += (s, e) =>
            {
                lastTsUpdate = DateTime.MinValue;
                atrailText.Text = "-- s";

                var id = instanceCombo.SelectedItem as string;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    CBASTerminalBus.SendCommand(id, "trailstatus");
                    CBASTerminalBus.SendCommand(id, "trailticks"); // request current
                }
            };

            var refreshBtn = new Button { Content = "Refresh", Margin = new Thickness(0, 0, 6, 0) };
            refreshBtn.Click += (s, e) => RefreshInstances();

            leftPanel.Children.Add(instanceCombo);
            leftPanel.Children.Add(refreshBtn);

            // Middle: Qty + TEST LONG/SHORT
            var middlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
            var qtyLbl = new TextBlock { Text = "Qty:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            qtyText = new TextBox { Text = "5", Width = 48, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Qty per leg" };

            // Right: Clear + Pause + Auto-scroll
            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

            testLongBtn = new Button { Content = "LONG", Width = 50, Margin = new Thickness(0, 0, 6, 0) };
            testShortBtn = new Button { Content = "SHORT", Width = 50, Margin = new Thickness(0, 0, 6, 0) };
            reverseBtn = new Button { Content = "REVERSE", Width = 70, Margin = new Thickness(0, 0, 6, 0) };

            testLongBtn.Click += (s, e) => SendTestCommand(true);
            testShortBtn.Click += (s, e) => SendTestCommand(false);
            reverseBtn.Click += (s, e) => SendReverseCommand();

            middlePanel.Children.Add(qtyLbl);
            middlePanel.Children.Add(qtyText);
            middlePanel.Children.Add(testLongBtn);
            middlePanel.Children.Add(testShortBtn);
            middlePanel.Children.Add(reverseBtn);

            tpmPollTimer.Tick += (s, e) =>
            {
                if ((DateTime.UtcNow - lastTpmUpdate).TotalSeconds > 5)
                    tpmText.Text = "--";
            };
            tpmPollTimer.Start();

            closeAllBtn = new Button { Content = "Close Trades", Margin = new Thickness(0, 0, 6, 0) };
            closeAllBtn.Click += (s, e) =>
            {
                var id = instanceCombo.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(id)) return;

                var cmd = "flat"; // or "closeall"
                CBASTerminalBus.SendCommand(id, cmd);

                logText.AppendText($"> [{id}] {cmd}\n");
                if (autoScrollChk.IsChecked == true)
                    logText.ScrollToEnd();
            };

            rightPanel.Children.Insert(0, closeAllBtn); // place before Clear/Pause

            clearBtn = new Button { Content = "Clear", Width = 60, Margin = new Thickness(0, 0, 6, 0) };
            clearBtn.Click += (s, e) => logText?.Clear();

            pauseBtn = new Button { Content = "Pause", Width = 60, Margin = new Thickness(0, 0, 6, 0) };
            pauseBtn.Click += PauseBtn_Click;

            autoScrollChk = new CheckBox { Content = "Auto-scroll", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };

            rightPanel.Children.Add(clearBtn);
            rightPanel.Children.Add(pauseBtn);
            rightPanel.Children.Add(autoScrollChk);

            // Add top groups
            topGrid.Children.Add(leftPanel);
            Grid.SetColumn(leftPanel, 0);
            topGrid.Children.Add(middlePanel);
            Grid.SetColumn(middlePanel, 1);
            topGrid.Children.Add(rightPanel);
            Grid.SetColumn(rightPanel, 3);

            grid.Children.Add(topGrid);
            Grid.SetRow(topGrid, 0);

            // LOG TEXT
            logText = new TextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas")
            };
            grid.Children.Add(logText);
            Grid.SetRow(logText, 1);

            // METRICS DISPLAY (below log, above input) - table format with fixed headers and scrollable values
            var metricsPanel = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 6, 0, 0),
                Background = System.Windows.Media.Brushes.Black,
                Height = 140  // Fixed height for headers + 6 value rows
            };
            
            // Grid with 2 rows: fixed headers at top, scrollable values below
            var metricsGrid = new Grid
            {
                Background = System.Windows.Media.Brushes.Black
            };
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Header row (fixed)
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // Values row (scrollable)

            // Fixed header TextBlock at top (will be populated when headers arrive)
            metricsHeaderText = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.Yellow,
                Background = System.Windows.Media.Brushes.Black,
                Padding = new Thickness(2),
                Text = "Waiting for metrics headers..."
            };
            Grid.SetRow(metricsHeaderText, 0);
            metricsGrid.Children.Add(metricsHeaderText);

            // Scrollable values RichTextBox below (only values, no headers)
            var metricsRichTextBox = new RichTextBox
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 10,
                Background = System.Windows.Media.Brushes.Black,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Document = new FlowDocument()
                {
                    PagePadding = new Thickness(2),
                    Background = System.Windows.Media.Brushes.Black
                }
            };
            Grid.SetRow(metricsRichTextBox, 1);
            metricsGrid.Children.Add(metricsRichTextBox);
            
            // Store reference as RichTextBox
            metricsText = metricsRichTextBox;
            
            metricsPanel.Child = metricsGrid;
            grid.Children.Add(metricsPanel);
            Grid.SetRow(metricsPanel, 2);

            // Shift INPUT ROW to row 3
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // metrics row

            // INPUT ROW
            var inputPanel = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            trailPollTimer.Tick += TrailPollTimer_Tick;
            trailPollTimer.Start();

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

            grid.Children.Add(inputPanel);
            Grid.SetRow(inputPanel, 3);  // moved from row 2 to row 3 to make room for metrics

			// Build a 2-column parent grid (left: main content, right: sidebar)
			var parent = new Grid { Margin = new Thickness(6) };
			parent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			parent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			
			// Move existing main grid into column 0
			grid.Margin = new Thickness(0); // parent holds the outer margin now
			parent.Children.Add(grid);
			Grid.SetColumn(grid, 0);
			
			// Right sidebar
			parent.Children.Add(rightSidebar);
			Grid.SetColumn(rightSidebar, 1);
			
			// Assign parent as window content
			Content = parent;


            //--------------------------------------------------------------------------------------------------------------------
            // Bus / events
            CBASTerminalBus.OnLog += HandleLog;
            CBASTerminalRegistry.RegistryChanged += RefreshInstances;

            pumpTimer.Tick += PumpTimer_Tick;
            pumpTimer.Start();

            Unloaded += OnUnloaded;

            RefreshInstances();
        }

        private void TrailPollTimer_Tick(object sender, EventArgs e)
        {
            var id = instanceCombo.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(id))
                CBASTerminalBus.SendCommand(id, "trailstatus");

            // If no [TS] response for 3 seconds, show "-- s"
            if ((DateTime.UtcNow - lastTsUpdate).TotalSeconds > 3)
                atrailText.Text = "-- s";
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= OnUnloaded;
            try
            {
                pumpTimer.Stop();
                pumpTimer.Tick -= PumpTimer_Tick;

                trailPollTimer.Stop();
                trailPollTimer.Tick -= TrailPollTimer_Tick;

                CBASTerminalBus.OnLog -= HandleLog;

                CBASTerminalRegistry.RegistryChanged -= RefreshInstances;
                tpmPollTimer.Stop();
                // Note: the tpmPollTimer lambda cannot be detached directly; safe to let GC handle
            }
            catch { }
        }

        private void PumpTimer_Tick(object sender, EventArgs e) => FlushQueueToUI();

        private void HandleLog(CBASTerminalLogEntry entry)
        {
            // Backpressure: if queue is too large, drop oldest messages
            if (queue.Count >= MaxQueueSize)
            {
                // Drain excess messages
                int excess = queue.Count - (MaxQueueSize / 2); // Keep half when trimming
                for (int i = 0; i < excess && queue.TryDequeue(out _); i++)
                    droppedMessageCount++;
            }
            queue.Enqueue(entry);
        }

        private void FlushQueueToUI()
        {
            if (queue.IsEmpty) return;

            // Batch UI updates to reduce freezing
            var sb = new StringBuilder();
            int processedCount = 0;
            const int maxBatchSize = 50; // Reduced from 100 to prevent UI freeze with complex parsing
            
            // Silently reset dropped counter (no message shown)
            droppedMessageCount = 0;
            
            // Cache UI updates to apply once at the end
            string newBarNumber = null;
            string newSignal = null;
            System.Windows.Media.Brush newSignalColor = null;
            string newBarClose = null;
            string newATrail = null;
            string newMetrics = null;
            System.Windows.Media.Brush newMetricsColor = null;
            string newTpm = null;
            string newTrailA = null;
            string newTrailB = null;

            while (queue.TryDequeue(out var e) && processedCount < maxBatchSize)
            {
                processedCount++;
                
                // Cache bar number updates
                if (e.Bar >= 0)
                {
                    newBarNumber = $"Bar: {e.Bar}";
                }
                
                // Intercept signal state lines: format "[SIG] BULL" or "[SIG] BEAR" or "[SIG] FLAT"
                if (e.Message != null && e.Message.StartsWith("[SIG]", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var payload = e.Message.Substring(5).Trim().ToUpperInvariant();
                        newSignal = payload;
                        
                        // Color code the signal
                        if (payload == "BULL")
                            newSignalColor = System.Windows.Media.Brushes.Green;
                        else if (payload == "BEAR")
                            newSignalColor = System.Windows.Media.Brushes.Red;
                        else
                            newSignalColor = System.Windows.Media.Brushes.Gray;
                    }
                    catch
                    {
                        newSignal = "--";
                        newSignalColor = System.Windows.Media.Brushes.Gray;
                    }
                    continue; // do not add to log
                }
                // Intercept bar close countdown lines: format "[BC] 45" (seconds remaining)
                else if (e.Message != null && e.Message.StartsWith("[BC]", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var payload = e.Message.Substring(4).Trim();
                        if (int.TryParse(payload, out var seconds) && seconds >= 0)
                        {
                            newBarClose = $"{seconds}s";
                        }
                        else
                        {
                            newBarClose = "--s";
                        }
                    }
                    catch
                    {
                        newBarClose = "--s";
                    }
                    continue; // do not add to log
                }
                // Intercept trail status lines: format we'll emit from the strategy is:
                // [TS] rem=123 armed=false
                else if (e.Message != null && e.Message.StartsWith("[TS]", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var payload = e.Message.Substring(4).Trim(); // after "]"
                        int rem = -1;
                        bool armed = false;

                        foreach (var token in payload.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var kv = token.Split(new[] { '=' }, 2);
                            if (kv.Length != 2) continue;

							var key = kv[0].Trim().ToLowerInvariant();
 							var val = kv[1].Trim();
                            if (key == "rem" || key == "remaining" || key == "seconds")
                            {
                                // accept values like "123", "123s"
                                if (val.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                                    val = val.Substring(0, val.Length - 1);

                                if (!int.TryParse(val, out rem))
                                    rem = -1;
                            }
                            else if (key == "armed" || key == "isarmed")
                            {
                                bool.TryParse(val, out armed);
                            }
                        }

                        newATrail = rem >= 0 ? $"{rem}s{(armed ? " (armed)" : "")}" : "-- s";
                        lastTsUpdate = DateTime.UtcNow;
                    }
                    catch
                    {
                        newATrail = "-- s";
                    }
                    continue; // do not add to log
                }
                // Intercept METRICS HEADERS: format "[METRICS_HEADERS] Attract|Objection|..."
                else if (e.Message != null && e.Message.StartsWith("[METRICS_HEADERS]", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var payload = e.Message.Substring(17).Trim(); // after "[METRICS_HEADERS]"
                        metricsColumnHeaders = payload.Split('|');
                        metricsHeadersReceived = true;
                        
                        // Update fixed header TextBlock at top (will stay visible while values scroll)
                        // Format headers with proper column widths (10 chars per column)
                        var formattedHeaders = string.Join(" ", metricsColumnHeaders.Select(h => h.PadRight(10)));
                        var separator = new string('─', formattedHeaders.Length);
                        
                        metricsHeaderText.Text = formattedHeaders + "\n" + separator;
                    }
                    catch
                    {
                        metricsHeadersReceived = false;
                    }
                    continue; // do not add to log
                }
                // Intercept METRICS VALUES: format "[METRICS_VALUES] 1.23|4.56|..."
                else if (e.Message != null && e.Message.StartsWith("[METRICS_VALUES]", StringComparison.OrdinalIgnoreCase))
                {
                    // Throttle to max 2 updates per second to prevent UI freezing
                    var now = DateTime.UtcNow;
                    if ((now - lastMetricsUpdate).TotalMilliseconds < 500)
                    {
                        continue; // Skip this update
                    }
                    lastMetricsUpdate = now;
                    
                    try
                    {
                        var payload = e.Message.Substring(16).Trim(); // after "[METRICS_VALUES]"
                        var values = payload.Split('|');
                        
                        // Extract realtime_state from last value for coloring
                        System.Windows.Media.Brush valueColor = System.Windows.Media.Brushes.WhiteSmoke;
                        if (values.Length > 0)
                        {
                            var lastValue = values[values.Length - 1];
                            if (lastValue.Contains("Bull"))
                                valueColor = System.Windows.Media.Brushes.Cyan;
                            else if (lastValue.Contains("Bear"))
                                valueColor = System.Windows.Media.Brushes.Red;
                            else if (lastValue.Contains("Flat"))
                                valueColor = System.Windows.Media.Brushes.Silver;
                        }
                        
                        // Format values with proper column widths to align with headers (10 chars per column)
                        var formattedValues = string.Join(" ", values.Select(v => v.PadRight(10)));
                        newMetrics = formattedValues;
                        newMetricsColor = valueColor;
                    }
                    catch
                    {
                        newMetrics = "Error parsing metrics values";
                        newMetricsColor = System.Windows.Media.Brushes.Red;
                    }
                    continue; // do not add to log
                }
                // Intercept TPM (ticks per minute) lines: format "[TPM] 123"
                else if (e.Message != null && e.Message.StartsWith("[TPM]", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var payload = e.Message.Substring(5).Trim(); // after "]"
                        if (int.TryParse(payload, out var tpm) && tpm >= 0)
                        {
                            newTpm = tpm.ToString();
                            lastTpmUpdate = DateTime.UtcNow;
                        }
                        else
                        {
                            newTpm = "--";
                        }
                    }
                    catch
                    {
                        newTpm = "--";
                    }
                    continue; // do not add to log
                }
                else if (e.Message != null && e.Message.StartsWith("[TT]", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var payload = e.Message.Substring(4).Trim();
                        int? a = null, b = null;
                        foreach (var token in payload.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var kv = token.Split(new[] { '=' }, 2);
                            if (kv.Length != 2) continue;
							var k = kv[0].Trim().ToUpperInvariant();
 							var v = kv[1].Trim();
                            if (k == "A" && int.TryParse(v, out var av)) a = av;
                            if (k == "B" && int.TryParse(v, out var bv)) b = bv;
                        }
                        if (a.HasValue) newTrailA = a.Value.ToString();
                        if (b.HasValue) newTrailB = b.Value.ToString();
                    }
                    catch { /* ignore parse errors */ }
                    continue; // don't append this line to the log
                }

                // Normal log append
                sb.AppendLine($"[{e.Timestamp:HH:mm:ss.fff}] [{e.Level}] [{e.Instrument}] Bar={e.Bar} :: {e.Message}");
            }

            // Apply all cached UI updates in one batch
            if (newBarNumber != null) barNumberText.Text = newBarNumber;
            if (newSignal != null)
            {
                signalText.Text = newSignal;
                if (newSignalColor != null) signalText.Foreground = newSignalColor;
            }
            if (newBarClose != null) barCloseText.Text = newBarClose;
            if (newATrail != null) atrailText.Text = newATrail;
            if (newMetrics != null)
            {
                // Add new metrics values row (column format already applied)
                var para = new Paragraph(new Run(newMetrics))
                {
                    Margin = new Thickness(0),
                    Foreground = newMetricsColor ?? System.Windows.Media.Brushes.WhiteSmoke,
                    FontSize = 12  // Match header font size
                };
                
                metricsText.Document.Blocks.Add(para);
                
                // Keep only last 20 value rows (headers are in separate TextBlock now)
                var blockCount = metricsText.Document.Blocks.Count;
                if (blockCount > 20)
                {
                    // Remove oldest value rows from the beginning
                    while (metricsText.Document.Blocks.Count > 20)
                    {
                        var firstBlock = metricsText.Document.Blocks.FirstOrDefault();
                        if (firstBlock != null)
                            metricsText.Document.Blocks.Remove(firstBlock);
                    }
                }
                
                // Auto-scroll to bottom to show latest metrics
                metricsText.ScrollToEnd();
            }
            if (newTpm != null) tpmText.Text = newTpm;
            if (newTrailA != null) trailAText.Text = newTrailA;
            if (newTrailB != null) trailBText.Text = newTrailB;

            // Update log text once with all accumulated messages
            if (sb.Length > 0)
            {
                logText.AppendText(sb.ToString());
                var lines = logText.LineCount;
                
                // OPTIMIZED: Use faster trimming when way over limit
                if (lines > MaxLines + 500) // Only trim when significantly over to reduce frequency
                {
                    try
                    {
                        // Much faster approach: split, take last N, rejoin
                        var allText = logText.Text;
                        var lineArray = allText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lineArray.Length > MaxLines)
                        {
                            var keepLines = lineArray.Skip(lineArray.Length - MaxLines).ToArray();
                            logText.Text = string.Join(Environment.NewLine, keepLines) + Environment.NewLine;
                            logText.CaretIndex = logText.Text.Length;
                        }
                    }
                    catch
                    {
                        // Fallback: just clear if trimming fails (rare edge case)
                        // This prevents a freeze if the text is malformed
                    }
                }
                
                if (autoScrollChk.IsChecked == true)
                    logText.ScrollToEnd();
            }
        }

        private void RefreshInstances()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)RefreshInstances);
                return;
            }

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

            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();

            inputText.Clear();
            inputText.Focus();
        }

        private void SendTestCommand(bool isLong)
        {
            var id = instanceCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                return;

            int qty = 5;
            int.TryParse(qtyText?.Text, out qty);
            qty = Math.Max(1, qty);

            string cmd = isLong ? $"testlong {qty}" : $"testshort {qty}";
            CBASTerminalBus.SendCommand(id, cmd);

            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();
        }

        private void SendReverseCommand()
        {
            var id = instanceCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                return;

            string cmd = "reverse";
            CBASTerminalBus.SendCommand(id, cmd);

            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            var id = instanceCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                return;

            paused = !paused;
            var cmd = paused ? "set Paused true" : "set Paused false";
            CBASTerminalBus.SendCommand(id, cmd);

            pauseBtn.Content = paused ? "Resume" : "Pause";

            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();
        }

        private void AdjustTrailAndSend(string propertyName, TextBox box, int delta)
        {
            var id = instanceCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                return;

            // Parse current value from the box; if not a number, assume 0
            int current = 0;
            int.TryParse((box.Text ?? "").Trim(), out current);

            int newVal = current + delta;
            
            // Use appropriate min/max based on property name
            if (propertyName == "StopTicks")
            {
                newVal = Math.Max(StopTicksMin, Math.Min(StopTicksMax, newVal));
            }
            else if (propertyName == "TargetATicks")
            {
                newVal = Math.Max(TargetATicksMin, Math.Min(TargetATicksMax, newVal));
            }
            else
            {
                newVal = Math.Max(TrailTicksMin, Math.Min(TrailTicksMax, newVal));
            }

            // Send the existing text command your strategy already supports
            string cmd = $"set {propertyName} {newVal}";
            CBASTerminalBus.SendCommand(id, cmd);

            // Log and optionally update the UI immediately
            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();

            // Optional: update instantly for responsiveness; the strategy can still echo back
            box.Text = newVal.ToString();
        }

        // Helper: adjust A-trail display locally to keep UI consistent with +/- controls.
        // Replace body with your own function call if you want to trigger strategy commands.
        private void AdjustATrailDisplay(int deltaSeconds)
        {
            // Extract current numeric seconds from text like "123s", "-- s", "123s (armed)"
            int current = 0;
            var txt = (atrailText.Text ?? "").Trim();

            // Try to parse the first number in the string
            int i = 0;
            while (i < txt.Length && !char.IsDigit(txt[i])) i++;
            int j = i;
            while (j < txt.Length && char.IsDigit(txt[j])) j++;

            if (i < j && int.TryParse(txt.Substring(i, j - i), out var parsed))
                current = parsed;
            else
                current = 0;

            var updated = Math.Max(0, current + deltaSeconds);
            atrailText.Text = $"{updated}s";
            var id = instanceCombo.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(id)) CBASTerminalBus.SendCommand(id, $"set ATrailSeconds {updated}");
        }

        // Helper: Set trail/stop value from TextBox when user types and presses Enter
        private void SetTrailValueFromTextBox(string propertyName, TextBox box)
        {
            var id = instanceCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                return;

            // Try to parse the user's input
            string input = (box.Text ?? "").Trim();
            if (!int.TryParse(input, out int newVal))
            {
                // Invalid input - restore previous value or show error
                logText.AppendText($"! Invalid input '{input}' - must be a number\n");
                if (autoScrollChk.IsChecked == true)
                    logText.ScrollToEnd();
                return;
            }

            // Apply min/max constraints
            if (propertyName == "StopTicks")
            {
                newVal = Math.Max(StopTicksMin, Math.Min(StopTicksMax, newVal));
            }
            else if (propertyName == "TargetATicks")
            {
                newVal = Math.Max(TargetATicksMin, Math.Min(TargetATicksMax, newVal));
            }
            else
            {
                newVal = Math.Max(TrailTicksMin, Math.Min(TrailTicksMax, newVal));
            }

            // Send command to strategy
            string cmd = $"set {propertyName} {newVal}";
            CBASTerminalBus.SendCommand(id, cmd);

            // Log the command
            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();

            // Update the display to the validated value
            box.Text = newVal.ToString();
        }

        // Helper: Set A-trail seconds from TextBox when user types and presses Enter
        private void SetATrailValueFromTextBox()
        {
            var id = instanceCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                return;

            // Try to parse the user's input (extract number from formats like "123" or "123s")
            string input = (atrailText.Text ?? "").Trim().TrimEnd('s', 'S', ' ');
            if (!int.TryParse(input, out int newVal))
            {
                // Invalid input - restore previous value or show error
                logText.AppendText($"! Invalid input '{atrailText.Text}' - must be a number (optionally followed by 's')\n");
                if (autoScrollChk.IsChecked == true)
                    logText.ScrollToEnd();
                return;
            }

            // Apply min/max constraints (0 to 600 seconds)
            newVal = Math.Max(0, Math.Min(600, newVal));

            // Send command to strategy
            string cmd = $"set ATrailDelaySec {newVal}";
            CBASTerminalBus.SendCommand(id, cmd);

            // Log the command
            logText.AppendText($"> [{id}] {cmd}\n");
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();

            // Update the display to the validated value
            atrailText.Text = $"{newVal}s";
        }
    }
}
