using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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

        // Optional: bounds
        private const int TrailTicksMin = 0;
        private const int TrailTicksMax = 1000;

        // Right sidebar controls
        private TextBox tpmText;
        private TextBox atrailText;
        private DateTime lastTpmUpdate = DateTime.MinValue;
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
        private TextBox qtyText;
        private Button closeAllBtn;

        private readonly ConcurrentQueue<CBASTerminalLogEntry> queue = new ConcurrentQueue<CBASTerminalLogEntry>();
        private readonly DispatcherTimer pumpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private const int MaxLines = 5000;
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

            // Trailing ticks controls
            var trailGroup = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 12, 0, 0) };
            trailGroup.Children.Add(new TextBlock { Text = "Trailing ticks", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });

            // Row A
            var rowA = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            rowA.Children.Add(new TextBlock { Text = "A:", VerticalAlignment = VerticalAlignment.Center, Width = 20 });

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

            trailAText = new TextBox { Width = 60, Text = "--", Margin = new Thickness(0, 0, 4, 0), IsReadOnly = true, HorizontalContentAlignment = HorizontalAlignment.Center };

            minusA.Click += (s, e) => AdjustTrailAndSend("ATrailTicks", trailAText, -2);
            plusA.Click += (s, e) => AdjustTrailAndSend("ATrailTicks", trailAText, +2);

            rowA.Children.Add(minusA);
            rowA.Children.Add(trailAText);
            rowA.Children.Add(plusA);

            // Row B
            var rowB = new StackPanel { Orientation = Orientation.Horizontal };
            rowB.Children.Add(new TextBlock { Text = "B:", VerticalAlignment = VerticalAlignment.Center, Width = 20 });

            trailBText = new TextBox { Width = 60, Text = "--", Margin = new Thickness(0, 0, 4, 0), IsReadOnly = true, HorizontalContentAlignment = HorizontalAlignment.Center };

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
            rightSidebar.Children.Add(trailGroup);

            // Ticks/min row (label and textbox on the same row)
            var rowTicks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 6) };
            rowTicks.Children.Add(new TextBlock { Text = "Ticks/min:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            tpmText = new TextBox { IsReadOnly = true, Width = 100, Text = "--" };
            rowTicks.Children.Add(tpmText);
            rightSidebar.Children.Add(rowTicks);

            // A-trail row (moved to right panel, same look/size as trail A/B, with +/- buttons)
            var rowATrail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            rowATrail.Children.Add(new TextBlock { Text = "A-trail:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 60, Margin = new Thickness(0, 0, 6, 0) });

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
                IsReadOnly = true,                   // keep read-only; will be populated by status or future function
                Width = 60,                          // same width as trail A/B textboxes
                Text = "-- s",
                Margin = new Thickness(0, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

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

            // Left: Instance + combo + Refresh
            var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftPanel.Children.Add(new TextBlock { Text = "Instance:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            instanceCombo = new ComboBox { Width = 280, Margin = new Thickness(0, 0, 6, 0), IsEditable = false };

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

            var refreshBtn = new Button { Content = "Refresh", Margin = new Thickness(0, 0, 0, 0) };
            refreshBtn.Click += (s, e) => RefreshInstances();

            leftPanel.Children.Add(instanceCombo);
            leftPanel.Children.Add(refreshBtn);

            // Middle: Qty + TEST LONG/SHORT
            var middlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var qtyLbl = new TextBlock { Text = "Qty:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            qtyText = new TextBox { Text = "5", Width = 48, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Qty per leg" };

            // Right: Clear + Pause + Auto-scroll
            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

            testLongBtn = new Button { Content = "TEST LONG", Margin = new Thickness(0, 0, 6, 0) };
            testShortBtn = new Button { Content = "TEST SHORT", Margin = new Thickness(0, 0, 0, 0) };

            testLongBtn.Click += (s, e) => SendTestCommand(true);
            testShortBtn.Click += (s, e) => SendTestCommand(false);

            middlePanel.Children.Add(qtyLbl);
            middlePanel.Children.Add(qtyText);
            middlePanel.Children.Add(testLongBtn);
            middlePanel.Children.Add(testShortBtn);

            tpmPollTimer.Tick += (s, e) =>
            {
                if ((DateTime.UtcNow - lastTpmUpdate).TotalSeconds > 5)
                    tpmText.Text = "--";
            };
            tpmPollTimer.Start();

            closeAllBtn = new Button { Content = "Close Trades", Margin = new Thickness(0, 0, 12, 0) };
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

            clearBtn = new Button { Content = "Clear", Margin = new Thickness(0, 0, 12, 0) };
            clearBtn.Click += (s, e) => logText?.Clear();

            pauseBtn = new Button { Content = "Pause Strategy", Margin = new Thickness(0, 0, 12, 0) };
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
            Grid.SetRow(inputPanel, 2);

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

        private void HandleLog(CBASTerminalLogEntry entry) => queue.Enqueue(entry);

        private void FlushQueueToUI()
        {
            if (queue.IsEmpty) return;

            var sb = new StringBuilder();
            while (queue.TryDequeue(out var e))
            {
                // Intercept trail status lines: format we’ll emit from the strategy is:
                // [TS] rem=123 armed=false
                if (e.Message != null && e.Message.StartsWith("[TS]", StringComparison.OrdinalIgnoreCase))
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

                        var text = rem >= 0 ? $"{rem}s{(armed ? " (armed)" : "")}" : "-- s";
                        atrailText.Text = text;
                        lastTsUpdate = DateTime.UtcNow;
                    }
                    catch
                    {
                        atrailText.Text = "-- s";
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
                            tpmText.Text = tpm.ToString();
                            lastTpmUpdate = DateTime.UtcNow;
                        }
                        else
                        {
                            tpmText.Text = "--";
                        }
                    }
                    catch
                    {
                        tpmText.Text = "--";
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
                        if (a.HasValue) trailAText.Text = a.Value.ToString();
                        if (b.HasValue) trailBText.Text = b.Value.ToString();
                    }
                    catch { /* ignore parse errors */ }
                    continue; // don't append this line to the log
                }

                // Normal log append
                sb.AppendLine($"[{e.Timestamp:HH:mm:ss.fff}] [{e.Level}] [{e.Instrument}] Bar={e.Bar} :: {e.Message}");
            }

            if (sb.Length == 0) return;

            logText.AppendText(sb.ToString());
            var lines = logText.LineCount;
            if (lines > MaxLines)
            {
                int removeLines = lines - MaxLines;
                int keepStartIndex = logText.GetCharacterIndexFromLineIndex(removeLines);
                int lengthToRemove = keepStartIndex;
                if (lengthToRemove > 0)
                {
                    logText.Select(0, lengthToRemove);
                    logText.SelectedText = string.Empty;
                }
                logText.CaretIndex = logText.Text.Length;
            }
            if (autoScrollChk.IsChecked == true)
                logText.ScrollToEnd();
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

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            var id = instanceCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                return;

            paused = !paused;
            var cmd = paused ? "set Paused true" : "set Paused false";
            CBASTerminalBus.SendCommand(id, cmd);

            pauseBtn.Content = paused ? "Resume Strategy" : "Pause Strategy";

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
            newVal = Math.Max(TrailTicksMin, Math.Min(TrailTicksMax, newVal));

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
    }
}
