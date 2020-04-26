using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TRRabbitMQ.Core.Connections;
using TRRabbitMQ.Core.Extensions;
using TRRabbitMQ.Core.Messages;
using TRRabbitMQ.Core.Messages.Implementations;
using TRRabbitMQ.Core.Models;
using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Utils;

namespace TRRabbitMQ.Core.Consumers
{
    public interface IConsumerOptions
    {
        Exchange Exchange { get; }
        Queue Queue { get; }
        IEnumerable<RoutingKey> RoutingKeys { get; }
        ushort PrefetchCount { get; }
        bool AutoAck { get; }
        TimeSpan RetryTtl { get; }
    }

    public interface IConsumerOptions<T> : IConsumerOptions
    {
    }

    public class ConsumerOptions<T> : IConsumerOptions<T>
    {
        public ConsumerOptions(
            Exchange exchange,
            Queue queue,
            ushort prefetchCount,
            bool autoAck,
            TimeSpan retryTtl,
            params RoutingKey[] routingKeys)
        {
            Exchange = exchange;
            Queue = queue;
            RoutingKeys = routingKeys;
            PrefetchCount = prefetchCount;
            AutoAck = autoAck;
            RetryTtl = retryTtl;
        }
        public ConsumerOptions(
            Exchange exchange,
            Queue queue,
            ushort prefetchCount = 1,
            bool autoAck = true,
            params RoutingKey[] routingKeys) : this(exchange, queue, prefetchCount, autoAck, TimeSpan.FromMinutes(1), routingKeys)
        {
        }

        public Exchange Exchange { get; }
        public Queue Queue { get; }
        public IEnumerable<RoutingKey> RoutingKeys { get; }
        public ushort PrefetchCount { get; }
        public bool AutoAck { get; }
        public TimeSpan RetryTtl { get; }
    }

    public interface IConsumerHandler<T>
    {
        Task ExecuteAsync(T message);
    }
    
    static class ConsumerController
    {
        private static int _running = 0;
        private static int _capacity = 100;

        public static void SetRunning(Task task)
        {
            Interlocked.Increment(ref _running);
            task.ContinueWith(t =>
            {
                t.Dispose();
                Interlocked.Decrement(ref _running);
            }).ConfigureAwait(false);
        }
        
        public static void SetDone() => Interlocked.Decrement(ref _running);
        
        public static async Task Wait()
        {
            while (_running >= _capacity)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }

        public static void Init(int capacity)
        {
            _capacity = capacity;
        }
    } 
    
    public class Consumer<T>
    {
        private readonly IConsumerOptions<T> _options;
        private readonly BusConnection _connection;
        private IServiceScopeFactory _scopeFactory;
        private IBusLogger _logger;
        private IBusSerializer _serializer;
        private IModel _channel;
        private string _consumerTag;
        private AsyncEventingBasicConsumer _consumer;

        public Consumer(
            BusConnection connection,
            IConsumerOptions<T> options,
            IModel channel,
            IBusSerializer serializer,
            IBusLogger logger,
            IServiceScopeFactory scopeFactory)
        {
            _connection = connection;
            _scopeFactory = scopeFactory;
            _options = options;
            _channel = channel;
            _serializer = serializer;
            _logger = logger;
        }

        private void Fail(BasicDeliverEventArgs args)
        {
            var failedQueue = _options.Queue.CreateFailedQueue();
            _connection.Publish(Exchange.Default, failedQueue, RoutingKey.Create(failedQueue.Name.Value), args.Body);
        }

        private void TryRequeue(BasicDeliverEventArgs args)
        {
            var maxAttempts = args.BasicProperties.GetMaxAttempts();
            var attemptCount = args.BasicProperties.GetAttemptCount();
            if (maxAttempts == attemptCount + 1)
            {
                Fail(args);
                return;
            }
            
            var retryQueue = _options.Queue.CreateRetryQueue(_options.RetryTtl);
            var message = new PublishMessage(
                new MessageOptions(
                    _options.Exchange,
                    retryQueue,
                    RoutingKey.Create(retryQueue.Name.Value),
                    maxAttempts),
                args.Body,
                attemptCount);
            _connection.Publish(message);
        }

        private void Done(BasicDeliverEventArgs args)
        {
            _channel.BasicAck(args.DeliveryTag, false);
        }
        

        private async Task Receive(object sender, BasicDeliverEventArgs args)
        {
            try
            {
                await ConsumerController.Wait();
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var message = _serializer.Deserialize<T>(args.Body);
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var handler = scope.ServiceProvider.GetService<IConsumerHandler<T>>();
                            try
                            {
                                await handler.ExecuteAsync(message);
                                if (_options.AutoAck)
                                {
                                    Done(args);
                                }
                            }
                            catch (SerializationException exception)
                            {
                                _logger?.WriteException(nameof(Subscribe), exception,
                                    new KeyValuePair<string, object>("Event", Encoding.UTF8.GetString(args.Body)));
                                Fail(args);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger?.WriteException(nameof(Subscribe), exception,
                            new KeyValuePair<string, object>("Event", Encoding.UTF8.GetString(args.Body)));
                        TryRequeue(args);
                    }
                });
                ConsumerController.SetRunning(task);
            }
            catch (Exception ex)
            {
                _logger?.WriteException(typeof(T).Name, ex,
                    new KeyValuePair<string, object>("args", args));
                _channel.BasicNack(args.DeliveryTag, false, true);
            }
        }
        
        public void Subscribe()
        {
            if (!string.IsNullOrWhiteSpace(_consumerTag))
            {
                return;
            }
            _channel.BasicQos(0, _options.PrefetchCount, false);
            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.Received += Receive;
            _consumerTag = _channel.BasicConsume(_options.Queue.Name.Value, false, _consumer);
        }

        public void Unsubscribe()
        {
            if (string.IsNullOrWhiteSpace(_consumerTag))
            {
                return;
            }
            _channel.BasicCancel(_consumerTag);
            _consumerTag = null;
        }
    }
}