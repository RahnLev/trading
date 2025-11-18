// StrategyHelpers/TelemetryClient.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace StrategyHelpers
{
    public interface IStrategyControl
    {
        string InstanceId { get; }
        void OnRemark(string message);
        void ApplyCommand(CommandDto cmd);
    }

    public class TelemetryClient : IDisposable
    {
        private readonly string baseUrl;
        private readonly HttpClient http;
        private readonly IStrategyControl control;
        private readonly JsonSerializerSettings jsonSettings;
        private CancellationTokenSource pollCts;
        private string sinceIso;

        public TelemetryClient(string baseUrl, IStrategyControl control, TimeSpan? timeout = null)
        {
            this.baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            this.control = control ?? throw new ArgumentNullException(nameof(control));

            http = new HttpClient
            {
                Timeout = timeout ?? TimeSpan.FromMilliseconds(400)
            };

            // Match camelCase expected by many web APIs
            jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };

            sinceIso = DateTime.UtcNow.AddMinutes(-5).ToString("o");
        }

        public void StartPolling(int intervalMs = 300)
        {
            StopPolling(); // ensure single poller

            pollCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!pollCts.IsCancellationRequested)
                {
                    try
                    {
                        var pollTimeout = TimeSpan.FromMilliseconds(Math.Max(250, intervalMs));
                        using (var cts = new CancellationTokenSource(pollTimeout))
                        {
                            var url = $"{baseUrl}/strategy/commands?instanceId={Uri.EscapeDataString(control.InstanceId)}&since={Uri.EscapeDataString(sinceIso)}";
                            var resp = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
                            if (resp.IsSuccessStatusCode)
                            {
                                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                var cmds = JsonConvert.DeserializeObject<List<CommandDto>>(text) ?? new List<CommandDto>();

                                if (cmds.Count > 0)
                                {
                                    foreach (var cmd in cmds)
                                    {
                                        if (string.Equals(cmd?.Type, "Remark", StringComparison.OrdinalIgnoreCase) && cmd?.Remark != null)
                                            control.OnRemark(cmd.Remark.Message);

                                        control.ApplyCommand(cmd);
                                    }

                                    await AckAsync(cmds).ConfigureAwait(false);
                                    sinceIso = DateTime.UtcNow.ToString("o");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Swallow errors; this is a best-effort background poller
                    }

                    try
                    {
                        await Task.Delay(intervalMs, pollCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore cancellation
                    }
                }
            }, pollCts.Token);
        }

        public void StopPolling()
        {
            try
            {
                if (pollCts != null)
                {
                    pollCts.Cancel();
                    pollCts.Dispose();
                    pollCts = null;
                }
            }
            catch { }
        }

        public void SendTelemetryFireAndForget(TelemetryDto t, int timeoutMs = 200)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs)))
                    {
                        var json = JsonConvert.SerializeObject(t, jsonSettings);
                        using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                        {
                            await http.PostAsync($"{baseUrl}/strategy/telemetry", content, cts.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch
                {
                    // ignore best-effort telemetry errors
                }
            });
        }

        private async Task AckAsync(List<CommandDto> cmds)
        {
            try
            {
                var dto = new AckDto
                {
                    InstanceId = control.InstanceId,
                    CommandIds = cmds.ConvertAll(c => c.CommandId ?? string.Empty).ToArray()
                };

                var json = JsonConvert.SerializeObject(dto, jsonSettings);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    await http.PostAsync($"{baseUrl}/strategy/ack", content).ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }
        }

        public void Dispose()
        {
            try
            {
                StopPolling();
                http?.Dispose();
            }
            catch { }
        }
    }

    // Minimal DTOs so the file compiles.
    // Extend these to match your server’s contract.

    public class CommandDto
    {
        public string CommandId { get; set; }
        public string Type { get; set; }
        public RemarkDto Remark { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    public class RemarkDto
    {
        public string Message { get; set; }
    }

    public class TelemetryDto
    {
        public string InstanceId { get; set; }
        public string Event { get; set; }
        public object Data { get; set; }
        public DateTime Utc { get; set; } = DateTime.UtcNow;
    }

    public class AckDto
    {
        public string InstanceId { get; set; }
        public string[] CommandIds { get; set; }
    }
}
