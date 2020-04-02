using System;
using Microsoft.Extensions.DependencyInjection;
using TRRabbitMQ.Core.Connections;
using TRRabbitMQ.Core.Models;
using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Tests.Utils;
using TRRabbitMQ.Core.Utils;

namespace TRRabbitMQ.Core.Tests.Connections.IntegrationTests.Fixtures
{
    public class BusConnectionFixture : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        internal BusConnectionOptions BusOptions = new BusConnectionOptions
        {
            PublisherBufferTtlInMilliseconds = 1
        };

        public BusConnectionFixture()
        {
            _serviceProvider = new ServiceCollection()
                .AddSingleton(BusConnectionString.Create("amqp://guest:guest@localhost/"))
                .AddSingleton<IBusSerializer, BusSerializer>()
                .AddSingleton<BusConnection>()
                .AddSingleton(Microsoft.Extensions.Options.Options.Create(BusOptions))
                .BuildServiceProvider();
        }

        internal BusConnection Connection => _serviceProvider.GetService<BusConnection>();

        public void Dispose()
        {
            _serviceProvider.Dispose();
        }
    }
}