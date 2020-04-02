using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TRRabbitMQ.Core.Connections;
using TRRabbitMQ.Core.Messages.Implementations;
using TRRabbitMQ.Core.Models;
using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Utils;
using Xunit;
using ExchangeType = TRRabbitMQ.Core.Models.Enums.ExchangeType;

namespace TRRabbitMQ.Core.Tests.Connections.UnitTests
{
    public class BusConnectionTests
    {
        public BusConnectionTests()
        {
            _connectionFactoryMock = new Mock<IConnectionFactory>();
            _busSerializerMock = new Mock<IBusSerializer>();
            _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
            _connectionMock = new Mock<IConnection>();
            _channelMock = new Mock<IModel>();
            _scopeMock = new Mock<IServiceScope>();
            _serviceProviderMock = new Mock<IServiceProvider>();
            _basicPropertiesMock = new Mock<IBasicProperties>();
            _loggerMock = new Mock<IBusLogger>();
            _headersMock = new Mock<IDictionary<string, object>>();
            _publishBatchMock = new Mock<IBasicPublishBatch>();
            _optionsMock = new Mock<IOptions<BusConnectionOptions>>();
            _optionsMock.SetupGet(x => x.Value)
                .Returns(new BusConnectionOptions
                {
                    PublisherBufferTtlInMilliseconds = 1
                })
                .Verifiable();
            _channelMock.Setup(x => x.CreateBasicPublishBatch())
                .Returns(_publishBatchMock.Object)
                .Verifiable();
            _basicPropertiesMock.SetupGet(x => x.Headers)
                .Returns(_headersMock.Object)
                .Verifiable();
            _scopeMock.SetupGet(x => x.ServiceProvider)
                .Returns(_serviceProviderMock.Object)
                .Verifiable();
            _connectionFactoryMock.Setup(x => x.CreateConnection())
                .Returns(_connectionMock.Object)
                .Verifiable();
            _connectionMock.Setup(x => x.CreateModel())
                .Returns(_channelMock.Object)
                .Verifiable();
            _busConnection = new BusConnection(
                _connectionFactoryMock.Object,
                _busSerializerMock.Object,
                _serviceScopeFactoryMock.Object,
                _optionsMock.Object,
                _loggerMock.Object);
            _serviceScopeFactoryMock.Setup(x => x.CreateScope())
                .Returns(_scopeMock.Object)
                .Verifiable();
            _channelMock.Setup(x => x.CreateBasicProperties())
                .Returns(_basicPropertiesMock.Object)
                .Verifiable();
        }

        private readonly Mock<IConnectionFactory> _connectionFactoryMock;
        private readonly Mock<IBusSerializer> _busSerializerMock;
        private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
        private readonly Mock<IModel> _channelMock;
        private readonly Mock<IConnection> _connectionMock;
        private readonly BusConnection _busConnection;
        private readonly Mock<IServiceScope> _scopeMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<IBasicProperties> _basicPropertiesMock;
        private readonly Mock<IDictionary<string, object>> _headersMock;
        private readonly Mock<IBusLogger> _loggerMock;
        private readonly Mock<IBasicPublishBatch> _publishBatchMock;
        private readonly Mock<IOptions<BusConnectionOptions>> _optionsMock;

        [Fact]
        public void GivenConnectionWhenCreateShouldNotTryToConnect()
        {
            var busConnection = new BusConnection(
                _connectionFactoryMock.Object,
                _busSerializerMock.Object,
                _serviceScopeFactoryMock.Object,
                _optionsMock.Object,
                _loggerMock.Object);

            busConnection.Should().NotBeNull();
            _connectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never());
        }

        [Fact]
        public void GivenConnectionWhenFailToDeserializeShouldNackAndPublishFailed()
        {
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");
            const ushort deliveryTag = 1;

            _busSerializerMock.Setup(x => x.DeserializeAsync<string>(body))
                .ThrowsAsync(new SerializationException("Test message"))
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);
            
            // _channelMock.Setup(x => x.QueueDeclare(It.Is))

            _channelMock.Setup(x => x.BasicConsume(
                    queue.Name.Value,
                    false,
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<IBasicConsumer>()))
                .Callback((string queueName, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                    IDictionary<string, object> _, IBasicConsumer consumer) =>
                {
                    ((AsyncEventingBasicConsumer) consumer).HandleBasicDeliver(
                        consumerTag,
                        deliveryTag,
                        false,
                        exchange.Name.Value,
                        routingKey.Value,
                        _basicPropertiesMock.Object,
                        body).Wait();
                })
                .Returns(Guid.NewGuid().ToString());

            _publishBatchMock.Setup(x => x.Publish())
                .Callback(() => autoResetEvent.Set())
                .Verifiable();

            _busConnection.Subscribe<string>(
                exchange,
                queue,
                routingKey,
                10,
                async (scope, @event) => await @event.GetDataAsync<string>());

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(5));

            _loggerMock.Verify(x => x.WriteException(It.IsAny<string>(), It.IsAny<Exception>(),
                It.IsAny<KeyValuePair<string, object>[]>()));
            _channelMock.Verify(x => x.BasicNack(deliveryTag, false, false));

            _publishBatchMock.VerifyAll();
            
            _channelMock.Verify(x => x.QueueDeclare(
                It.Is((string y) => y.EndsWith("-failed")),
                true,
                false,
                false,
                It.IsAny<IDictionary<string, object>>()), Times.Once());
            _publishBatchMock.Verify(x => x.Add(
                ExchangeName.Default.Value,
                It.Is((string y) => y.StartsWith(queue.Name.Value) && y.EndsWith("-failed")),
                false,
                _basicPropertiesMock.Object,
                It.IsAny<byte[]>()), Times.Once());
        }

        [Fact]
        public void GivenConnectionWhenPublishFailsShouldExecuteFailedCallback()
        {
            const string notification = "test";
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");

            var headersMock = new Mock<IDictionary<string, object>>();

            _busSerializerMock.Setup(x => x.SerializeAsync(It.IsAny<object>()))
                .ReturnsAsync(body)
                .Verifiable();
            _basicPropertiesMock.Setup(x => x.Headers)
                .Returns(headersMock.Object)
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _publishBatchMock.Setup(x => x.Publish())
                .Throws(new Exception())
                .Verifiable();

            Exception callbackException = null;
            List<BatchItem> callbackItems = null;

            _busConnection.PublishFailed += (items, ex) =>
            {
                callbackItems = items.ToList();
                callbackException = ex;
                autoResetEvent.Set();
            };
            _busConnection.Publish(exchange, queue, routingKey, notification);

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(5));

            _publishBatchMock.VerifyAll();

            callbackItems.Should().NotBeNull();
            callbackItems.Should().NotBeEmpty();
            callbackException.Should().NotBeNull();
        }

        [Fact]
        public void GivenConnectionWhenPublishShouldConfigureBasicPropertiesForRetry()
        {
            const string notification = "test";
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");

            var headersMock = new Mock<IDictionary<string, object>>();

            _busSerializerMock.Setup(x => x.SerializeAsync(It.IsAny<object>()))
                .ReturnsAsync(body)
                .Verifiable();
            _basicPropertiesMock.Setup(x => x.Headers)
                .Returns(headersMock.Object)
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _publishBatchMock.Setup(x => x.Publish())
                .Callback(() => autoResetEvent.Set())
                .Verifiable();

            _busConnection.Publish(exchange, queue, routingKey, notification);

            autoResetEvent.WaitOne();

            _publishBatchMock.VerifyAll();
            _channelMock.Verify(x => x.CreateBasicProperties(), Times.Once());
            _basicPropertiesMock.VerifySet(x => x.Headers = new Dictionary<string, object>());
            headersMock.Verify(x => x.Add(nameof(ConsumerMessage.Options.MaxAttempts), It.IsAny<object>()));
            headersMock.Verify(x => x.Add(nameof(ConsumerMessage.AttemptCount), It.IsAny<object>()));
        }

        [Fact]
        public void GivenConnectionWhenPublishShouldDeclareExchangePublishMessageCloseAndDisposeChannel()
        {
            const string notification = "test";
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");
            _busSerializerMock.Setup(x => x.SerializeAsync(It.IsAny<object>()))
                .ReturnsAsync(body)
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _publishBatchMock.Setup(x => x.Publish())
                .Callback(() => autoResetEvent.Set())
                .Verifiable();

            _busConnection.Publish(exchange, routingKey, notification);

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(5));

            _publishBatchMock.VerifyAll();
            _connectionFactoryMock.Verify(x => x.CreateConnection(), Times.Once());
            _connectionMock.Verify(x => x.CreateModel(), Times.Once());
            _channelMock.Verify(x => x.ExchangeDeclare(
                exchange.Name.Value,
                exchange.Type.Value,
                exchange.Durability.IsDurable,
                exchange.IsAutoDelete,
                It.IsAny<IDictionary<string, object>>()), Times.Once());
            _channelMock.Verify(x => x.Close(), Times.Once());
            _channelMock.Verify(x => x.Dispose(), Times.Once());
        }

        [Fact]
        public void GivenConnectionWhenPublishShouldDeclareExchangeQueueAndBindRoutingKey()
        {
            const string notification = "test";
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");

            var headersMock = new Mock<IDictionary<string, object>>();

            _busSerializerMock.Setup(x => x.SerializeAsync(It.IsAny<object>()))
                .ReturnsAsync(body)
                .Verifiable();
            _basicPropertiesMock.Setup(x => x.Headers)
                .Returns(headersMock.Object)
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _publishBatchMock.Setup(x => x.Publish())
                .Callback(() => autoResetEvent.Set())
                .Verifiable();

            _busConnection.Publish(exchange, queue, routingKey, notification);

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(5));

            _publishBatchMock.VerifyAll();
            _connectionFactoryMock.Verify(x => x.CreateConnection(), Times.Once());
            _connectionMock.Verify(x => x.CreateModel(), Times.Once());
            _channelMock.Verify(x => x.ExchangeDeclare(
                exchange.Name.Value,
                exchange.Type.Value,
                exchange.Durability.IsDurable,
                exchange.IsAutoDelete,
                It.IsAny<IDictionary<string, object>>()), Times.Once());
            _channelMock.Verify(x => x.QueueDeclare(
                queue.Name.Value,
                queue.Durability.IsDurable,
                false,
                queue.IsAutoDelete,
                It.IsAny<IDictionary<string, object>>()));
            _channelMock.Verify(x => x.QueueBind(
                queue.Name.Value,
                exchange.Name.Value,
                routingKey.Value,
                It.IsAny<IDictionary<string, object>>()));
        }

        [Fact]
        public void GivenConnectionWhenPublishShouldPublishMessageAndCloseAndDisposeChannel()
        {
            const string notification = "test";
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");

            var headersMock = new Mock<IDictionary<string, object>>();

            _busSerializerMock.Setup(x => x.Serialize(It.IsAny<object>()))
                .Returns(body)
                .Verifiable();
            _basicPropertiesMock.Setup(x => x.Headers)
                .Returns(headersMock.Object)
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _publishBatchMock.Setup(x => x.Publish())
                .Callback(() => autoResetEvent.Set())
                .Verifiable();

            _busConnection.Publish(exchange, queue, routingKey, notification);

            autoResetEvent.WaitOne();

            _publishBatchMock.VerifyAll();

            _publishBatchMock.Verify(
                x => x.Add(
                    exchange.Name.Value,
                    routingKey.Value,
                    false,
                    _basicPropertiesMock.Object,
                    body), Times.Once());
            _channelMock.Verify(x => x.Close(), Times.Once());
            _channelMock.Verify(x => x.Dispose(), Times.Once());
        }

        [Fact]
        public void GivenConnectionWhenPublishSuccessedShouldExecuteSuccessedCallback()
        {
            const string notification = "test";
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");

            var headersMock = new Mock<IDictionary<string, object>>();

            _busSerializerMock.Setup(x => x.SerializeAsync(It.IsAny<object>()))
                .ReturnsAsync(body)
                .Verifiable();
            _basicPropertiesMock.Setup(x => x.Headers)
                .Returns(headersMock.Object)
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            List<BatchItem> callbackItems = null;

            _busConnection.PublishSuccessed += items =>
            {
                callbackItems = items.ToList();
                autoResetEvent.Set();
            };
            _busConnection.Publish(exchange, queue, routingKey, notification);

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(5));

            _publishBatchMock.VerifyAll();

            callbackItems.Should().NotBeNull();
            callbackItems.Should().NotBeEmpty();
        }

        [Fact]
        public void GivenConnectionWhenSubscribeShouldExecuteCallbackAndAckOnSuccess()
        {
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");
            const ushort deliveryTag = 1;

            _busSerializerMock.Setup(x => x.DeserializeAsync<string>(body))
                .ReturnsAsync("test")
                .Verifiable();

            _channelMock.Setup(x => x.BasicConsume(
                    queue.Name.Value,
                    false,
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<IBasicConsumer>()))
                .Callback((string queueName, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                    IDictionary<string, object> _, IBasicConsumer consumer) =>
                {
                    ((AsyncEventingBasicConsumer) consumer).HandleBasicDeliver(
                        consumerTag,
                        deliveryTag,
                        false,
                        exchange.Name.Value,
                        routingKey.Value,
                        _basicPropertiesMock.Object,
                        body).Wait();
                })
                .Returns(Guid.NewGuid().ToString());

            var isExecuted = false;
            var autoResetEvent = new AutoResetEvent(false);
            _busConnection.Subscribe<string>(exchange, queue, routingKey, 10, (scope, @event) =>
            {
                isExecuted = true;
                return Task.CompletedTask;
            });

            _channelMock.Setup(x => x.BasicAck(1, false))
                .Callback((ulong tag, bool multiple) => autoResetEvent.Set())
                .Verifiable();

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(30));

            isExecuted.Should().BeTrue();
            _channelMock.Verify(x => x.BasicQos(0, 10, false), Times.Once());
            _channelMock.Verify(x => x.BasicConsume(
                queue.Name.Value,
                false,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<IBasicConsumer>()), Times.Once());
            _channelMock.Verify(x => x.BasicAck(deliveryTag, false), Times.Once());
        }

        [Fact]
        public void GivenConnectionWhenSubscribeShouldExecuteCallbackAndNackOnFailure()
        {
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");
            const ushort deliveryTag = 1;

            _busSerializerMock.Setup(x => x.DeserializeAsync<string>(body))
                .ReturnsAsync("test")
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _channelMock.Setup(x => x.BasicConsume(
                    queue.Name.Value,
                    false,
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<IBasicConsumer>()))
                .Callback((string queueName, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                    IDictionary<string, object> _, IBasicConsumer consumer) =>
                {
                    ((AsyncEventingBasicConsumer) consumer).HandleBasicDeliver(
                        consumerTag,
                        deliveryTag,
                        false,
                        exchange.Name.Value,
                        routingKey.Value,
                        _basicPropertiesMock.Object,
                        body).Wait();
                })
                .Returns(Guid.NewGuid().ToString());

            _channelMock.Setup(x => x.BasicAck(deliveryTag, false))
                .Callback((ulong tag, bool multiple) => autoResetEvent.Set())
                .Verifiable();

            _busConnection.Subscribe<string>(
                exchange,
                queue,
                routingKey,
                10,
                (scope, @event) => throw new Exception());

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(5));

            _channelMock.Verify(x => x.BasicAck(deliveryTag, false), Times.Once());
        }

        [Fact]
        public void GivenConnectionWhenSubscribeShouldExecuteCallbackAndRetryOnFailure()
        {
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");
            const ushort deliveryTag = 1;

            var loggerMock = new Mock<IBusLogger>();

            _busSerializerMock.Setup(x => x.DeserializeAsync<string>(body))
                .ReturnsAsync("test")
                .Verifiable();
            _serviceProviderMock.Setup(x => x.GetService(typeof(IBusLogger)))
                .Returns(loggerMock.Object)
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _channelMock.Setup(x => x.BasicConsume(
                    queue.Name.Value,
                    false,
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<IBasicConsumer>()))
                .Callback((string queueName, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                    IDictionary<string, object> _, IBasicConsumer consumer) =>
                {
                    ((AsyncEventingBasicConsumer) consumer).HandleBasicDeliver(
                        consumerTag,
                        deliveryTag,
                        false,
                        exchange.Name.Value,
                        routingKey.Value,
                        _basicPropertiesMock.Object,
                        body).Wait();
                })
                .Returns(Guid.NewGuid().ToString());

            _publishBatchMock.Setup(x => x.Publish())
                .Callback(() => autoResetEvent.Set())
                .Verifiable();

            _busConnection.Subscribe<string>(
                exchange,
                queue,
                routingKey,
                10,
                (scope, @event) => throw new Exception());

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(5));

            _publishBatchMock.VerifyAll();
            _channelMock.Verify(x => x.QueueDeclare(
                It.Is((string y) => y.EndsWith("-retry")),
                true,
                false,
                false,
                It.Is<IDictionary<string, object>>(args =>
                    args["x-dead-letter-exchange"].Equals(ExchangeName.Default.Value)
                    && args["x-dead-letter-routing-key"].Equals(queue.Name.Value))), Times.Once());
            _publishBatchMock.Verify(x => x.Add(
                Exchange.Default.Name.Value,
                It.Is((string y) => y.StartsWith(queue.Name.Value) && y.EndsWith("-retry")),
                false,
                _basicPropertiesMock.Object,
                It.IsAny<byte[]>()), Times.Once());
        }

        [Fact]
        public void GivenConnectionWhenSubscribeShouldExecuteCallbackLogOnFailure()
        {
            var exchange = Exchange.Create("test", ExchangeType.Direct);
            var queue = Queue.Create("test.requested");
            var routingKey = RoutingKey.Create("test.route");
            var body = Encoding.UTF8.GetBytes("test");
            const ushort deliveryTag = 1;

            _busSerializerMock.Setup(x => x.DeserializeAsync<string>(body))
                .ReturnsAsync("test")
                .Verifiable();
            
            _basicPropertiesMock
                .Setup(x => x.IsHeadersPresent())
                .Returns(true)
                .Verifiable();
            _basicPropertiesMock
                .SetupGet(x => x.Headers)
                .Returns(new Dictionary<string, object>
                {
                    { "MaxAttempts", "1" }
                })
                .Verifiable();

            var autoResetEvent = new AutoResetEvent(false);

            _channelMock.Setup(x => x.BasicConsume(
                    queue.Name.Value,
                    false,
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<IBasicConsumer>()))
                .Callback((string queueName, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                    IDictionary<string, object> _, IBasicConsumer consumer) =>
                {
                    ((AsyncEventingBasicConsumer) consumer).HandleBasicDeliver(
                        consumerTag,
                        deliveryTag,
                        false,
                        exchange.Name.Value,
                        routingKey.Value,
                        _basicPropertiesMock.Object,
                        body).Wait();
                })
                .Returns(Guid.NewGuid().ToString());

            _loggerMock.Setup(x => x.WriteException(
                    It.IsAny<string>(),
                    It.IsAny<Exception>(),
                    It.IsAny<KeyValuePair<string, object>[]>()))
                .Callback((string name, Exception exception, KeyValuePair<string, Object>[] properties) =>
                    autoResetEvent.Set())
                .Verifiable();

            _busConnection.Subscribe<string>(
                exchange,
                queue,
                routingKey,
                10,
                (scope, @event) => throw new Exception());

            autoResetEvent.WaitOne(TimeSpan.FromSeconds(30));

            _loggerMock.Verify(x => x.WriteException(It.IsAny<string>(), It.IsAny<Exception>(),
                It.IsAny<KeyValuePair<string, object>[]>()));
        }
    }
}