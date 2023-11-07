using Golem;
using Golem.IntegrationTests.Tools;
using GolemLib;
using GolemLib.Types;

namespace Golem.Tests
{
    public class GolemTests
    {
        string golemPath = "c:\\git\\yagna\\target\\debug";

        [Fact]
        public async Task StartStop_VerifyStatusAsync()
        {
            Console.WriteLine("Path: " + golemPath);

            var golem = new Golem(golemPath);
            GolemStatus status = GolemStatus.Off;

            Action<GolemStatus> updateStatus = (v) => { 
                status = v;
            };

            golem.PropertyChanged += new PropertyChangedHandler<GolemStatus>(nameof(IGolem.Status), updateStatus).Subscribe();

            await golem.Start();

            Assert.Equal(GolemStatus.Ready, status);

            await golem.Stop();

            Assert.Equal(GolemStatus.Off, status);
        }
    }
}
