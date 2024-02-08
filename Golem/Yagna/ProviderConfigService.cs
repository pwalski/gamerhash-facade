﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Golem
{
    using global::Golem.Yagna.Types;
    using global::Golem.Yagna;
    using GolemLib.Types;

    namespace GolemUI.Src
    {
        public class ProviderConfigService
        {
            private readonly Provider _provider;
            public Network Network { get; private set; }

            public ProviderConfigService(Provider provider, Network network)
            {
                _provider = provider;
                Network = network;
            }

            public string WalletAddress
            {
                get
                {
                    return _provider.Config?.Account ?? "";
                }
                set
                {
                    UpdateWalletAddress(value);
                }
            }

            public GolemPrice GolemPrice
            {
                get
                {
                    var presets = _provider.PresetConfig.DefaultPresets;
                    
                    if(presets.Count == 0)
                        return new GolemPrice();

                    //TODO handle different values in multiple runtimes/presets
                    var preset = presets[0];

                    if(!preset.UsageCoeffs.TryGetValue("ai-runtime.requests", out var numRequests))
                        numRequests = 0;
                    if (!preset.UsageCoeffs.TryGetValue("golem.usage.duration_sec", out var duration))
                        duration = 0;
                    if (!preset.UsageCoeffs.TryGetValue("golem.usage.gpu-sec", out var gpuSec))
                        gpuSec = 0;

                    var initPrice = preset.InitialPrice ?? 0m;

                    return new GolemPrice
                    {
                        EnvPerHour = duration,
                        StartPrice = initPrice,
                        GpuPerHour = gpuSec,
                        NumRequests = numRequests
                    };
                }

                set
                {
                    if (!_provider.UpdateStatus()) {
                        _provider.PresetConfig.InitilizeDefaultPresets();
                    }
                    _provider.PresetConfig.UpdatePrices(new Dictionary<string, decimal>
                    {
                        { "ai-runtime.requests", value.NumRequests },
                        { "golem.usage.duration_sec", value.EnvPerHour },
                        { "golem.usage.gpu-sec", value.GpuPerHour },
                        { "Initial", value.StartPrice }
                    });
                }
            }

            private void UpdateWalletAddress(string? walletAddress = null)
            {
                var config = _provider.Config;
                if (config != null)
                {
                    config.Account = walletAddress;
                    _provider.Config = config;
                }
            }
        }
    }

}
