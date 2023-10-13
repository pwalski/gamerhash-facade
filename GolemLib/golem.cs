﻿namespace GolemLib;

using GolemLib.Types;
using System.Threading.Tasks;
using System.ComponentModel;

public interface IGolem : INotifyPropertyChanged
{
    public event EventHandler<Events.JobStarted> OnJobStarted
    {
        add { }
        remove { }
    }
    public event EventHandler<Events.JobFinished> OnJobFinished
    {
        add { }
        remove { }
    }
    public event EventHandler<Events.PaymentConfirmed> OnPaymentConfirmed
    {
        add { }
        remove { }
    }


    public GolemPrice Price { get; set; }
    public string WalletAddress { get; set; }
    /// <summary>
    /// Benchmarked network speed in B/s.
    /// </summary>
    /// <param name="speed"></param>
    public uint SetNetworkSpeed { get; set; }
    public GolemStatus Status { get; }
    /// <summary>
    /// You can either listen to PropertyChanged notifications for this property
    /// or use `OnJobStarted` and `OnJobFinished` events.
    /// This property is designed to work better with WPF binding contexts.
    /// </summary>
    public Job? CurrentJob { get; }

    /// <summary>
    /// Node identification in Golem network.
    /// </summary>
    public string NodeId { get; }

    public Task StartYagna();
    /// <summary>
    /// Shutdown all Golem processes even if any job is in progress.
    /// </summary>
    /// <returns></returns>
    public Task StopYagna();
    /// <summary>
    /// Returns true if process can be suspended without stopping computations.
    /// When job is in progress, `Provider` will be stopped after it is finished.
    /// `JobFinished` event will be generated then.
    /// If you want to stop anyway, use `StopYagna` method.
    /// </summary>
    /// <returns></returns>
    public Task<bool> Suspend();
    /// <summary>
    /// Allow Provider to run tasks again.
    /// TODO: Might be redundant. Consider leaving only `StartYagna`.
    /// </summary>
    /// <returns></returns>
    public Task Resume();

    /// <summary>
    /// Don't accept tasks from this Node.
    /// Use in case of malicious Requestors.
    /// </summary>
    /// <param name="node_id"></param>
    /// <returns></returns>
    public Task BlacklistNode(string node_id);
    /// <summary>
    /// List all jobs that were running during period of time.
    /// In normal flow events api should be used to track jobs, but in case of application
    /// crash events can be lost and there might be a need to verify historical jobs.
    /// </summary>
    /// <param name="since">Only jobs started after this timestamp will be returned.</param>
    /// <returns></returns>
    public Task<List<Job>> ListJobs(DateTime since);
}

