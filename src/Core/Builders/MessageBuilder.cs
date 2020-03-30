﻿using System;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TRRabbitMQ.Core.Messages;
using TRRabbitMQ.Core.Messages.Implementations;
using TRRabbitMQ.Core.Models;
using TRRabbitMQ.Core.Options;
using TRRabbitMQ.Core.Utils;

namespace TRRabbitMQ.Core.Builders
{
    public class MessageBuilder
    {
        private readonly IModel _channel;
        private readonly IBusSerializer _serializer;
        private Action<IConsumerMessage> _onFail;
        private Action<IConsumerMessage> _onSuccess;
        private BasicDeliverEventArgs _event;
        private Exchange _exchange;
        private Queue _queue;
        private int _maxAttempts;
        private int _attemptCount;
        private Action<IConsumerMessage> _onRetry;

        public MessageBuilder(IModel channel, IBusSerializer serializer)
        {
            _channel = channel;
            _serializer = serializer;
        }

        public MessageBuilder SetOnFail(Action<IConsumerMessage> onFail)
        {
            _onFail = onFail;
            return this;
        }
        
        public MessageBuilder SetOnRetry(Action<IConsumerMessage> onRetry)
        {
            _onRetry = onRetry;
            return this;
        }

        public MessageBuilder SetOnSuccess(Action<IConsumerMessage> onSuccess)
        {
            _onSuccess = onSuccess;
            return this;
        }

        public MessageBuilder SetEvent(BasicDeliverEventArgs @event)
        {
            _event = @event;
            _maxAttempts = GetHeaderValue("MaxAttempts", 5);
            _attemptCount = GetHeaderValue("AttemptCount", 0);
            _attemptCount++; // The header loads last attempt. This is a new one.
            
            return this;
        }

        public MessageBuilder SetExchange(Exchange exchange)
        {
            _exchange = exchange;
            return this;
        }
        
        public MessageBuilder SetQueue(Queue queue)
        {
            _queue = queue;
            return this;
        }

        public IRabbitMqConsumerMessage Build()
        {
            var options = new MessageOptions(_exchange, _queue, null, _maxAttempts, _serializer); 
            return new RabbitMqConsumerMessage(
                options,
                _event.Body,
                _attemptCount,
                _onSuccess,
                _onRetry,
                _onFail,
                _channel,
                _event);
        }

        private int GetHeaderValue(string key, int defaultValue)
        {
            var value = defaultValue;
            if (_event.BasicProperties.IsHeadersPresent() &&
                _event.BasicProperties.Headers.TryGetValue(key, out var text))
            {
                value = int.Parse(text.ToString());
            }

            return value;
        }
    }
}