using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TRRabbitMQ.Core.Messages;
using TRRabbitMQ.Core.Messages.Implementations;
using TRRabbitMQ.Core.Models;
using TRRabbitMQ.Core.Models.Enums;
using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Tests.Connections.IntegrationTests.Extensions;
using TRRabbitMQ.Core.Tests.Connections.IntegrationTests.Fixtures;
using Xunit;

namespace TRRabbitMQ.Core.Tests.Connections.IntegrationTests
{
    [CollectionDefinition("BusConnectionTests", DisableParallelization = true)]
    public class BusConnectionTests : IClassFixture<BusConnectionFixture>
    {
        public BusConnectionTests(BusConnectionFixture connectionFixture)
        {
            _connectionFixture = connectionFixture;
        }

        private readonly BusConnectionFixture _connectionFixture;

        [Fact]
        public void GivenConnectionWhenDisposeTwiceShouldNotThrow()
        {
            _connectionFixture.Connection.Dispose();
            _connectionFixture.Connection
                .Invoking(x => x.Dispose())
                .Should().NotThrow();
        }

        [Fact]
        public void GivenConnectionWhenPublishBatchShouldQueueHaveBatchLength()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";

            var autoResetEvent = new AutoResetEvent(false);
            _connectionFixture.Connection.PublishSuccessed += _ => autoResetEvent.Set();

            _connectionFixture.Connection.PublishBatch(exchange, queue, routingKey, new[]
            {
                notification,
                notification
            });

            autoResetEvent.WaitOne();

            _connectionFixture.Connection.MessageCount(queue).Should().Be(2);
        }

        [Fact]
        public void GivenConnectionWhenPublishMessageShouldQueueHaveCountOne()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";

            var autoResetEvent = new AutoResetEvent(false);
            _connectionFixture.Connection.PublishSuccessed += _ => autoResetEvent.Set();

            _connectionFixture.Connection.Publish(exchange, queue, routingKey, notification);

            autoResetEvent.WaitOne();

            _connectionFixture.Connection.MessageCount(queue).Should().Be(1);
        }

        [Fact]
        public void GivenConnectionWhenPublishShouldAddRetryProperties()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            var data = "Notification message";
            var options = new MessageOptions(exchange, queue, routingKey, 10);
            var message = new PublishMessage(options, data, 0);

            var autoResetEvent = new AutoResetEvent(false);
            _connectionFixture.Connection.PublishSuccessed += _ => autoResetEvent.Set();
            _connectionFixture.Connection.Publish(message);

            autoResetEvent.WaitOne();

            var responseMessage = _connectionFixture.Connection.GetMessage(queue);
            responseMessage.Should().NotBeNull();
            responseMessage.GetData<string>().Should().Be(data);
            responseMessage.AttemptCount.Should().Be(1);
            responseMessage.Options.MaxAttempts.Should().Be(10);
        }

        [Fact]
        public void GivenConnectionWhenSubscribeAndThrowAndMaxAttemptsAchievedShouldQueueOnFailedQueue()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}");
            var routingKey = RoutingKey.Create(queue.Name.Value);
            var message = new PublishMessage(new MessageOptions(exchange, queue, routingKey, 1), "message");

            var autoResetEvent = new AutoResetEvent(false);

            _connectionFixture.Connection.PublishSuccessed += items =>
            {
                if (items.Any(x => x.Queue.Name.Value.EndsWith("-failed")))
                {
                    autoResetEvent.Set();
                }
            };

            _connectionFixture.Connection.Publish(message);

            _connectionFixture.Connection.Subscribe<string>(exchange, queue, routingKey, 1, (scope, m) =>
            {
                autoResetEvent.Set();
                throw new Exception();
            });

            autoResetEvent.WaitOne(); // Wait for subscribe to execute.

            autoResetEvent.WaitOne(); // Wait for failed message publishing.

            var failedQueue = Queue.Create($"{queue.Name.Value}-failed");

            _connectionFixture.Connection.MessageCount(failedQueue).Should().Be(1);
        }

        [Fact]
        public void GivenConnectionWhenSubscribeAndThrowShouldRequeueOnRetryQueue()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}");
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";

            var autoResetEvent = new AutoResetEvent(false);

            _connectionFixture.Connection.PublishSuccessed += items =>
            {
                if (items.Any(x => x.Queue.Name.Value.EndsWith("-retry")))
                {
                    autoResetEvent.Set();
                }
            };

            _connectionFixture.Connection.Publish(exchange, queue, routingKey, notification);

            _connectionFixture.Connection.Subscribe<string>(exchange, queue, routingKey, 1, (scope, message) =>
            {
                autoResetEvent.Set();
                throw new Exception();
            });

            autoResetEvent.WaitOne(); // Wait for subscribe to execute.

            autoResetEvent.WaitOne(); // Wait for retry message publishing.

            var retryQueue = Queue.Create($"{queue.Name.Value}-retry");

            _connectionFixture.Connection.MessageCount(retryQueue).Should().Be(1);
        }

        [Fact]
        public void GivenConnectionWhenSubscribeShouldExecuteCallback()
        {
            var exchange = Exchange.Create("seedwork-cqrs-bus.integration-tests", ExchangeType.Direct);
            var queue = Queue.Create($"seedwork-cqrs-bus.integration-tests.queue-{Guid.NewGuid()}")
                .WithAutoDelete();
            var routingKey = RoutingKey.Create(queue.Name.Value);
            const string notification = "Notification message";

            _connectionFixture.Connection.Publish(exchange, queue, routingKey, notification);

            IServiceScope callbackScope = null;
            IConsumerMessage callbackMessage = null;

            var autoResetEvent = new AutoResetEvent(false);

            _connectionFixture.Connection.Subscribe<string>(exchange, queue, routingKey, 1, (scope, message) =>
            {
                callbackScope = scope;
                callbackMessage = message;
                autoResetEvent.Set();

                return Task.CompletedTask;
            });

            autoResetEvent.WaitOne();

            callbackScope.Should().NotBeNull();
            callbackMessage.GetData<string>().Should().Be(notification);
        }
    }
}