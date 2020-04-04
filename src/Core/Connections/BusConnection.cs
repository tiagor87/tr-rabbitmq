using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BufferList;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TRRabbitMQ.Core.Builders;
using TRRabbitMQ.Core.Extensions;
using TRRabbitMQ.Core.Messages;
using TRRabbitMQ.Core.Messages.Implementations;
using TRRabbitMQ.Core.Models;
using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Utils;

namespace TRRabbitMQ.Core.Connections
{
    public delegate void PublishSuccessed(IEnumerable<BatchItem> items);

    public delegate void PublishFailed(IEnumerable<BatchItem> items, Exception exception);

    public class BusConnection : IDisposable
    {
        private static volatile object _sync = new object();
        
        private bool _disposed;
        private readonly IConnectionFactory _connectionFactory;
        private readonly ConcurrentDictionary<string, (IModel, AsyncEventingBasicConsumer, List<Task>)> _consumers;
        private readonly IBusLogger _logger;
        private readonly IOptions<BusConnectionOptions> _options;
        private readonly BufferList<BatchItem> _publisherBuffer;
        private readonly IBusSerializer _serializer;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private IConnection _consumerConnection;
        private IConnection _publisherConnection;
        private List<Task> _tasks = new List<Task>();

        public BusConnection(IConnectionFactory connectionFactory,
            IBusSerializer serializer,
            IServiceScopeFactory serviceScopeFactory,
            IOptions<BusConnectionOptions> options,
            IBusLogger logger = null)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _options = options;
            _consumers = new ConcurrentDictionary<string, (IModel, AsyncEventingBasicConsumer, List<Task>)>();
            _connectionFactory = connectionFactory;
            _publisherBuffer =
                new BufferList<BatchItem>(_options.Value.PublisherBufferSize,
                    TimeSpan.FromMilliseconds(_options.Value.PublisherBufferTtlInMilliseconds));
            _publisherBuffer.Cleared += PublishBufferOnCleared;
        }

        public BusConnection(BusConnectionString connectionString,
            IBusSerializer serializer,
            IServiceScopeFactory serviceScopeFactory,
            IOptions<BusConnectionOptions> options,
            IBusLogger logger = null) : this(GetConnectionFactory(connectionString.Value), serializer,
            serviceScopeFactory, options, logger)
        {
        }

        public IConnection PublisherConnection
        {
            get
            {
                if (_publisherConnection != null)
                {
                    return _publisherConnection;
                }

                lock (_sync)
                {
                    return _publisherConnection ?? (_publisherConnection = Policy.Handle<Exception>()
                               .WaitAndRetry(_options.Value.ConnectionMaxRetry,
                                   _ => TimeSpan.FromMilliseconds(_options.Value.ConnectionRetryDelayInMilliseconds))
                               .Execute(() => _connectionFactory.CreateConnection()));
                }
            }
        }

        public IConnection ConsumerConnection
        {
            get
            {
                if (_consumerConnection != null)
                {
                    return _consumerConnection;
                }

                lock (_sync)
                {
                    return _consumerConnection ?? (_consumerConnection = Policy.Handle<Exception>()
                               .WaitAndRetry(
                                   _options.Value.ConnectionMaxRetry,
                                   _ => TimeSpan.FromMilliseconds(_options.Value.ConnectionRetryDelayInMilliseconds))
                               .Execute(() => _connectionFactory.CreateConnection()));
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event PublishSuccessed PublishSuccessed;
        public event PublishFailed PublishFailed;

        public void Declare(Exchange exchange, Queue queue, params RoutingKey[] routingKeys)
        {
            using (var channel = PublisherConnection.CreateModel())
            {
                exchange.Declare(channel);
                queue.Declare(channel);
                foreach (var key in routingKeys)
                {
                    queue.Bind(channel, exchange, key);
                }

                channel.Close();
            }
        }

        public void Subscribe<T>(Exchange exchange, Queue queue, RoutingKey routingKey, ushort prefetchCount,
            Func<IServiceScope, IConsumerMessage, Task> action, bool autoAck = true)
        {
            var channel = ConsumerConnection.CreateModel();
            channel.BasicQos(0, prefetchCount, false);
            Declare(exchange, queue, routingKey);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += (sender, args) =>
            {
                WaitForFreeSlots();
                try
                {
                    var task = Task.Run(async () =>
                    {
                        var builder = new MessageBuilder(channel, _serializer);
                        var message = builder
                            .SetOnSuccess(CompleteMessage)
                            .SetOnRetry(RetryMessage)
                            .SetOnFail(FailMessage)
                            .SetExchange(exchange)
                            .SetQueue(queue)
                            .SetEvent(args)
                            .Build();
                        try
                        {
                            using (var scope = _serviceScopeFactory.CreateScope())
                            {
                                try
                                {
                                    await action.Invoke(scope, message);
                                    if (autoAck) message.Success();
                                }
                                catch (SerializationException exception)
                                {
                                    _logger?.WriteException(nameof(Subscribe), exception,
                                        new KeyValuePair<string, object>("Event", Encoding.UTF8.GetString(args.Body)));
                                    FailMessage(message);
                                    channel.BasicNack(args.DeliveryTag, false, false);
                                }
                                catch (Exception exception)
                                {
                                    message.Fail(exception);
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            _logger?.WriteException(nameof(Subscribe), exception,
                                new KeyValuePair<string, object>("Event", Encoding.UTF8.GetString(args.Body)));
                            FailMessage(message);
                            channel.BasicNack(args.DeliveryTag, false, false);
                        }
                    });
                    lock (_sync)
                    {
                        _tasks.Add(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.WriteException(typeof(T).Name, ex,
                        new KeyValuePair<string, object>("args", args));
                    channel.BasicNack(args.DeliveryTag, false, true);
                }

                return Task.CompletedTask;
            };

            var consumerTag = channel.BasicConsume(queue.Name.Value, false, consumer);
            _consumers.GetOrAdd(consumerTag, (channel, consumer, _tasks));
        }

        public void Publish(IPublishMessage publishMessage)
        {
            _publisherBuffer.Add(new BatchItem(publishMessage.Options.Exchange, publishMessage.Options.Queue, publishMessage.Options.RoutingKey, publishMessage));
        }

        public void Publish(Exchange exchange, Queue queue, RoutingKey routingKey, object notification)
        {
            var options = new MessageOptions(exchange, queue, routingKey,  _options.Value.MessageMaxRetry, _serializer);
            var message = new PublishMessage(options, notification, 0);
            Publish(message);
        }

        public void Publish(Exchange exchange, RoutingKey routingKey, object notification)
        {
            var options = new MessageOptions(exchange, null, routingKey, _options.Value.MessageMaxRetry, _serializer);
            var message = new PublishMessage(options, notification, 0);
            Publish(message);
        }

        public void PublishBatch(Exchange exchange, Queue queue, RoutingKey routingKey,
            IEnumerable<object> notifications)
        {
            var options = new MessageOptions(exchange, queue, routingKey, _options.Value.MessageMaxRetry, _serializer);
            var messages = notifications.Select(notification => new PublishMessage(options, notification, 0));
            PublishBatch(messages);
        }

        public void PublishBatch(IEnumerable<IPublishMessage> messages)
        {
            var batches = messages.Select(message => new BatchItem(message.Options.Exchange, message.Options.Queue, message.Options.RoutingKey, message));
            foreach (var item in batches)
            {
                _publisherBuffer.Add(item);
            }
        }

        ~BusConnection()
        {
            Dispose(false);
        }

        private void WaitForFreeSlots()
        {
            while (_tasks.Count >= _options.Value.ConsumerMaxParallelTasks)
            {
                lock (_sync)
                {
                    _tasks.RemoveAll(x => x.IsCompleted || x.IsCanceled || x.IsFaulted);
                }

                Thread.Sleep(100);
            }
        }

        private static IConnectionFactory GetConnectionFactory(Uri connectionString)
        {
            return new ConnectionFactory
            {
                Uri = connectionString,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                RequestedHeartbeat = 30,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                DispatchConsumersAsync = true
            };
        }

        private void CompleteMessage(IConsumerMessage consumerMessage)
        {
            var message = consumerMessage as IRabbitMqConsumerMessage;
            message?.Channel.BasicAck(message.Event.DeliveryTag, false);
        }

        private void RetryMessage(IConsumerMessage consumerMessage)
        {
            var retryQueue = consumerMessage.Options.Queue
                .CreateRetryQueue(TimeSpan.FromMinutes(1));
            var retryRoutingKey = RoutingKey.Create(retryQueue.Name.Value);
            Publish(Exchange.Default, retryQueue, retryRoutingKey, consumerMessage);
            consumerMessage.Success();
        }

        private void FailMessage(IConsumerMessage message)
        {
            var failedQueue = message.Options.Queue
                .CreateFailedQueue();
            var failedRoutingKey = RoutingKey.Create(failedQueue.Name.Value);
            Publish(Exchange.Default, failedQueue, failedRoutingKey, message);
            message.Success();
            _logger?.WriteException(nameof(FailMessage), message.Error, new KeyValuePair<string, object>("Event", message));
        }

        private void PublishBufferOnCleared(IEnumerable<BatchItem> removedItems)
        {
            var items = removedItems.ToList();
            try
            {
                Policy
                    .Handle<Exception>()
                    .WaitAndRetry(
                        _options.Value.PublishMaxRetry,
                        _ => TimeSpan.FromMilliseconds(_options.Value.PublishRetryDelayInMilliseconds),
                        (exception, span) =>
                        {
                            _logger?.WriteException("Publisher", exception,
                                new KeyValuePair<string, object>("Events", removedItems));
                        })
                    .Execute(() =>
                    {
                        using (var channel = PublisherConnection.CreateModel())
                        {
                            var batch = channel.CreateBasicPublishBatch();
                            try
                            {
                                foreach (var group in items.GroupBy(x => (x.Exchange, x.Queue, x.RoutingKey)))
                                {
                                    var (exchange, queue, routingKey) = group.Key;
                                    exchange.Declare(channel);
                                    queue?.Declare(channel);
                                    queue?.Bind(channel, exchange, routingKey);
                                    foreach (var item in group)
                                    {
                                        var message = item.Message;
                                        var basicProperties = channel.CreateBasicProperties();
                                        basicProperties.AddAttemptHeaders(message);
                                        batch.Add(exchange.Name.Value, routingKey.Value, false,
                                            basicProperties, message.GetBody(_serializer));
                                    }
                                }

                                batch.Publish();
                            }
                            finally
                            {
                                channel.Close();
                            }
                        }
                    });

                PublishSuccessed?.Invoke(items);
            }
            catch (Exception ex)
            {
                _logger?.WriteException(nameof(PublishBufferOnCleared), ex,
                    new KeyValuePair<string, object>("Events", items));
                PublishFailed?.Invoke(items, ex);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                foreach (var consumerGroup in _consumers)
                {
                    var key = consumerGroup.Key;
                    var value = consumerGroup.Value;
                    var (channel, _, buffer) = value;
                    buffer.Clear();
                    channel.BasicCancel(key);
                    channel.Close();
                    channel.Dispose();
                }

                _publisherBuffer.Clear();
                _consumerConnection?.Close();
                _consumerConnection?.Dispose();
                _publisherConnection?.Close();
                _publisherConnection?.Dispose();
            }

            _disposed = true;
        }
    }
}