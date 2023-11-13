﻿using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Golem;
using Golem.GolemUI.Src;
using Golem.Tools;
using Golem.Yagna;
using Golem.Yagna.Types;
using GolemLib;
using GolemLib.Types;

namespace Golem
{
    public class Golem : IGolem, IAsyncDisposable
    {
        private YagnaService Yagna { get; set; }
        private Provider Provider { get; set; }
        private ProviderConfigService ProviderConfig { get; set; }

        private readonly HttpClient HttpClient;

        private GolemPrice price;
        public GolemPrice Price {
            get
            {
                return price;
            }
            set
            {
                price = value;
                OnPropertyChanged();
            }
        }
        
        public uint NetworkSpeed { get; set; }


        private GolemStatus status;
        public GolemStatus Status
        {
            get { return status; }
            set { status = value; OnPropertyChanged(); }
        }

        public IJob? CurrentJob => null;

        public string NodeId {
            get { return Yagna.Id?.NodeId ?? ""; }
        }

        public string WalletAddress {
            get {
                var walletAddress = ProviderConfig.WalletAddress;
                if(walletAddress ==null || walletAddress.Length==0)
                    walletAddress = Yagna.Id?.NodeId;
                return walletAddress ?? "";
            }

            set => ProviderConfig.WalletAddress = value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public Task BlacklistNode(string node_id)
        {
            throw new NotImplementedException();
        }

        public Task<List<IJob>> ListJobs(DateTime since)
        {
            throw new NotImplementedException();
        }

        public Task Resume()
        {
            throw new NotImplementedException();
        }

        public async Task Start()
        {
            Status = GolemStatus.Starting;

            bool openConsole = false;

            var yagnaOptions = YagnaOptionsFactory.CreateStartupOptions(openConsole);

            var success = await StartupYagnaAsync(yagnaOptions);

            if (success)
            {
                var defaultKey = Yagna.AppKeyService.Get("default") ?? Yagna.AppKeyService.Get("autoconfigured");
                if (defaultKey is not null)
                {
                    if (StartupProvider(yagnaOptions))
                    {
                        Status = GolemStatus.Ready;
                    }
                    else
                    {
                        Status = GolemStatus.Error;
                    }
                }
            }
            else
            {
                Status = GolemStatus.Error;
            }

            OnPropertyChanged("WalletAddress");
            OnPropertyChanged("NodeId");
        }

        public async Task Stop()
        {
            await Provider.Stop();
            await Yagna.Stop();
            Status = GolemStatus.Off;
        }

        public Task<bool> Suspend()
        {
            throw new NotImplementedException();
        }

        public Golem(string golemPath, string? dataDir)
        {
            var prov_datadir = dataDir != null ? Path.Combine(dataDir, "provider") : null;
            var yagna_datadir = dataDir != null ? Path.Combine(dataDir, "yagna") : null;

            Yagna = new YagnaService(golemPath, yagna_datadir);
            Provider = new Provider(golemPath, prov_datadir);
            ProviderConfig = new ProviderConfigService(Provider, YagnaOptionsFactory.DefaultNetwork);

            HttpClient = new HttpClient
            {
                BaseAddress = new Uri(YagnaOptionsFactory.DefaultYagnaApiUrl)
            };
        }

        private async Task<bool> StartupYagnaAsync(YagnaStartupOptions yagnaOptions)
        {
            var success = Yagna.Run(yagnaOptions);

            if(!success)
                return false;

            var account = WalletAddress;

            if (!yagnaOptions.OpenConsole)
            {
                Yagna.BindErrorDataReceivedEvent(OnYagnaErrorDataRecv);
                Yagna.BindOutputDataReceivedEvent(OnYagnaOutputDataRecv);
            }

            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", yagnaOptions.AppKey);

            Thread.Sleep(700);

            //yagna is starting and /me won't work until all services are running
            for (int tries = 0; tries < 300; ++tries)
            {
                Thread.Sleep(300);

                if (Yagna.HasExited) // yagna has stopped
                {
                    throw new Exception("Failed to start yagna ...");
                }

                try
                {
                    var response = HttpClient.GetAsync($"/me").Result;
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new Exception("Unauthorized call to yagna daemon - is another instance of yagna running?");
                    }
                    var txt = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptionsBuilder()
                                    .WithJsonNamingPolicy(JsonNamingPolicy.CamelCase)
                                    .Build();

                    MeInfo? meInfo = JsonSerializer.Deserialize<MeInfo>(txt, options) ?? null;
                    //sanity check
                    if (meInfo != null)
                    {
                        if(account == null || account.Length == 0)
                            account = meInfo.Identity;
                        break;
                    }
                    throw new Exception("Failed to get key");

                }
                catch (Exception)
                {
                    // consciously swallow the exception... presumably REST call error...
                }
            }

            Yagna.PaymentService.Init(yagnaOptions.Network, PaymentDriver.ERC20.Id, account ?? "");

            return success;
        }

        public bool StartupProvider(YagnaStartupOptions yagnaOptions)
        {
            var presets = Provider.PresetConfig.ActivePresetsNames;
            if (!presets.Contains(Provider.PresetConfig.DefaultPresetName))
            {
                // Duration=0.0001 CPU=0.0001 "Init price=0.0000000000000001"
                var coefs = new Dictionary<string, decimal>
                {
                    { "Duration", 0.0001m },
                    { "CPU", 0.0001m },
                    //{ "Init price", 0.0000000000000001m }
                };
                // name "ai" as defined in plugins/*.json
                var preset = new Preset(Provider.PresetConfig.DefaultPresetName, "ai", coefs);

                Provider.PresetConfig.AddPreset(preset, out string args, out string info);
                Console.WriteLine($"Args {args}");
                Console.WriteLine($"Args {info}");

            }
            Provider.PresetConfig.ActivatePreset(Provider.PresetConfig.DefaultPresetName);

            foreach (string preset in presets)
            {
                if(preset != Provider.PresetConfig.DefaultPresetName)
                {
                    Provider.PresetConfig.DeactivatePreset(preset);
                }
                Console.WriteLine($"Preset {preset}");
            }

            return Provider.Run(yagnaOptions.AppKey, Network.Goerli, yagnaOptions.YagnaApiUrl, true, true);
        }

        void OnYagnaErrorDataRecv(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine($"[Error]: {e.Data}");
        }
        void OnYagnaOutputDataRecv(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine($"[Data]: {e.Data}");
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
        }
    }
}