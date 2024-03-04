﻿using System.Diagnostics;
using Golem.Yagna.Types;
using System.Text.Json;
using Golem.Tools;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Net.Http.Headers;

namespace Golem.Yagna
{
    public class YagnaStartupOptions
    {
        public string AppKey { get; set; } = "";

        public string? PrivateKey { get; set; }

        public bool Debug { get; set; }

        public string YagnaApiUrl { get; set; } = "";
        public Network Network { get; set; } = Network.Holesky;
        public PaymentDriver PaymentDriver { get; set; } = PaymentDriver.ERC20;
    }



    //[JsonObject(MemberSerialization.OptIn)]
    public class IdInfo
    {
        // [JsonPropertyName("alias")]
        public string? Alias { get; set; }

        // [JsonPropertyName("default")]
        public bool IsDefault { get; set; }

        // [JsonPropertyName("locked")]
        public bool IsLocked { get; set; }

        // [JsonPropertyName("nodeId")]
        public string NodeId { get; set; }

        public IdInfo(bool _isDefault, bool _isLocked, string? _alias, string _nodeId)
        {
            IsDefault = _isDefault;
            IsLocked = _isLocked;
            Alias = _alias;
            NodeId = _nodeId;
        }

        [JsonConstructor]
        public IdInfo()
        {
            NodeId = "";
        }
    }

    public class YagnaService
    {
        private readonly string _yaExePath;
        private readonly string? _dataDir;
        private Process? YagnaProcess { get; set; }
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        private EnvironmentBuilder Env
        {
            get
            {
                var certs = Path.Combine(Path.GetDirectoryName(_yaExePath) ?? "", "cacert.pem");
                var env = new EnvironmentBuilder();
                env = File.Exists(certs) ? env.WithSslCertFile(certs) : env;
                env = _dataDir != null ? env.WithYagnaDataDir(_dataDir) : env;
                return env;
            }
        }

        public YagnaService(string golemPath, string dataDir, ILoggerFactory? loggerFactory)
        {
            loggerFactory = loggerFactory == null ? NullLoggerFactory.Instance : loggerFactory;
            _logger = loggerFactory.CreateLogger<YagnaService>();
            _yaExePath = Path.GetFullPath(Path.Combine(golemPath, ProcessFactory.BinName("yagna")));
            _dataDir = Path.GetFullPath(dataDir);
            if (!File.Exists(_yaExePath))
            {
                throw new Exception($"File not found: {_yaExePath}");
            }
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(YagnaOptionsFactory.DefaultYagnaApiUrl)
            };
        }

        internal string ExecToText(params string[] arguments)
        {
            return new ProcessFactory(_yaExePath, _logger).WithEnv(Env.Build()).ExecToText(arguments);
        }

        internal async Task<string> ExecToTextAsync(params string[] arguments)
        {
            return await Task.Run(() => Task.FromResult(ExecToText(arguments)));
        }

        internal T? Exec<T>(params string[] arguments) where T : class
        {
            return new ProcessFactory(_yaExePath, _logger).WithEnv(Env.Build()).Exec<T>(arguments);
        }

        internal async Task<T?> ExecAsync<T>(params string[] arguments) where T : class
        {
            // TODO: This should be funciton in ProcessFactory.
            var text = await ExecToTextAsync(arguments);
            var options = new JsonSerializerOptionsBuilder()
                .WithJsonNamingPolicy(JsonNamingPolicy.CamelCase)
                .Build();
            return JsonSerializer.Deserialize<T>(text, options);
        }

        internal YagnaStartupOptions StartupOptions()
        {
            return YagnaOptionsFactory.CreateStartupOptions();
        }

        public IdService Ids
        {
            get
            {
                return new IdService(this);
            }
        }

        // public IdInfo? Id => Exec<Result<IdInfo>>("--json", "id", "show")?.Ok;

        public IdInfo? Id
        {
            get
            {
                if (YagnaProcess == null)
                {
                    return null;
                }
                try
                {
                    return Exec<Result<IdInfo>>("--json", "id", "show")?.Ok;
                }
                catch (Exception e)
                {
                    _logger.LogError($"Failed to get Node Id. Err {e}");
                    return null;
                }

            }
        }

        public PaymentService PaymentService
        {
            get
            {
                return new PaymentService(this);
            }
        }

        public AppKeyService AppKeyService
        {
            get
            {
                return new AppKeyService(this, null);
            }
        }

        public bool HasExited => YagnaProcess?.HasExited ?? true;

        public void Run(YagnaStartupOptions options, Func<int, string, Task> exitHandler, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (YagnaProcess != null)
            {
                throw new GolemException("Yagna process is already running");
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AppKey);

            string debugFlag = "";
            if (options.Debug)
            {
                debugFlag = "--debug";
            }

            EnvironmentBuilder environment = Env;
            environment = options.YagnaApiUrl != null ? environment.WithYagnaApiUrl(options.YagnaApiUrl) : environment;
            environment = options.PrivateKey != null ? environment.WithPrivateKey(options.PrivateKey) : environment;
            environment = options.AppKey != null ? environment.WithAppKey(options.AppKey) : environment;

            var args = $"service run {debugFlag}".Split();
            YagnaProcess = ProcessFactory.StartProcess(_yaExePath, args, environment.Build());
            ChildProcessTracker.AddProcess(YagnaProcess);

            YagnaProcess.WaitForExitAsync()
                .ContinueWith(result =>
                {
                    if (YagnaProcess != null && YagnaProcess.HasExited)
                    {
                        var exitCode = YagnaProcess?.ExitCode ?? throw new GolemException("Unable to get Yagna process exit code");
                        exitHandler(exitCode, "Yagna");
                    }
                    YagnaProcess = null;
                });
        }

        public async Task Stop(int stopTimeoutMs = 30_000)
        {
            if (YagnaProcess == null)
                return;
            if (YagnaProcess.HasExited)
            {
                YagnaProcess = null;
                return;
            }
            _logger.LogInformation("Stopping Yagna process");
            await ProcessFactory.StopProcess(YagnaProcess, stopTimeoutMs, _logger);
            YagnaProcess = null;
        }

        public async Task<MeInfo?> Me(CancellationToken cancellationToken)
        {
            var response = _httpClient.GetAsync($"/me", cancellationToken).Result;
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new Exception("Unauthorized call to yagna daemon - is another instance of yagna running?");
            }
            var txt = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptionsBuilder()
                            .WithJsonNamingPolicy(JsonNamingPolicy.CamelCase)
                            .Build();

            return JsonSerializer.Deserialize<MeInfo>(txt, options) ?? null;
        }

        public async Task<string?> WaitForIdentityAsync(CancellationToken cancellationToken)
        {
            string? identity = null;

            //yagna is starting and /me won't work until all services are running
            for (int tries = 0; tries < 300; ++tries)
            {
                Thread.Sleep(300);
                cancellationToken.ThrowIfCancellationRequested();

                if (HasExited) // yagna has stopped
                {
                    throw new Exception("Failed to start yagna ...");
                }

                try
                {
                    MeInfo? meInfo = await Me(cancellationToken);
                    //sanity check
                    if (meInfo != null)
                    {
                        if (String.IsNullOrEmpty(identity))
                            identity = meInfo.Identity;
                        break;
                    }
                    throw new Exception("Failed to get key");

                }
                catch (Exception)
                {
                    // consciously swallow the exception... presumably REST call error...
                }
            }
            return identity;
        }

        /// TODO: Reconsider API of this function.
        public Task StartActivityLoop(CancellationToken token, Action<Job?> setCurrentJob, IJobsUpdater jobs)
        {
            token.Register(_httpClient.CancelPendingRequests);
            return new ActivityLoop(_httpClient, token, _logger).Start(
                setCurrentJob,
                jobs,
                token
            );
        }

        /// TODO: Reconsider API of this function.
        public Task StartInvoiceEventsLoop(CancellationToken token, IJobsUpdater jobs)
        {
            token.Register(_httpClient.CancelPendingRequests);
            return new InvoiceEventsLoop(_httpClient, token, _logger).Start(jobs.UpdatePaymentStatus, jobs.UpdatePaymentConfirmation);
        }

        private void BindOutputEventHandlers(Process proc)
        {
            proc.OutputDataReceived += OnOutputDataRecv;
            proc.ErrorDataReceived += OnErrorDataRecv;
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();
        }

        private void OnOutputDataRecv(object sender, DataReceivedEventArgs e)
        {
            _logger.LogInformation($"{e.Data}");
        }

        private void OnErrorDataRecv(object sender, DataReceivedEventArgs e)
        {
            _logger.LogInformation($"{e.Data}");
        }
    }

    public class KeyInfo
    {
        public string Name { get; set; }

        public string? Key { get; set; }

        public string Id { get; set; }

        public string? Role { get; set; }

        public DateTime? Created { get; set; }

        [JsonConstructor]
        public KeyInfo()
        {
            Name = "";
            Id = "";
        }
    }

    public class MeInfo
    {
        public string? Name { get; set; }
        public string? Identity { get; set; }

        public string? Role { get; set; }

        [JsonConstructor]
        public MeInfo()
        {
        }
    }

    public class AppKeyService
    {
        private readonly YagnaService _yagnaService;
        private readonly string? _id;

        internal AppKeyService(YagnaService yagnaService, string? id)
        {
            this._yagnaService = yagnaService;
            this._id = id;
        }

        private string[] prepareArgs(params string[] arguments)
        {
            var execArgs = new List<string>(3 + arguments.Length);
            execArgs.Add("--json");
            if (_id != null)
            {
                execArgs.Add("--id");
                execArgs.Add(_id);
            }
            execArgs.Add("app-key");
            execArgs.AddRange(arguments);

            return execArgs.ToArray();
        }

        private T? Exec<T>(params string[] arguments) where T : class
        {
            return _yagnaService.Exec<T>(prepareArgs(arguments));
        }

        private async Task<T?> ExecAsync<T>(params string[] arguments) where T : class
        {
            return await _yagnaService.ExecAsync<T>(prepareArgs(arguments));
        }

        public string? Create(string name)
        {
            return Exec<string>("create", name);
        }
        public async Task<string?> CreateAsync(string name)
        {
            return await ExecAsync<string>("create", name);
        }

        public void Drop(string name)
        {
            var opt = Exec<string>("drop", name);
        }

        public KeyInfo? Get(string name)
        {
            var keys = List();
            if (keys is not null && keys.Count > 0)
            {
                if (keys.Count > 1)
                {
                    return keys.Where(x => x.Name.Equals(name)).FirstOrDefault();
                }
                else if (keys.Count == 1)
                {
                    return keys.First();
                }
            }
            return null;
        }

        public List<KeyInfo> List()
        {
            var output = new List<KeyInfo>();
            int tries = 0;
            while (output.Count == 0)
            {
                List<KeyInfo>? o;
                try
                {
                    o = Exec<List<KeyInfo>>("list");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.ToString());
                    o = null;
                }
                if (o != null)
                    output.AddRange(o);
                else
                    Thread.Sleep(1000);
                if (++tries == 10)
                {
                    throw new Exception("Failed to obtain key list from yagna service");
                }
            }
            return output;
        }

        public async Task<List<KeyInfo>> ListAsync()
        {
            var output = await ExecAsync<List<KeyInfo>>("list");
            return output ?? new List<KeyInfo>();
        }


    }

    public class IdService
    {
        readonly YagnaService _yagna;

        internal IdService(YagnaService yagna)
        {
            _yagna = yagna;
        }

        public List<IdInfo> List()
        {
            var table = _yagna.Exec<List<IdInfo>>("--json", "id", "list");
            return table ?? new List<IdInfo>();
        }
    }


    public class PaymentService
    {
        readonly YagnaService _yagna;

        internal PaymentService(YagnaService yagna)
        {
            _yagna = yagna;
        }

        public void Init(string account)
        {
            var yagnaOptions = _yagna.StartupOptions();
            _yagna.ExecToText("payment", "init", "--receiver", "--network", yagnaOptions.Network.Id, "--driver", yagnaOptions.PaymentDriver.Id, "--account", account);
        }

        public async Task<PaymentStatus?> Status(Network network, string driver, string account)
        {
            return await _yagna.ExecAsync<PaymentStatus>("--json", "payment", "status", "--network", network.Id, "--driver", driver, "--account", account);
        }

        public async Task<string?> ExitTo(Network network, string driver, string account, string? destination)
        {
            if (destination == null)
            {
                return null;
            }
            return await _yagna.ExecToTextAsync("--json", "payment", "exit", "--network", network.Id, "--driver", driver, "--account", account, "--to-address", destination);
        }

        public async Task<ActivityStatus?> ActivityStatus()
        {
            return await _yagna.ExecAsync<ActivityStatus>("--json", "activity", "status");
        }

    }
}
